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

    private bool _floatWindowBlurEnabled = true;
    /// <summary>
    /// 悬浮窗 Acrylic 毛玻璃背景开关
    /// </summary>
    public bool FloatWindowBlurEnabled
    {
        get => _floatWindowBlurEnabled;
        set
        {
            if (_floatWindowBlurEnabled == value) return;
            _floatWindowBlurEnabled = value;
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

    private double _floatWindowLeft = double.NaN;
    /// <summary>
    /// 悬浮窗 Left 坐标（NaN 表示未保存，使用默认位置）。注意：不自动存盘，由 Manager 统一管理写入时机。
    /// </summary>
    public double FloatWindowLeft
    {
        get => _floatWindowLeft;
        set
        {
            if (_floatWindowLeft.Equals(value)) return;
            _floatWindowLeft = value;
            OnPropertyChanged();
        }
    }

    private double _floatWindowTop = double.NaN;
    /// <summary>
    /// 悬浮窗 Top 坐标（NaN 表示未保存，使用默认位置）。注意：不自动存盘，由 Manager 统一管理写入时机。
    /// </summary>
    public double FloatWindowTop
    {
        get => _floatWindowTop;
        set
        {
            if (_floatWindowTop.Equals(value)) return;
            _floatWindowTop = value;
            OnPropertyChanged();
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
                _floatWindowBlurEnabled = data.FloatWindowBlurEnabled;
                OnPropertyChanged(nameof(FloatWindowBlurEnabled));

                _lockFloatWindow = data.LockFloatWindow;
                OnPropertyChanged(nameof(LockFloatWindow));

                if (!double.IsNaN(data.FloatWindowLeft))
                {
                    _floatWindowLeft = data.FloatWindowLeft;
                    OnPropertyChanged(nameof(FloatWindowLeft));
                }

                if (!double.IsNaN(data.FloatWindowTop))
                {
                    _floatWindowTop = data.FloatWindowTop;
                    OnPropertyChanged(nameof(FloatWindowTop));
                }
            }
        }
        catch { /* 文件损坏，忽略，保留默认值 */ }
    }

    public void Save()
    {
        var data = new AudioflowData
        {
            FloatWindowBlurEnabled = _floatWindowBlurEnabled,
            LockFloatWindow = _lockFloatWindow,
            FloatWindowLeft = _floatWindowLeft,
            FloatWindowTop = _floatWindowTop
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed class AudioflowData
    {
        public bool FloatWindowBlurEnabled { get; set; } = true;
        public bool LockFloatWindow { get; set; }
        public double FloatWindowLeft { get; set; } = double.NaN;
        public double FloatWindowTop { get; set; } = double.NaN;
    }
}