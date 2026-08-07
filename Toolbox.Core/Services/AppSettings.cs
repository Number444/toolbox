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

    private string _remoteControlDefaultPort = "8090";
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
                    key.SetValue("Toolbox", $"\"{exePath}\"");
            }
            else
            {
                if (key.GetValue("Toolbox") != null)
                    key.DeleteValue("Toolbox", false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 开机自启注册表写入失败: {ex.Message}");
        }
    }

    public AppSettings() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Toolbox"))
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
        public bool? MouseHaloEnabled { get; set; }
        public bool? ControlGlowEnabled { get; set; }
        public bool AutoStartRemoteControl { get; set; }
        public string? RemoteControlDefaultPort { get; set; }
        public string? RemoteControlDefaultKey { get; set; }
    }
}