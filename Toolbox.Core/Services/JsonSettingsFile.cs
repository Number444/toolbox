using System.IO;
using System.Text.Json;

namespace Toolbox.Core.Services;

/// <summary>
/// JSON 设置文件读写 —— AppSettings / AudioflowSettings 共用的文件 IO 帮助类。
/// 只抽象文件读写，不统一存盘时机（各设置类的 setter 自动存盘策略保持不变）。
/// 写入采用「临时文件 + 备份 + 原子替换」：中途断电/杀进程不会留下截断文件。
/// </summary>
public static class JsonSettingsFile
{
    /// <summary>
    /// 读写共用 options。AllowNamedFloatingPointLiterals：设置类中未初始化的 double 坐标为
    /// NaN 时也能正常序列化/反序列化（此前 NaN 序列化抛 JsonException → 整个设置文件保存
    /// 静默失败，2026-08-03 审查高危；如 AudioflowSettings.FloatWindowLeft 默认 NaN）。
    /// </summary>
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <summary>读取并反序列化 JSON 设置文件；文件不存在或任何异常（损坏/占用）时回落 .bak，仍失败返回 default</summary>
    public static T? Load<T>(string path)
    {
        var result = TryLoad<T>(path);
        if (result.loaded) return result.value;

        // 主文件损坏/读取失败，回落备份
        var bak = TryLoad<T>(path + ".bak");
        if (bak.loaded)
        {
            System.Diagnostics.Debug.WriteLine($"[JsonSettingsFile] 主文件损坏，已回落备份: {path}");
            return bak.value;
        }
        return default;
    }

    /// <summary>序列化并原子写入 JSON 设置文件（旧文件保留为 .bak）；成功返回 true，失败静默记录并返回 false</summary>
    public static bool Save<T>(string path, T obj)
    {
        var tmp = path + ".tmp";
        var bak = path + ".bak";
        try
        {
            var json = JsonSerializer.Serialize(obj, IndentedOptions);
            File.WriteAllText(tmp, json);

            // 旧文件先留作备份，再原子替换；任一步失败都不破坏现有主文件
            if (File.Exists(path))
                File.Copy(path, bak, overwrite: true);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JsonSettingsFile] 保存失败: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }

    private static (bool loaded, T? value) TryLoad<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return (false, default);
            var json = File.ReadAllText(path);
            // 必须复用同一 options：写出的 "NaN" 字面量只有配 AllowNamedFloatingPointLiterals 才能读回
            return (true, JsonSerializer.Deserialize<T>(json, IndentedOptions));
        }
        catch
        {
            return (false, default);
        }
    }
}
