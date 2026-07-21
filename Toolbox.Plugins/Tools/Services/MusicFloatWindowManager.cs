using System;
using System.Diagnostics;
using System.Windows;
using Toolbox.Controls;
using Toolbox.Services;
using Toolbox.Tools.Helpers;
using Toolbox.Tools.Models;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 音乐悬浮窗管理器（单例）。
/// 共享 SMTC 监听器，管理透明/毛玻璃两种窗口的创建与切换。
/// </summary>
public class MusicFloatWindowManager
{
    private static readonly Lazy<MusicFloatWindowManager> _instance = new(() => new MusicFloatWindowManager());
    public static MusicFloatWindowManager Instance => _instance.Value;

    private readonly SMTCListener _listener = new();
    private readonly EdgeDockService _dockService = new();
    private NowPlayingInfo _cachedInfo = new();

    /// <summary>贴边服务，供外部（NeteaseMusicTool）访问。</summary>
    public EdgeDockService DockService => _dockService;

    private Window? _activeWindow;
    private bool _isVisible;
    private bool _blurEnabled = true;
    private FloatSizeMode _sizeMode = FloatSizeMode.Large;
    private bool _isLocked;

    /// <summary>当前活跃窗口是否可见。</summary>
    public bool IsVisible => _isVisible && _activeWindow != null;

    /// <summary>当前大小模式。</summary>
    public FloatSizeMode CurrentSizeMode => _sizeMode;

    /// <summary>可见性变化事件，供工具面板同步胶囊开关状态。</summary>
    public event EventHandler<bool>? VisibilityChanged;

    private MusicFloatWindowManager()
    {
        _listener.NowPlayingChanged += OnNowPlayingChanged;
        AudioflowSettings.Instance.PropertyChanged += OnFloatSettingChanged;
    }

    // ── 公开操作 ──────────────────────────────────────────

