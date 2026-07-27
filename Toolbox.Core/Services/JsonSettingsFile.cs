using System.IO;
using System.Text.Json;

namespace Toolbox.Core.Services;

/// <summary>
/// JSON 设置文件读写 —— AppSettings / AudioflowSettings 共用的文件 IO 帮助类。
/// 只抽象文件读写，不统一存盘时机（各设置类的 setter 自动存盘策略保持不变）。
/// </summary>
public static class JsonSettingsFile
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>读取并反序列化 JSON 设置文件；文件不存在或任何异常（损坏/占用）返回 default</summary>
    public static T? Load<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            // 文件损坏/读取失败，忽略，调用方保留默认值
            return default;
        }
    }

    /// <summary>序列化并写入 JSON 设置文件；成功返回 true，失败静默记录并返回 false</summary>
    public static bool Save<T>(string path, T obj)
    {
        try
        {
            var json = JsonSerializer.Serialize(obj, IndentedOptions);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JsonSettingsFile] 保存失败: {ex.Message}");
            return false;
        }
    }
}
