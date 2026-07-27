using System;
using System.Diagnostics;
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
        // 按窗口所在显示器的中心判断左右侧（多显示器安全）
        var wa = MonitorHelper.GetMonitorWorkAreaDips(this);
        var isLeft = Left <= wa.Left + wa.Width / 2.0;
        MusicContent.SetAlignmentFromParent(isLeft);
    }

    // ═══════════════════════════════════════════════════════════
    // DWM Acrylic 毛玻璃背景效果
    // ═══════════════════════════════════════════════════════════

    private void InitializeBackdropBase()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();

            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is HwndTarget hwndTarget)
                hwndTarget.BackgroundColor = Colors.Transparent;

            DwmHelper.ExtendFrameIntoClientArea(this);
            DwmHelper.SetImmersiveDarkMode(this, true);
            DwmHelper.SetWindowCorners(this, CornerPreference.Round);
        }
        catch (Exception ex)
        {
            // 句柄未创建/DWM 调用失败：跳过基础背景设置，由 ApplyBackdropEffect 的降级兜底
            Debug.WriteLine($"[AcrylicMusicWindow] 基础背景初始化失败: {ex.Message}");
        }
    }

    private void ApplyBackdropEffect()
    {
        try
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
        catch (Exception ex)
        {
            // 句柄未创建/DWM 调用失败：降级为不透明遮罩兜底，避免内容悬空无背景
            Debug.WriteLine($"[AcrylicMusicWindow] 毛玻璃背景应用失败，降级为不透明遮罩: {ex.Message}");
            OpacityOverlay.Visibility = Visibility.Visible;
            AcrylicTintOverlay.Visibility = Visibility.Collapsed;
        }
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
    // 点击穿透（游戏模式）—— 共享实现见 ClickThroughHelper
    // 本窗口非 layered（DWM Acrylic 渲染路径），不能用 WS_EX_TRANSPARENT。
    // ═══════════════════════════════════════════════════════════

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // HWND 在此时创建完毕，是注册钩子和修改扩展样式的最早可靠时机
        ClickThroughHelper.OnSourceInitialized(this, layered: false);
    }

    /// <summary>
    /// 开启/关闭鼠标点击穿透（游戏模式）。开启后鼠标事件直接落到下层窗口（游戏），
    /// 悬浮窗变成纯信息展示，不可拖拽、不可交互、不可被激活。
    /// </summary>
    public void SetClickThrough(bool enabled)
        => ClickThroughHelper.SetClickThrough(this, enabled, layered: false);
}
