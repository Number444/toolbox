using System.IO;
using Toolbox.Core.Services;

namespace Toolbox.Plugins.Services;

/// <summary>
/// 远程控制工具独立设置（单一 json：%LOCALAPPDATA%/Toolbox/remote-control.json，与 AppSettings 解耦，参照 AudioflowSettings 惯例）。
/// 内容：上次密钥/端口、自动生成开关、曾连接设备表（用户决策 2026-08-08：合并为单文件，密钥明文落盘换取"上次密钥自动回填"便利，UI 明示警告）。
/// </summary>
public sealed class RemoteControlSettings
{
    private static readonly Lazy<RemoteControlSettings> _instance = new(() => new RemoteControlSettings());
    public static RemoteControlSettings Instance => _instance.Value;

    private readonly string _settingsPath;
    private readonly object _devicesLock = new();
    private List<DeviceRecord> _knownDevices = new();

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

    // ==================== 曾连接设备表（认证成功即记录；自动填密钥/设备列表/踢出共用） ====================

    /// <summary>曾连接设备快照（IP、设备名、首次/最后时间）</summary>
    public IReadOnlyList<DeviceRecord> KnownDevices
    {
        get { lock (_devicesLock) return _knownDevices.ToArray(); }
    }

    /// <summary>已记录设备（IP 匹配）？——控制页自动填密钥判断</summary>
    public bool IsKnownDevice(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        lock (_devicesLock) return _knownDevices.Any(d => d.Ip == ip);
    }

    /// <summary>记录/更新设备（认证成功时调用）；同 IP 刷新设备名与最后时间</summary>
    public void RecordDevice(string ip, string deviceName)
    {
        if (string.IsNullOrEmpty(ip) || ip == "unknown") return;
        lock (_devicesLock)
        {
            var now = DateTime.Now;
            var existing = _knownDevices.FirstOrDefault(d => d.Ip == ip);
            if (existing == null)
                _knownDevices.Add(new DeviceRecord { Ip = ip, DeviceName = deviceName, FirstSeen = now, LastSeen = now });
            else
            {
                existing.DeviceName = deviceName;
                existing.LastSeen = now;
            }
            Save();
        }
    }

    /// <summary>移除设备（踢出时调用；撤销自动填密钥）</summary>
    public void RemoveDevice(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return;
        lock (_devicesLock)
        {
            var removed = _knownDevices.RemoveAll(d => d.Ip == ip);
            if (removed > 0) Save();
        }
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
        if (data.KnownDevices != null) _knownDevices = data.KnownDevices;
    }

    private void Save()
    {
        try
        {
            // 锁内快照（ToList）：序列化在锁外进行，防止并发 RecordDevice 修改同一 List 导致序列化异常（审查 P2-3）
            List<DeviceRecord> devices;
            lock (_devicesLock) devices = _knownDevices.ToList();
            JsonSettingsFile.Save(_settingsPath, new SettingsData
            {
                LastKey = _lastKey,
                LastPort = _lastPort,
                AutoGenerateKey = _autoGenerateKey,
                KnownDevices = devices
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
        public List<DeviceRecord>? KnownDevices { get; set; }
    }
}
