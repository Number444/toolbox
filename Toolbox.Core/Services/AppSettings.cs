using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

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
        var path = SettingsPath;
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data != null)
            {
                _minimizeOnClose = data.MinimizeOnClose;
                OnPropertyChanged(nameof(MinimizeOnClose));

                _autoOpenFloatWindow = data.AutoOpenFloatWindow;
                OnPropertyChanged(nameof(AutoOpenFloatWindow));

                if (!string.IsNullOrEmpty(data.MusicFloatSizeMode))
                {
                    _musicFloatSizeMode = data.MusicFloatSizeMode;
                    OnPropertyChanged(nameof(MusicFloatSizeMode));
                }
            }
        }
        catch { /* 文件损坏，忽略，保留默认值 */ }
    }

    public void Save()
    {
        var data = new SettingsData
        {
            MinimizeOnClose = _minimizeOnClose,
            AutoOpenFloatWindow = _autoOpenFloatWindow,
            MusicFloatSizeMode = _musicFloatSizeMode
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed class SettingsData
    {
        public bool MinimizeOnClose { get; set; }
        public bool AutoOpenFloatWindow { get; set; }
        public string? MusicFloatSizeMode { get; set; }
    }
}