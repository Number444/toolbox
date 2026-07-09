using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Toolbox.Services;

/// <summary>
/// 网易云音乐悬浮窗独立设置
/// 存储在 %LOCALAPPDATA%/Toolbox/audioflow.json
/// 与 AppSettings 解耦，独立保存/加载
/// </summary>
public sealed class AudioflowSettings : INotifyPropertyChanged
{
    private static readonly Lazy<AudioflowSettings> _instance = new(() => new AudioflowSettings());
    public static AudioflowSettings Instance => _instance.Value;

    private readonly string _settingsDir;
    private string SettingsPath => Path.Combine(_settingsDir, "audioflow.json");

    private bool _floatWindowOpacity;
    /// <summary>
    /// 悬浮窗背景透明度 45%
    /// </summary>
    public bool FloatWindowOpacity
    {
        get => _floatWindowOpacity;
        set
        {
            if (_floatWindowOpacity == value) return;
            _floatWindowOpacity = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _lockFloatWindow;
    /// <summary>
    /// 锁定悬浮窗移动
    /// </summary>
    public bool LockFloatWindow
    {
        get => _lockFloatWindow;
        set
        {
            if (_lockFloatWindow == value) return;
            _lockFloatWindow = value;
            OnPropertyChanged();
            Save();
        }
    }

    public AudioflowSettings() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Toolbox"))
    { }

    internal AudioflowSettings(string customDir)
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
            var data = JsonSerializer.Deserialize<AudioflowData>(json);
            if (data != null)
            {
                _floatWindowOpacity = data.FloatWindowOpacity;
                OnPropertyChanged(nameof(FloatWindowOpacity));

                _lockFloatWindow = data.LockFloatWindow;
                OnPropertyChanged(nameof(LockFloatWindow));
            }
        }
        catch { /* 文件损坏，忽略，保留默认值 */ }
    }

    public void Save()
    {
        var data = new AudioflowData
        {
            FloatWindowOpacity = _floatWindowOpacity,
            LockFloatWindow = _lockFloatWindow
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed class AudioflowData
    {
        public bool FloatWindowOpacity { get; set; }
        public bool LockFloatWindow { get; set; }
    }
}