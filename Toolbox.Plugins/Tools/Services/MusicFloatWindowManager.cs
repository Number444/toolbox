using System;
using System.Windows;
using Toolbox.Controls;
using Toolbox.Services;
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
    private NowPlayingInfo _cachedInfo = new();

    private Window? _activeWindow;
    private bool _isVisible;
    private bool _blurEnabled = true;
    private FloatSizeMode _sizeMode = FloatSizeMode.Large;
    private bool _isLocked;

    /// <summary>当前活跃窗口是否可见。</summary>
    public bool IsVisible => _isVisible && _activeWindow != null;

    /// <summary>当前大小模式。</summary>
    public FloatSizeMode CurrentSizeMode => _sizeMode;

    private MusicFloatWindowManager()
    {
        _listener.NowPlayingChanged += OnNowPlayingChanged;
    }

    // ── 公开操作 ──────────────────────────────────────────

    /// <summary>创建并显示悬浮窗（根据当前设置选择透明/毛玻璃）。</summary>
    public void Show(FloatSizeMode sizeMode, bool blurEnabled)
    {
        _sizeMode = sizeMode;
        _blurEnabled = blurEnabled;

        if (!_listener.IsListening)
            _ = _listener.StartAsync();

        // 始终创建新窗口（确保正确的窗口类型）
        var newWindow = CreateWindow();
        newWindow.Show();
        PositionWindow(newWindow);

        // 注入缓存的歌曲信息
        if (_cachedInfo.Title != null || _cachedInfo.Artist != null)
            GetContentControl(newWindow).UpdateSongInfo(_cachedInfo);

        _activeWindow = newWindow;
        _isVisible = true;
    }

    /// <summary>隐藏当前窗口。</summary>
    public void Hide()
    {
        _activeWindow?.Hide();
        _isVisible = false;
    }

    /// <summary>关闭并清理。</summary>
    public void Close()
    {
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        _activeWindow?.Close();
        _activeWindow = null;
        _isVisible = false;
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
        var isRightSide = _activeWindow.Left > SystemParameters.PrimaryScreenWidth / 2;

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
        var isRightSide = _activeWindow.Left > SystemParameters.PrimaryScreenWidth / 2;

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

        GetContentControl(window).SizeMode = _sizeMode;
        return window;
    }

    private static MusicContentControl GetContentControl(Window window) =>
        window switch
        {
            TransparentMusicWindow tw => tw.MusicContent,
            AcrylicMusicWindow aw => aw.MusicContent,
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

    private static void PositionWindow(Window window)
    {
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        window.Left = 0;
        window.Top = (screenHeight - window.Height) / 2;
    }

    private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
    {
        _cachedInfo = info;
        if (_activeWindow != null)
            GetContentControl(_activeWindow).UpdateSongInfo(info);
    }
}
