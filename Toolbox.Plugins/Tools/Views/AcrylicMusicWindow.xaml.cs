using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Toolbox.Controls;
using Toolbox.Tools.Helpers;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 毛玻璃悬浮窗（WindowChrome + DWM Acrylic）。
/// 毛玻璃开关打开时使用。
/// </summary>
public partial class AcrylicMusicWindow : Window
{
    private bool _isLocked;
    private EdgeDockService? _edgeDock;

    public AcrylicMusicWindow()
    {
        InitializeComponent();

        MusicContent.SizeRequired += OnSizeRequired;
        MusicContent.DragRequested += OnDragRequested;
        LocationChanged += OnWindowLocationChanged;

        Loaded += (_, _) =>
        {
            InitializeBackdropBase();
            ApplyBackdropEffect();
        };
    }

    public FloatSizeMode SizeMode
    {
        get => MusicContent.SizeMode;
        set => MusicContent.SizeMode = value;
    }

    public DockTriggerBar TriggerBar => DockTriggerBar;

    public event EventHandler? DragMoveCompleted;

    /// <summary>由 EdgeDockService 在 Attach 时设置，用于 MouseLeave 缩回检测。</summary>
    public void SetEdgeDockService(EdgeDockService service) => _edgeDock = service;

    public void SetWindowLocked(bool locked) => _isLocked = locked;

    private void OnSizeRequired(object? sender, (double Width, double Height) size)
    {
        Width = size.Width;
        Height = size.Height;

        // WPF 交换链重建和 DWM 帧更新都是异步的，发生在 Render Present 阶段，
        // 不属于 Dispatcher 队列项。ContextIdle 在 Dispatcher 完全空闲后执行，
        // 确保交换链已重建完毕、DWM 已处理完上一帧。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ReapplyHwndTransparency();
            ApplyBackdropEffect();
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void OnDragRequested(object? sender, EventArgs e)
    {
        if (!_isLocked && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AcrylicMusicWindow] DragMove 失败: {ex.Message}");
            }
            DragMoveCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var isLeft = Left <= screenWidth / 2.0;
        MusicContent.SetAlignmentFromParent(isLeft);
    }

    // ═══════════════════════════════════════════════════════════
    // DWM Acrylic 毛玻璃背景效果
    // ═══════════════════════════════════════════════════════════

    private void InitializeBackdropBase()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is HwndTarget hwndTarget)
            hwndTarget.BackgroundColor = Colors.Transparent;

        DwmHelper.ExtendFrameIntoClientArea(this);
        DwmHelper.SetImmersiveDarkMode(this, true);
        DwmHelper.SetWindowCorners(this, CornerPreference.Round);
    }

    private void ApplyBackdropEffect()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is HwndTarget hwndTarget)
            hwndTarget.BackgroundColor = Colors.Transparent;

        if (DwmHelper.IsWindows11_22H2OrLater())
        {
            DwmHelper.ExtendFrameIntoClientArea(this);
            DwmHelper.SetImmersiveDarkMode(this, true);
            DwmHelper.SetWindowCorners(this, CornerPreference.Round);
            DwmHelper.SetBackdrop(this, BackdropType.Acrylic);
        }
        else if (DwmHelper.IsWindows10OrLater())
        {
            DwmHelper.EnableAcrylicBlur(this, 0xCC1A1A1A);
            DwmHelper.ExtendFrameIntoClientArea(this);
        }

        OpacityOverlay.Visibility = Visibility.Collapsed;
        AcrylicTintOverlay.Visibility = Visibility.Visible;

        // 强制 DWM 刷新窗口帧——尺寸变化后不调用此方法，DWM 可能仍渲染旧尺寸的帧区
        DwmHelper.RefreshWindowFrame(this);
    }

    private void ReapplyHwndTransparency()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is HwndTarget target)
                target.BackgroundColor = Colors.Transparent;
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════
    // 点击穿透（游戏模式）
    // ═══════════════════════════════════════════════════════════

    private bool _isClickThrough;
    private bool _clickThroughHookRegistered;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private static readonly IntPtr HTTRANSPARENT = new(-1);
    private static readonly IntPtr MA_NOACTIVATE = new(3);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // HWND 在此时创建完毕，是注册钩子和修改扩展样式的最早可靠时机
        EnsureClickThroughHook();
        ApplyClickThroughStyles();
    }

    /// <summary>
    /// 开启/关闭鼠标点击穿透（游戏模式）。开启后鼠标事件直接落到下层窗口（游戏），
    /// 悬浮窗变成纯信息展示，不可拖拽、不可交互、不可被激活。
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        _isClickThrough = enabled;
        EnsureClickThroughHook();
        ApplyClickThroughStyles();
    }

    /// <summary>钩子只注册一次，消息处理内部用 _isClickThrough 门控。</summary>
    private void EnsureClickThroughHook()
    {
        if (_clickThroughHookRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // 窗口未 Show 前无 HWND，等 OnSourceInitialized
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProcClickThrough);
        _clickThroughHookRegistered = true;
    }

    private IntPtr WndProcClickThrough(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_isClickThrough) return IntPtr.Zero;

        // 命中测试透明：点击穿透到下层窗口
        if (msg == WM_NCHITTEST)
        {
            handled = true;
            return HTTRANSPARENT;
        }

        // 兜底：即使点击意外落在本窗口，也拒绝激活（防止抢前台导致游戏鼠标脱捕）
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return MA_NOACTIVATE;
        }

        return IntPtr.Zero;
    }

    // ── Win32 扩展样式 ──────────────────────────────────────
    // HTTRANSPARENT 只保证点击"穿过去"；WS_EX_NOACTIVATE 保证窗口任何
    // 情况下都不会被激活——后者才是防止游戏丢失前台、鼠标脱捕的根治。
    // 本窗口非 layered（DWM Acrylic 渲染路径），不能用 WS_EX_TRANSPARENT。

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>按 _isClickThrough 切换 WS_EX_NOACTIVATE 扩展样式。</summary>
    private void ApplyClickThroughStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // 等 OnSourceInitialized

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        int newStyle = _isClickThrough
            ? exStyle | WS_EX_NOACTIVATE
            : exStyle & ~WS_EX_NOACTIVATE;
        if (newStyle == exStyle) return;

        SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
