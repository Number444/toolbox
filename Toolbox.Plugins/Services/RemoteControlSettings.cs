using System.IO;
using Toolbox.Core.Services;

namespace Toolbox.Plugins.Services;

/// <summary>
/// 远程控制工具独立设置（存储 %LOCALAPPDATA%/Toolbox/remote-control.json，与 AppSettings 解耦，参照 AudioflowSettings 惯例）。
/// 含上次使用的密钥明文（用户决策 2026-08-08：密钥落盘换取"上次密钥自动回填"便利，UI 明示警告）。
/// </summary>
public sealed class RemoteControlSettings
{
    private static readonly Lazy<RemoteControlSettings> _instance = new(() => new RemoteControlSettings());
    public static RemoteControlSettings Instance => _instance.Value;

    private readonly string _settingsPath;

    private string _lastKey = "";
    /// <summary>上次使用的密钥（明文落盘；输入框回填用）</summary>
    public string LastKey
    {
        get => _lastKey;
        set { _lastKey = value; Save(); }
    }

    private string _lastPort = "8090";
    /// <summary>上次使用的端口（输入框回填用）</summary>
    public string LastPort
    {
        get => _lastPort;
        set { _lastPort = value; Save(); }
    }

    private bool _autoGenerateKey = true;
    /// <summary>无密钥时自动生成随机密钥开关</summary>
    public bool AutoGenerateKey
    {
        get => _autoGenerateKey;
        set { _autoGenerateKey = value; Save(); }
    }

    public RemoteControlSettings() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Toolbox", "remote-control.json"))
    { }

    internal RemoteControlSettings(string path)
    {
        _settingsPath = path;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception) { /* 目录创建失败不影响构造 */ }

        var data = JsonSettingsFile.Load<SettingsData>(path);
        if (data == null) return;
        _lastKey = data.LastKey ?? "";
        _lastPort = data.LastPort ?? "8090";
        _autoGenerateKey = data.AutoGenerateKey ?? true;
    }

    private void Save()
    {
        try
        {
            JsonSettingsFile.Save(_settingsPath, new SettingsData
            {
                LastKey = _lastKey,
                LastPort = _lastPort,
                AutoGenerateKey = _autoGenerateKey
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteControlSettings] 保存失败: {ex.Message}");
        }
    }

    private sealed class SettingsData
    {
        public string? LastKey { get; set; }
        public string? LastPort { get; set; }
        public bool? AutoGenerateKey { get; set; }
    }
}
