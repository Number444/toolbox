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
}
