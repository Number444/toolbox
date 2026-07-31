using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace Toolbox.Helpers;

/// <summary>
/// 自动从 NuGet.org / 百度 CDN / GitHub 下载并组装 PaddleOCR 高精度引擎。
/// 无需用户自行托管任何文件，所有来源均为公开稳定 URL。
/// </summary>
public static class EngineDownloader
{
    /// <summary>引擎安装目录</summary>
    public static string DefaultEngineDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Toolbox", "PaddleOCR");

    /// <summary>模型子目录</summary>
    public static string ModelsDirectory => Path.Combine(DefaultEngineDirectory, "models");

    /// <summary>引擎是否已下载并完整</summary>
    public static bool IsDownloaded =>
        Directory.Exists(DefaultEngineDirectory) &&
        File.Exists(Path.Combine(DefaultEngineDirectory, "PaddleOCRSharp.dll")) &&
        File.Exists(Path.Combine(DefaultEngineDirectory, "paddle_inference.dll")) &&
        File.Exists(Path.Combine(DefaultEngineDirectory, "Newtonsoft.Json.dll")) &&
        Directory.Exists(ModelsDirectory) &&
        File.Exists(Path.Combine(ModelsDirectory, "ppocr_keys.txt"));

    // ====== 下载源 ======
    private const string NuGetApi = "https://www.nuget.org/api/v2/package";
    private const string PaddleOcrSharpVer = "6.2.0";
    private const string PaddleRuntimeVer = "3.4.0";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// 自动下载并组装引擎。报告进度 (percent 0-100, status)。
    /// 成功返回 true；用户取消抛 OperationCanceledException。
    /// </summary>
    public static async Task DownloadAndExtractAsync(
        IProgress<(int percent, string status)> progress, CancellationToken ct)
    {
        var engineDir = DefaultEngineDirectory;

        // 全部先下载/解压到临时目录，全部成功后整体替换旧引擎目录：
        // 避免下载失败、取消或解压出错时旧引擎已被删除、留下不可用的新引擎。
        var stagingDir = Path.Combine(Path.GetTempPath(), $"paddleocr_staging_{Guid.NewGuid()}");
        try
        {
            await DownloadToStagingAsync(stagingDir, progress, ct);

            // 下载全部成功后才清理旧版本并整体替换
            progress.Report((90, "清理旧版本…"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(engineDir)!);
                if (Directory.Exists(engineDir))
                    Directory.Delete(engineDir, true);
                Directory.Move(stagingDir, engineDir);
            }
            catch (Exception ex)
            {
                throw new IOException($"无法替换旧的引擎目录：{ex.Message}", ex);
            }
        }
        finally
        {
            // 下载失败/取消/替换失败时清理临时目录；替换成功后目录已移走，此处为空操作
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
        }

        progress.Report((100, "安装完成"));
    }

