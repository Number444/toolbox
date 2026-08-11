using System;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Toolbox.Core.Services;

public sealed class AppSettings : INotifyPropertyChanged
{
    private static readonly Lazy<AppSettings> _instance = new(() => new AppSettings());
    public static AppSettings Instance => _instance.Value;

    private readonly string _settingsDir;
    private string SettingsPath => Path.Combine(_settingsDir, "settings.json");

    private string _musicFloatSizeMode = "Large";
    public string MusicFloatSizeMode
    {
        get => _musicFloatSizeMode;
        set
        {
            if (_musicFloatSizeMode == value) return;
            _musicFloatSizeMode = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _minimizeOnClose;
    public bool MinimizeOnClose
    {
        get => _minimizeOnClose;
        set
        {
            if (_minimizeOnClose == value) return;
            _minimizeOnClose = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _autoOpenFloatWindow;
    public bool AutoOpenFloatWindow
    {
        get => _autoOpenFloatWindow;
        set
        {
            if (_autoOpenFloatWindow == value) return;
            _autoOpenFloatWindow = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (_autoStart == value) return;
            _autoStart = value;
            OnPropertyChanged();
            Save();
            SetStartupRegistry(value);
        }
    }

    private bool _autoStartSilent = true;
    /// <summary>开机自启时静默启动（不显示主界面，后台托盘驻留 + 悬浮窗照常）</summary>
    public bool AutoStartSilent
    {
        get => _autoStartSilent;
        set
        {
            if (_autoStartSilent == value) return;
            _autoStartSilent = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _mouseHaloEnabled = true;
    public bool MouseHaloEnabled
    {
        get => _mouseHaloEnabled;
        set
        {
            if (_mouseHaloEnabled == value) return;
            _mouseHaloEnabled = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _controlGlowEnabled = true;
    public bool ControlGlowEnabled
    {
        get => _controlGlowEnabled;
        set
        {
            if (_controlGlowEnabled == value) return;
            _controlGlowEnabled = value;
            OnPropertyChanged();
            Save();
        }
    }

    // ===== 远程控制（2026-08-08 用户授权扩展；设置页提供编辑入口） =====

    private bool _autoStartRemoteControl;
    /// <summary>启动 Toolbox 时自动启动远程控制服务（默认端口/密钥见下）</summary>
    public bool AutoStartRemoteControl
    {
        get => _autoStartRemoteControl;
        set
        {
            if (_autoStartRemoteControl == value) return;
            _autoStartRemoteControl = value;
            OnPropertyChanged();
            Save();
        }
    }

    private string _remoteControlDefaultPort = AppPaths.DefaultRemotePort;
    /// <summary>远程控制默认端口（工具面板端口输入框的初始值）</summary>
    public string RemoteControlDefaultPort
    {
        get => _remoteControlDefaultPort;
        set
        {
            if (_remoteControlDefaultPort == value) return;
            _remoteControlDefaultPort = value;
            OnPropertyChanged();
            Save();
        }
    }

    private string _remoteControlDefaultKey = "";
    /// <summary>远程控制默认密钥（工具面板密钥输入框的初始值；为空则按开关自动生成）</summary>
    public string RemoteControlDefaultKey
    {
        get => _remoteControlDefaultKey;
        set
        {
            if (_remoteControlDefaultKey == value) return;
            _remoteControlDefaultKey = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>
    /// 启动自检：自启注册表值与期望值（当前 exe 路径 + --autostart）不一致时自动重写。
    /// 2026-08-10：v1.6.2 起自启需带 --autostart 参数实现静默启动——旧版本写入的
    /// 裸路径值、升级后安装路径变化，都靠这里自动纠正，**升级用户无需手动重新开关自启**。
    /// 仅当注册表已有值（用户开过自启）时处理；从未开启过的不创建。
    /// </summary>
    public void EnsureStartupRegistryValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            // 读写必须用同一值名（AppPaths.DataFolderName）：曾硬编码 "Toolbox" 导致
            // Debug 版读到正式版的自启值 → 用自己的值名误建 Debug 自启（2026-08-11 修复）
            if (key.GetValue(AppPaths.DataFolderName) is not string current) return; // 未开启自启

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            var expected = $"\"{exePath}\" --autostart";
            if (current == expected) return; // 已是最新格式与路径

            key.SetValue(AppPaths.DataFolderName, expected);
            System.Diagnostics.Debug.WriteLine("[AppSettings] 自启注册表值已自动迁移为带 --autostart 的新格式");
        }
        catch (Exception ex)
        {
            // 杀软拦截/权限不足不影响启动
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 自启注册表值自检失败: {ex.Message}");
        }
    }

    private static void SetStartupRegistry(bool enable)
    {
        // 杀软拦截/权限不足可能抛异常，失败不影响设置项本身
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    // 2026-08-09：带 --autostart 参数，App 据此判断"开机自启"场景
                    //（配合 AutoStartSilent 实现静默启动：不弹主界面、托盘驻留）
                    key.SetValue(AppPaths.DataFolderName, $"\"{exePath}\" --autostart");
            }
            else
            {
                if (key.GetValue(AppPaths.DataFolderName) != null)
                    key.DeleteValue(AppPaths.DataFolderName, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 开机自启注册表写入失败: {ex.Message}");
        }
    }

    public AppSettings() : this(AppPaths.DataDir)
    { }

    // 测试用：可注入自定义目录
    internal AppSettings(string customDir)
    {
        _settingsDir = customDir;
        if (!Directory.Exists(_settingsDir))
            Directory.CreateDirectory(_settingsDir);
    }

    public void Load()
    {
        var data = JsonSettingsFile.Load<SettingsData>(SettingsPath);
        if (data == null) return;

        _minimizeOnClose = data.MinimizeOnClose;
        OnPropertyChanged(nameof(MinimizeOnClose));

        _autoOpenFloatWindow = data.AutoOpenFloatWindow;
        OnPropertyChanged(nameof(AutoOpenFloatWindow));

        if (!string.IsNullOrEmpty(data.MusicFloatSizeMode))
        {
            _musicFloatSizeMode = data.MusicFloatSizeMode;
            OnPropertyChanged(nameof(MusicFloatSizeMode));
        }

        _autoStart = data.AutoStart;
        OnPropertyChanged(nameof(AutoStart));

        // 旧版 settings.json 无此字段（null），默认开启静默
        _autoStartSilent = data.AutoStartSilent ?? true;
        OnPropertyChanged(nameof(AutoStartSilent));

        // 旧版 settings.json 无此字段（null），默认开启
        _mouseHaloEnabled = data.MouseHaloEnabled ?? true;
        OnPropertyChanged(nameof(MouseHaloEnabled));

        _controlGlowEnabled = data.ControlGlowEnabled ?? true;
        OnPropertyChanged(nameof(ControlGlowEnabled));

        _autoStartRemoteControl = data.AutoStartRemoteControl;
        OnPropertyChanged(nameof(AutoStartRemoteControl));

        if (!string.IsNullOrEmpty(data.RemoteControlDefaultPort))
        {
            _remoteControlDefaultPort = data.RemoteControlDefaultPort;
            OnPropertyChanged(nameof(RemoteControlDefaultPort));
        }

        if (data.RemoteControlDefaultKey != null)
        {
            _remoteControlDefaultKey = data.RemoteControlDefaultKey;
            OnPropertyChanged(nameof(RemoteControlDefaultKey));
        }
    }

    public void Save()
    {
        var data = new SettingsData
        {
            MinimizeOnClose = _minimizeOnClose,
            AutoOpenFloatWindow = _autoOpenFloatWindow,
            MusicFloatSizeMode = _musicFloatSizeMode,
            AutoStart = _autoStart,
            AutoStartSilent = _autoStartSilent,
            MouseHaloEnabled = _mouseHaloEnabled,
            ControlGlowEnabled = _controlGlowEnabled,
            AutoStartRemoteControl = _autoStartRemoteControl,
            RemoteControlDefaultPort = _remoteControlDefaultPort,
            RemoteControlDefaultKey = _remoteControlDefaultKey
        };
        JsonSettingsFile.Save(SettingsPath, data);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed class SettingsData
    {
        public bool MinimizeOnClose { get; set; }
        public bool AutoOpenFloatWindow { get; set; }
        public string? MusicFloatSizeMode { get; set; }
        public bool AutoStart { get; set; }
        public bool? AutoStartSilent { get; set; }
        public bool? MouseHaloEnabled { get; set; }
        public bool? ControlGlowEnabled { get; set; }
        public bool AutoStartRemoteControl { get; set; }
        public string? RemoteControlDefaultPort { get; set; }
        public string? RemoteControlDefaultKey { get; set; }
    }
}