    /// <summary>创建并显示悬浮窗（根据当前设置选择透明/毛玻璃）。</summary>
    public void Show(FloatSizeMode sizeMode, bool blurEnabled)
    {
        _sizeMode = sizeMode;
        _blurEnabled = blurEnabled;

        if (!_listener.IsListening)
            _ = StartListenerSafeAsync();

        // 始终创建新窗口（确保正确的窗口类型）
        var newWindow = CreateWindow();
        PrePositionWindow(newWindow);
        newWindow.Show();

        // 注入缓存的歌曲信息
        if (_cachedInfo.Title != null || _cachedInfo.Artist != null)
            GetContentControl(newWindow).UpdateSongInfo(_cachedInfo);

        _activeWindow = newWindow;
        _activeWindow.LocationChanged += OnWindowMoved;
        _isVisible = true;
        VisibilityChanged?.Invoke(this, true);

        // 启动时检测是否满足贴边条件，自动收起
        newWindow.Dispatcher.BeginInvoke(
            new Action(() => _dockService.OnDragCompleted()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>隐藏当前窗口。</summary>
    public void Hide()
    {
        SaveWindowPosition();
        if (_activeWindow != null)
            _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();
        _activeWindow?.Hide();
        _isVisible = false;
        VisibilityChanged?.Invoke(this, false);
    }

    /// <summary>关闭并清理。</summary>
    public void Close()
    {
        SaveWindowPosition();
        if (_activeWindow != null)
            _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        _activeWindow?.Close();
        _activeWindow = null;
        _isVisible = false;
        VisibilityChanged?.Invoke(this, false);
    }

    /// <summary>切换毛玻璃效果（透明 ↔ 毛玻璃窗口）。</summary>
    public void ToggleBlur(bool enabled)
    {
        if (_blurEnabled == enabled) return;
        _blurEnabled = enabled;

        if (_activeWindow == null || !_isVisible) return;

        // 保存当前状态
        var savedRight = _activeWindow.Left + _activeWindow.Width;
        var savedTop = _activeWindow.Top;
        var savedLocked = _isLocked;
        var wa = MonitorHelper.GetMonitorWorkAreaDips(_activeWindow);
        var isRightSide = _activeWindow.Left > wa.Left + wa.Width / 2;

        _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();

        // 创建新窗口
        var newWindow = CreateWindow();
        newWindow.Left = _activeWindow.Left;
        newWindow.Top = savedTop;
        SetLocked(newWindow, savedLocked);

        // 先显示新窗口再关闭旧窗口，避免闪烁
        newWindow.Show();
        InjectSongInfo(newWindow);

        // 右侧锚定右边缘，宽度可能因 size mode 不同而变
        if (isRightSide)
            newWindow.Left = savedRight - newWindow.Width;

        _activeWindow.Close();
        _activeWindow = newWindow;
        _activeWindow.LocationChanged += OnWindowMoved;
    }

    /// <summary>切换大小模式（通过窗口替换，避免在同一窗口内 resize 导致 DWM 渲染问题）。</summary>
    public void SetSizeMode(FloatSizeMode mode)
    {
        if (_sizeMode == mode) return;
        _sizeMode = mode;

        if (_activeWindow == null || !_isVisible) return;

        // 保存当前状态
        var savedRight = _activeWindow.Left + _activeWindow.Width;
        var savedTop = _activeWindow.Top;
        var savedLocked = _isLocked;
        var wa = MonitorHelper.GetMonitorWorkAreaDips(_activeWindow);
        var isRightSide = _activeWindow.Left > wa.Left + wa.Width / 2;

        _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();

        // 创建同类型新窗口（保持 blur/transparent 不变，只改 SizeMode）
        var newWindow = CreateWindow();
        newWindow.Left = _activeWindow.Left;
        newWindow.Top = savedTop;
        SetLocked(newWindow, savedLocked);

        // 先显示新窗口再关闭旧窗口，避免闪烁
        newWindow.Show();
        InjectSongInfo(newWindow);

        // 右侧锚定右边缘，宽窄模式切换时宽度会变（242↔190）
        if (isRightSide)
            newWindow.Left = savedRight - newWindow.Width;

        _activeWindow.Close();
        _activeWindow = newWindow;
        _activeWindow.LocationChanged += OnWindowMoved;
    }

    /// <summary>设置窗口锁定状态。</summary>
    public void SetWindowLocked(bool locked)
    {
        _isLocked = locked;
        if (_activeWindow != null)
            SetLocked(_activeWindow, locked);
    }

    /// <summary>设置窗口位置（预留扩展方法）。</summary>
    public void SetWindowPosition(double left, double top)
    {
        if (_activeWindow != null)
        {
            _activeWindow.Left = left;
            _activeWindow.Top = top;
        }
    }

    /// <summary>设置窗口尺寸（预留扩展方法）。</summary>
    public void SetWindowSize(double width, double height)
    {
        if (_activeWindow != null)
        {
            _activeWindow.Width = width;
            _activeWindow.Height = height;
        }
    }

    // ── 内部 ──────────────────────────────────────────────

    private Window CreateWindow()
    {
        Window window = _blurEnabled
            ? new AcrylicMusicWindow()
            : new TransparentMusicWindow();

        var content = GetContentControl(window);
        content.SizeMode = _sizeMode;

        // 透明窗口：需越界 5px 才触发贴边；毛玻璃窗口：距边缘 10px 即触发
        _dockService.EdgeThreshold = _blurEnabled ? 10 : -5;

        // 挂载 EdgeDockService
        _dockService.Attach(window, content, GetTriggerBar(window), OnDragMoveCompleted);

        // 应用当前点击穿透状态（游戏模式）
        SetClickThrough(window, AudioflowSettings.Instance.ClickThroughEnabled);

        return window;
    }

    private void OnDragMoveCompleted(object? sender, EventArgs e)
    {
        _dockService.OnDragCompleted();
    }

    private static MusicContentControl GetContentControl(Window window) =>
        window switch
        {
            TransparentMusicWindow tw => tw.MusicContent,
            AcrylicMusicWindow aw => aw.MusicContent,
            _ => throw new InvalidOperationException("Unknown window type")
        };

    private static DockTriggerBar GetTriggerBar(Window window) =>
        window switch
        {
            TransparentMusicWindow tw => tw.TriggerBar,
            AcrylicMusicWindow aw => aw.TriggerBar,
            _ => throw new InvalidOperationException("Unknown window type")
        };

    private static void SetLocked(Window window, bool locked)
    {
        switch (window)
        {
            case TransparentMusicWindow tw: tw.SetWindowLocked(locked); break;
            case AcrylicMusicWindow aw: aw.SetWindowLocked(locked); break;
        }
    }

    private void InjectSongInfo(Window window)
    {
        if (_cachedInfo.Title != null || _cachedInfo.Artist != null)
            GetContentControl(window).UpdateSongInfo(_cachedInfo);
    }

    private void PrePositionWindow(Window window)
    {
        var settings = AudioflowSettings.Instance;

        if (!double.IsNaN(settings.FloatWindowLeft) && !double.IsNaN(settings.FloatWindowTop))
        {
            // 防丢窗口：持久化位置完全落在虚拟屏幕外时（拔副屏/分辨率变小），
            // 放弃恢复，改用默认位置
            double w = double.IsNaN(window.Width) ? 242 : window.Width;
            double h = double.IsNaN(window.Height) ? 252 : window.Height;
            var saved = new Rect(settings.FloatWindowLeft, settings.FloatWindowTop, w, h);
            var virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

            if (!Rect.Intersect(saved, virtualScreen).IsEmpty)
            {
                window.Left = settings.FloatWindowLeft;
                window.Top = settings.FloatWindowTop;
                return;
            }
        }

        // 使用已知最终尺寸预设位置，避免 Show() 后在 (0,0) 闪现。
        // 基于主屏工作区定位（避开任务栏），多显示器下也安全。
        double defaultH = _sizeMode == FloatSizeMode.Large ? 252 : 96;
        var workArea = SystemParameters.WorkArea;
        window.Left = workArea.Left + 20;
        window.Top = workArea.Top + (workArea.Height - defaultH) / 2;
    }

    /// <summary>安全启动 SMTC 监听，记录启动失败异常。</summary>
    private async Task StartListenerSafeAsync()
    {
        try
        {
            await _listener.StartAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] SMTC 监听启动失败: {ex.Message}");
        }
    }

    private void SaveWindowPosition()
    {
        // 贴边/动画状态下窗口坐标位于屏幕边缘外，不应作为用户位置持久化
        if (_activeWindow == null || _dockService.State != DockState.Free) return;
        var settings = AudioflowSettings.Instance;
        settings.FloatWindowLeft = _activeWindow.Left;
        settings.FloatWindowTop = _activeWindow.Top;
        settings.Save();
    }

    /// <summary>将悬浮窗复位到默认位置（所在显示器工作区垂直居中，距左 20 像素）。</summary>
    public void ResetPosition()
    {
        if (_activeWindow == null || !_isVisible) return;

        var wa = MonitorHelper.GetMonitorWorkAreaDips(_activeWindow);
        _activeWindow.Left = wa.Left + 20;
        _activeWindow.Top = wa.Top + (wa.Height - _activeWindow.Height) / 2;

        SaveWindowPosition();
    }

    /// <summary>监听窗口位置变化，实时保存位置到 audioflow.json。</summary>
    private void OnWindowMoved(object? sender, EventArgs e)
    {
        if (_activeWindow == null) return;
        // 贴边缩入/展开动画会瞬间把窗口推到屏幕外，这些坐标不是用户意图，跳过
        if (_dockService.State != DockState.Free) return;
        var settings = AudioflowSettings.Instance;
        settings.FloatWindowLeft = _activeWindow.Left;
        settings.FloatWindowTop = _activeWindow.Top;
    }

    /// <summary>悬浮窗设置项变更回调。</summary>
    private void OnFloatSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioflowSettings.ClickThroughEnabled))
        {
            var enabled = AudioflowSettings.Instance.ClickThroughEnabled;
            SetClickThrough(_activeWindow, enabled);
            if (_activeWindow == null || !_isVisible) return;

            if (enabled)
            {
                // 若正处于贴边缩入状态（内容隐藏只露触发条），先还原窗口，
                // 否则穿透下无法悬停展开，窗口会永远保持"一条缝"的外观
                _dockService.ForceRestore();
                _dockService.Detach(); // 穿透下贴边无效，主动断开
            }
            else if (AudioflowSettings.Instance.EdgeDockEnabled)
            {
                // 关闭穿透时恢复贴边挂载（Attach 内部自带 Detach，可安全重挂）
                _dockService.Attach(_activeWindow, GetContentControl(_activeWindow),
                    GetTriggerBar(_activeWindow), OnDragMoveCompleted);
            }
        }
    }

    private static void SetClickThrough(Window? window, bool enabled)
    {
        if (window == null) return;
        switch (window)
        {
            case AcrylicMusicWindow aw: aw.SetClickThrough(enabled); break;
            case TransparentMusicWindow tw: tw.SetClickThrough(enabled); break;
        }
    }

    private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
    {
        _cachedInfo = info;
        if (_activeWindow == null || !_isVisible) return;
        try
        {
            // SMTCListener 事件在后台线程触发，必须 Dispatch 到 UI 线程
            _activeWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeWindow == null) return;
                GetContentControl(_activeWindow).UpdateSongInfo(info);
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] SMTC 回调处理异常: {ex.Message}");
        }
    }
}
