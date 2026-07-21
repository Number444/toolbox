using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Toolbox.Controls;
using Toolbox.Tools.Helpers;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 纯透明悬浮窗（AllowsTransparency=True，无 DWM 背景效果）。
/// 毛玻璃开关关闭时使用。
/// </summary>
public partial class TransparentMusicWindow : Window
{
    private bool _isLocked;
    private EdgeDockService? _edgeDock;

    public TransparentMusicWindow()
    {
        InitializeComponent();

        MusicContent.SizeRequired += OnSizeRequired;
        MusicContent.DragRequested += OnDragRequested;
        LocationChanged += OnWindowLocationChanged;
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
                Debug.WriteLine($"[TransparentMusicWindow] DragMove 失败: {ex.Message}");
            }
            DragMoveCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        // 按窗口所在显示器的中心判断左右侧（多显示器安全）
        var wa = MonitorHelper.GetMonitorWorkAreaDips(this);
        var isLeft = Left <= wa.Left + wa.Width / 2.0;
        MusicContent.SetAlignmentFromParent(isLeft);
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

        // 命中测试透明：点击穿透到下层窗口（对 WS_EX_TRANSPARENT 之外的残余路径兜底）
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
    // 本窗口是 layered（AllowsTransparency），WS_EX_TRANSPARENT 直接生效：
    // 系统在命中测试阶段就整块跳过本窗口，不派发任何消息，不受钩子顺序影响，
    // 是最硬的穿透手段。WS_EX_NOACTIVATE 保证窗口任何情况下都不会被激活——
    // 后者才是防止游戏丢失前台、鼠标脱捕的根治。

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
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

    /// <summary>按 _isClickThrough 切换 WS_EX_NOACTIVATE | WS_EX_TRANSPARENT 扩展样式。</summary>
    private void ApplyClickThroughStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // 等 OnSourceInitialized

        const int mask = WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        int newStyle = _isClickThrough
            ? exStyle | mask
            : exStyle & ~mask;
        if (newStyle == exStyle) return;

        SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
