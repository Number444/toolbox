using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace Toolbox.Plugins.Helpers;

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
    // 按顺序尝试：华为云镜像优先（实测直连 ~5MB/s，无代理时比官方快 50 倍），失败自动回退官方源。
    // 均为 v3-flatcontainer 格式：{base}/{id小写}/{版本}/{id小写}.{版本}.nupkg
    private static readonly string[] NuGetSources =
    {
        "https://repo.huaweicloud.com/artifactory/api/nuget/v3/nuget-remote",
        "https://api.nuget.org/v3-flatcontainer",
    };
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

            // 下载全部成功后才清理旧版本并整体替换（进度保持单调递增：解压结束为 92）
            progress.Report((93, "清理旧版本…"));
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

        progress.Report((95, "引擎文件已就绪"));
    }

    /// <summary>把各 NuGet 包下载并解压到临时目录（引擎文件 + 模型），供成功后整体替换</summary>
    private static async Task DownloadToStagingAsync(
        string stagingDir, IProgress<(int percent, string status)> progress, CancellationToken ct)
    {
        var stagingModelsDir = Path.Combine(stagingDir, "models");
        Directory.CreateDirectory(stagingModelsDir);

        // ===== 第 1 步：PaddleOCRSharp 包只下载一次，提取 3 部分 (0-70%) =====
        // 1a. managed 封装（必须用 net9.0 版本，net40 等依赖 Newtonsoft.Json 而我们不需要）
        // 1b. 原生 OCR 桥接 DLL（PaddleOCR.dll 是 C++/CLI，依赖 Newtonsoft.Json）
        // 1c. 内置 PP-OCRv5 模型（build/ytLib/inference/）
        var ocrSharpZip = await DownloadPackageAsync("PaddleOCRSharp", PaddleOcrSharpVer,
            "引擎包", 0, 55, progress, ct);
        try
        {
            ExtractFromArchive(ocrSharpZip, stagingDir,
                entry => entry.FullName.Replace('\\', '/') == "lib/net9.0/PaddleOCRSharp.dll",
                _ => "PaddleOCRSharp.dll",
                "引擎封装库", 55, 58, progress, ct);
            ExtractFromArchive(ocrSharpZip, stagingDir,
                entry => entry.FullName.Replace('\\', '/').StartsWith("build/ytLib/")
                         && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                entry => entry.Name,
                "引擎桥接层", 58, 62, progress, ct);
            ExtractFromArchive(ocrSharpZip, stagingModelsDir,
                entry => entry.FullName.Replace('\\', '/').StartsWith("build/ytLib/inference/"),
                entry => entry.FullName.Replace('\\', '/').Substring("build/ytLib/inference/".Length),
                "内置模型", 62, 70, progress, ct);
        }
        finally
        {
            try { File.Delete(ocrSharpZip); } catch { }
        }

        // ===== 第 2 步：Paddle.Runtime 包 (70-90%) =====
        var runtimeZip = await DownloadPackageAsync("Paddle.Runtime.win_x64", PaddleRuntimeVer,
            "推理运行时", 70, 85, progress, ct);
        try
        {
            ExtractFromArchive(runtimeZip, stagingDir,
                entry => entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                         && entry.FullName.Replace('\\', '/').StartsWith("build/win_x64/"),
                entry => entry.Name,
                "推理运行时", 85, 90, progress, ct);
        }
        finally
        {
            try { File.Delete(runtimeZip); } catch { }
        }

        // ===== 第 3 步：Newtonsoft.Json 依赖 (90-92%) =====
        // net9.0 的 PaddleOCRSharp.dll 不需要，但 PaddleOCR.dll 需要
        var jsonZip = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3",
            "JSON 依赖", 90, 91, progress, ct);
        try
        {
            ExtractFromArchive(jsonZip, stagingDir,
                entry => entry.FullName.Replace('\\', '/') == "lib/netstandard2.0/Newtonsoft.Json.dll",
                _ => "Newtonsoft.Json.dll",
                "JSON 依赖", 91, 92, progress, ct);
        }
        finally
        {
            try { File.Delete(jsonZip); } catch { }
        }
    }

    /// <summary>
    /// 下载 NuGet 包到临时 zip 文件。按源列表顺序尝试（华为云镜像优先，失败回退官方）；
    /// 每个源内网络瞬时失败自动重试（最多 3 次，退避）。
    /// 返回 zip 路径，调用方负责删除。
    /// </summary>
    private static async Task<string> DownloadPackageAsync(
        string packageId, string version, string label,
        int baseMin, int baseMax,
        IProgress<(int percent, string status)> progress, CancellationToken ct)
    {
        const int maxRetries = 3;
        Exception? lastError = null;

        foreach (var source in NuGetSources)
        {
            var url = BuildPackageUrl(source, packageId, version);

            for (int attempt = 1; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    progress.Report((baseMin, $"正在连接 {label}…"));

                    using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();

                    long totalBytes = resp.Content.Headers.ContentLength ?? -1;
                    using var stream = await resp.Content.ReadAsStreamAsync(ct);

                    // 响应体读取单独限时：ResponseHeadersRead 模式下 HttpClient.Timeout 不覆盖体读取，
                    // 连接挂死后进度会无限冻结；此超时与用户取消并存
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readCts.CancelAfter(TimeSpan.FromMinutes(20));

                    var tempPath = Path.Combine(Path.GetTempPath(), $"nupkg_{Guid.NewGuid()}.zip");
                    try
                    {
                        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                        {
                            var buffer = new byte[64 * 1024];
                            long downloaded = 0;
                            int lastPct = -1;
                            int read;
                            while ((read = await stream.ReadAsync(buffer, readCts.Token)) > 0)
                            {
                                await fs.WriteAsync(buffer.AsMemory(0, read), readCts.Token);
                                downloaded += read;
                                if (totalBytes > 0)
                                {
                                    int pct = (int)(downloaded * 100L / totalBytes);
                                    if (pct != lastPct) // 节流：百分比变化才上报，避免每 8KB 一次跨线程调用
                                    {
                                        lastPct = pct;
                                        progress.Report((MapPercent(pct, baseMin, baseMax), $"下载 {label}… {pct}%"));
                                    }
                                }
                            }
                        }
                        return tempPath;
                    }
                    catch
                    {
                        try { File.Delete(tempPath); } catch { }
                        throw;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // 用户取消不重试
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    lastError = ex;
                    progress.Report((baseMin, $"连接失败，正在重试（第 {attempt} 次）…"));
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
                catch (Exception ex)
                {
                    lastError = ex; // 当前源重试耗尽，换下一个源
                    progress.Report((baseMin, $"下载源不可用，正在切换…"));
                    break;
                }
            }
        }

        throw lastError ?? new HttpRequestException($"所有下载源均失败（{label}）");
    }

    /// <summary>按 v3-flatcontainer 格式拼接包下载 URL（源与包 ID 大小写无关）</summary>
    private static string BuildPackageUrl(string source, string packageId, string version)
    {
        var id = packageId.ToLowerInvariant();
        return $"{source}/{id}/{version}/{id}.{version}.nupkg";
    }

    /// <summary>
    /// 从已下载的 zip 中按过滤器提取文件到目标目录，报告解压进度。
    /// selector 把条目的包内路径转换为目标相对路径；目录条目（Name 为空）由调用方过滤器决定。
    /// 同步执行：调用方保证在后台线程（解压是纯 CPU/磁盘 IO，无异步变体收益）。
    /// </summary>
    private static void ExtractFromArchive(
        string zipPath, string extractDir,
        Func<ZipArchiveEntry, bool> filter,
        Func<ZipArchiveEntry, string> selector,
        string label, int baseMin, int baseMax,
        IProgress<(int percent, string status)> progress, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        // 只取文件条目（目录条目 Name 为空），先落成列表以便报解压进度
        var candidates = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && filter(e))
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException($"未在包中找到所需文件（{label}）。");

        progress.Report((MapPercent(0, baseMin, baseMax), $"解压 {label}…"));
        int done = 0;
        foreach (var entry in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var targetPath = Path.Combine(extractDir, selector(entry));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);

            done++;
            progress.Report((MapPercent(done * 100 / candidates.Count, baseMin, baseMax), $"解压 {label}… {done}/{candidates.Count}"));
        }
    }

    /// <summary>将 0-100 的子进度映射到 baseMin-baseMax 范围</summary>
    private static int MapPercent(int subPct, int baseMin, int baseMax)
    {
        int range = baseMax - baseMin;
        return baseMin + subPct * range / 100;
    }
}