    /// <summary>把各 NuGet 包下载并解压到临时目录（引擎文件 + 模型），供成功后整体替换</summary>
    private static async Task DownloadToStagingAsync(
        string stagingDir, IProgress<(int percent, string status)> progress, CancellationToken ct)
    {
        var stagingModelsDir = Path.Combine(stagingDir, "models");
        Directory.CreateDirectory(stagingModelsDir);

        // ===== 第 1 步：下载 PaddleOCRSharp NuGet (0-20%) =====
        // 提取 managed 封装（必须用 net9.0 版本，net40 等依赖 Newtonsoft.Json 而我们不需要）
        await DownloadNuGetDllAsync("PaddleOCRSharp", PaddleOcrSharpVer, stagingDir, "PaddleOCRSharp.dll",
            entry =>
            {
                var segs = entry.FullName.Replace('\\', '/').Split('/');
                return segs.Length >= 3 && segs[0] == "lib" && segs[1] == "net9.0" && entry.Name == "PaddleOCRSharp.dll";
            },
            "引擎封装库", 0, 8, progress, ct);
        // 提取原生 OCR 桥接 DLL（PaddleOCR.dll 是 C++/CLI，依赖 Newtonsoft.Json）
        await DownloadNuGetDllAsync("PaddleOCRSharp", PaddleOcrSharpVer, stagingDir, null,
            entry =>
            {
                return entry.FullName.Replace('\\', '/').StartsWith("build/ytLib/") && entry.Name.EndsWith(".dll");
            },
            "引擎桥接层", 8, 15, progress, ct);
        // 下载桥接层的 Newtonsoft.Json 依赖（net9.0 的 PaddleOCRSharp.dll 不需要，但 PaddleOCR.dll 需要）
        await DownloadNuGetDllAsync("Newtonsoft.Json", "13.0.3", stagingDir, null,
            entry =>
            {
                var segs = entry.FullName.Replace('\\', '/').Split('/');
                return segs.Length >= 3 && segs[0] == "lib" && segs[1] == "netstandard2.0"
                    && entry.Name == "Newtonsoft.Json.dll";
            },
            "JSON 依赖", 15, 20, progress, ct);

        // ===== 第 2 步：下载 Paddle.Runtime NuGet (20-45%) =====
        await DownloadNuGetDllAsync("Paddle.Runtime.win_x64", PaddleRuntimeVer, stagingDir, null,
            entry =>
            {
                if (!entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    return false;
                return entry.FullName.Replace('\\', '/').StartsWith("build/win_x64/");
            },
            "推理运行时", 20, 45, progress, ct);

        // ===== 第 3 步：提取内置 PP-OCRv5 模型 (45-80%) =====
        await ExtractBuiltInModelsAsync("PaddleOCRSharp", PaddleOcrSharpVer, stagingModelsDir,
            "内置模型", 45, 80, progress, ct);
    }

    /// <summary>从 NuGet 下载管理 DLL 并提取</summary>
    private static async Task DownloadNuGetDllAsync(
        string packageId, string version,
        string extractDir, string? renameTo,
        Func<ZipArchiveEntry, bool> filter,
        string label, int baseMin, int baseMax,
        IProgress<(int percent, string status)> progress, CancellationToken ct)
    {
        var url = $"{NuGetApi}/{packageId}/{version}";
        progress.Report((baseMin, $"正在连接 {label}…"));

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long totalBytes = resp.Content.Headers.ContentLength ?? -1;
        using var stream = await resp.Content.ReadAsStreamAsync(ct);

        var tempPath = Path.Combine(Path.GetTempPath(), $"nupkg_{Guid.NewGuid()}.zip");
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[8192];
                long downloaded = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    if (totalBytes > 0)
                    {
                        int pct = (int)(downloaded * 100L / totalBytes);
                        progress.Report((MapPercent(pct, baseMin, baseMax), $"下载 {label}… {pct}%"));
                    }
                }
            }

            ct.ThrowIfCancellationRequested();
            progress.Report((MapPercent(5, baseMin, baseMax), $"解压 {label}…"));

            using var archive = ZipFile.OpenRead(tempPath);
            bool found = false;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (!filter(entry)) continue;

                found = true;
                string targetPath = renameTo != null
                    ? Path.Combine(extractDir, renameTo)
                    : Path.Combine(extractDir, entry.Name);

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }

            if (!found)
                throw new InvalidOperationException($"未在 {packageId} 包中找到所需文件。");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>从 PaddleOCRSharp NuGet 包提取内置 PP-OCRv5 模型到 models 目录</summary>
    private static async Task ExtractBuiltInModelsAsync(
        string packageId, string version, string modelsDir, string label,
        int baseMin, int baseMax,
        IProgress<(int percent, string status)> progress, CancellationToken ct)
    {
        var url = $"{NuGetApi}/{packageId}/{version}";
        progress.Report((baseMin, $"正在连接 {label}…"));

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long totalBytes = resp.Content.Headers.ContentLength ?? -1;
        using var stream = await resp.Content.ReadAsStreamAsync(ct);

        var tempPath = Path.Combine(Path.GetTempPath(), $"nupkg_model_{Guid.NewGuid()}.zip");
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[8192];
                long downloaded = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    if (totalBytes > 0)
                    {
                        int pct = (int)(downloaded * 100L / totalBytes);
                        progress.Report((baseMin + pct * (baseMax - baseMin) / 200, $"下载 {label}… {pct}%"));
                    }
                }
            }

            ct.ThrowIfCancellationRequested();
            progress.Report((baseMin + (baseMax - baseMin) / 2, $"解压 {label}…"));

            const string prefix = "build/ytLib/inference/";
            using var archive = ZipFile.OpenRead(tempPath);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var entryPath = entry.FullName.Replace('\\', '/');
                if (!entryPath.StartsWith(prefix) || string.IsNullOrEmpty(entryPath))
                    continue;

                string relativePath = entryPath.Substring(prefix.Length);
                if (string.IsNullOrEmpty(relativePath)) continue; // 前缀目录自身

                var destPath = Path.Combine(modelsDir, relativePath);
                // 目录条目或以 / 结尾的视为目录
                if (string.IsNullOrEmpty(entry.Name) || entryPath.EndsWith("/"))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>将 0-100 的子进度映射到 baseMin-baseMax 范围</summary>
    private static int MapPercent(int subPct, int baseMin, int baseMax)
    {
        int range = baseMax - baseMin;
        return baseMin + subPct * range / 100;
    }

    /// <summary>格式化文件大小</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
