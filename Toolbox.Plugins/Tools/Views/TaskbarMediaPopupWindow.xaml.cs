using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Toolbox.Core.Helpers;
using Toolbox.Tools.Models;
using Windows.Media.Control;
using static Toolbox.Core.Helpers.Win32Native;

namespace Toolbox.Tools.Views;

/// <summary>
/// 弹出媒体卡片（Mica 毛玻璃）：从任务栏迷你控件上方展开，布局对齐 Win11 原生媒体控件。
/// - 材质：DWM Mica（采样壁纸的静态模糊，比 Acrylic 稳定）；边框由 DWM 原生绘制（不加自定义描边防双层边缘）
/// - 动画（FluentFlyout 同款路径，参照 github.com/unchihugo/FluentFlyout 验证）：
///   Window.Top/Opacity 是依赖属性，可直接开动画——Show 前窗口预置于最终位置下方 20px（首帧即起点，无闪现），
///   Top 上移 20px + Opacity 0→1（300ms CubicEase EaseOut），内容层模糊 8→0 以 450ms 更缓收尾；
///   收拢 180ms EaseIn 镜像后 Hide。位移仅 20px 且窗口底部始终在任务栏上沿之上，层序免疫。
///   （FluentFlyout 实证：Mica 背板与窗口级 Opacity 动画可共存——Opacity 回到 1 后 WPF 摘掉
///   layered 属性，DWM 背板恢复。若实测背板被杀死，回退假毛玻璃视觉树底色。）
/// - 按钮：公共样式 MediaTransportButtonStyle（App.xaml，叠层法 hover/press + 缩放 0.97）
/// - 打开后 Activate() 一次（点击来自任务栏，焦点不在输入框，无感），从而获得 Deactivated → 点外部关闭能力
/// </summary>
public partial class TaskbarMediaPopupWindow : Window
{
    // ── 播放控制事件（由 Manager 转发到 SMTC 会话）──
    public event Action? OnSkipPrevious;
    public event Action? OnTogglePlayPause;
    public event Action? OnSkipNext;

    /// <summary>卡片关闭动画播放完毕（真正隐藏）后触发，供 Manager 抑制"点控件关卡片又立刻重开"的竞态。</summary>
    public event Action? PopupClosed;

    // ── 当前歌曲信息 ──
    private NowPlayingInfo? _currentInfo;
    private bool _isClosing;

    // ── 动画参数（FluentFlyout 同款：20px 轻推 + 淡入，CubicEase）──
    private const double OpenRiseDistance = 20;      // 起点在最终位置下方 20px（FluentFlyout 实测量级）
    private const double BlurStartRadius = 8;        // 内容模糊渐显起点（小半径防重绘开销）
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(300);      // 1x（FluentFlyout 默认）
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(180);     // 离场更快
    private static readonly TimeSpan BlurOpenDuration = TimeSpan.FromMilliseconds(450);  // 模糊比整卡渐显慢半拍

    public TaskbarMediaPopupWindow()
    {
        InitializeComponent();

        // 打开时应用毛玻璃背景
        Loaded += (_, _) =>
        {
            InitializeBackdropBase();
            ApplyBackdropEffect();
        };

        // 失焦自动关闭（Open 时会 Activate() 一次，因此点卡片外任意位置都会触发）
        Deactivated += (_, _) => AnimatedClose();

        // Esc 关闭
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                AnimatedClose();
            }
        };

        // 播放按钮（Button + 公共样式，走 Click）
        PopupBtnPrev.Click += (_, _) => OnSkipPrevious?.Invoke();
        PopupBtnPlayPause.Click += (_, _) => OnTogglePlayPause?.Invoke();
        PopupBtnNext.Click += (_, _) => OnSkipNext?.Invoke();
    }

    // ── 打开 / 关闭 / 锚定 ──

    /// <summary>
    /// 打开并锚定到迷你控件上方（widgetScreenBounds 为迷你控件的屏幕物理坐标，dpiScale 取任务栏控件窗口的 DPI）。
    /// 水平与控件中心对齐，垂直位于任务栏上方 8px。
    /// 动画（FluentFlyout 同款）：Show 前窗口预置于最终位置下方 20px → Top 上移 + Opacity 淡入 + 内容模糊渐显。
    /// </summary>
    public void Open(Rect widgetScreenBounds, double dpiScale)
    {
        // 起始帧（先清旧动画再设本地值，防残留动画值覆盖起点）
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        ContentBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
        ContentBlur.Radius = BlurStartRadius;

        if (IsVisible)
        {
            // 重开兜底：已可见则直接落定到完成态
            AnchorTo(widgetScreenBounds, dpiScale);
            Opacity = 1;
            ContentBlur.Radius = 0;
            return;
        }

        // Show 前预锚定（DPI 来自任务栏控件窗口）：窗口直接出现在动画起点（最终位置下方 20px），无首帧闪现
        AnchorTo(widgetScreenBounds, dpiScale);
        double finalTop = Top;
        Top = finalTop + OpenRiseDistance;
        Opacity = 0;

        Show();

        // 主动激活一次：点击来源是任务栏（焦点本就不在输入框），无感；
        // 激活后 Deactivated 才能可靠触发 → 实现"点击外部自动关闭"
        Activate();

        // 整卡：上移 20px 到位 + 淡入（300ms CubicEase EaseOut）
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(TopProperty, new DoubleAnimation(finalTop, OpenDuration) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, OpenDuration) { EasingFunction = ease });
        // 内容：模糊渐显，比整卡慢半拍收尾（450ms）
        ContentBlur.BeginAnimation(BlurEffect.RadiusProperty,
            new DoubleAnimation(BlurStartRadius, 0, BlurOpenDuration) { EasingFunction = ease });
    }

    /// <summary>关闭（整卡下沉 20px + 淡出 + 内容变模糊，180ms EaseIn 后 Hide）。幂等。</summary>
    public void AnimatedClose()
    {
        if (_isClosing || !IsVisible) return;
        _isClosing = true;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        BeginAnimation(TopProperty, new DoubleAnimation(Top + OpenRiseDistance, CloseDuration) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, CloseDuration) { EasingFunction = ease });
        ContentBlur.BeginAnimation(BlurEffect.RadiusProperty,
            new DoubleAnimation(BlurStartRadius, CloseDuration) { EasingFunction = ease });

        DispatcherTimer? closeTimer = null;
        closeTimer = new DispatcherTimer(
            CloseDuration + TimeSpan.FromMilliseconds(30),
            DispatcherPriority.Background,
            (_, _) =>
            {
                Hide();
                _isClosing = false;
                closeTimer?.Stop();
                PopupClosed?.Invoke();
            },
            Dispatcher);
        closeTimer.Start();
    }

    /// <summary>重新锚定到迷你控件当前屏幕位置（拖拽换位后调用）。dpiScale 可由调用方（任务栏控件窗口）提供，用于 Show 前预锚定。</summary>
    public void AnchorTo(Rect widgetScreenBounds, double? dpiScale = null)
    {
        double scale = dpiScale ?? 1.0;
        if (!dpiScale.HasValue && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            scale = target.TransformToDevice.M11;
        }

        // 统一在 DIP 空间计算
        double widgetLeftDips = widgetScreenBounds.X / scale;
        double widgetWidthDips = widgetScreenBounds.Width / scale;
        double widgetTopDips = widgetScreenBounds.Y / scale;
        double gapDips = 8 / scale;

        double x = widgetLeftDips + widgetWidthDips / 2 - Width / 2;
        double y = widgetTopDips - Height - gapDips;

        // 防越界（虚拟屏幕内留 8px 边距）
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        if (x < vLeft + 8) x = vLeft + 8;
        if (x + Width > vRight - 8) x = vRight - 8 - Width;

        Left = x;
        Top = y;
    }

    // ── 歌曲信息 ──

    public void UpdateSongInfo(NowPlayingInfo info)
    {
        if (info == null) return;
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded) return;

            _currentInfo = info;

            PopupTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
            PopupArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;

            bool isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            PopupPlayIcon.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
            PopupPauseIcon.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;

            LoadCover(info.ThumbnailData);
        });
    }

    private void LoadCover(byte[]? thumbnailData)
    {
        if (thumbnailData == null || thumbnailData.Length == 0)
        {
            PopupImageBrush.ImageSource = null;
            PopupImagePlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            using var memStream = new MemoryStream(thumbnailData);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memStream;
            bitmap.EndInit();
            bitmap.Freeze();

            PopupImageBrush.ImageSource = bitmap;
            PopupImagePlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarMediaPopupWindow] 封面加载失败: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════
    // DWM Mica 背景（与 AcrylicMusicWindow 同路径，仅背板类型不同）
    // ═══════════════════════════════════════════════════════════

    private void InitializeBackdropBase()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is HwndTarget hwndTarget)
                hwndTarget.BackgroundColor = Colors.Transparent;

            DwmHelper.ExtendFrameIntoClientArea(this);
            DwmHelper.SetImmersiveDarkMode(this, true);
            DwmHelper.SetWindowCorners(this, CornerPreference.Round);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarMediaPopupWindow] 基础背景初始化失败: {ex.Message}");
        }
    }

    private void ApplyBackdropEffect()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is HwndTarget hwndTarget)
                hwndTarget.BackgroundColor = Colors.Transparent;

            if (DwmHelper.IsWindows11_22H2OrLater())
            {
                DwmHelper.ExtendFrameIntoClientArea(this);
                DwmHelper.SetImmersiveDarkMode(this, true);
                DwmHelper.SetWindowCorners(this, CornerPreference.Round);
                DwmHelper.SetBackdrop(this, BackdropType.Mica); // Mica：采样壁纸的静态模糊，稳定性远好于 Acrylic
            }
            else if (DwmHelper.IsWindows10OrLater())
            {
                DwmHelper.EnableAcrylicBlur(this, 0xCC1A1A1A);
                DwmHelper.ExtendFrameIntoClientArea(this);
            }

            OpacityOverlay.Visibility = Visibility.Collapsed;
            AcrylicTintOverlay.Visibility = Visibility.Visible;
            DwmHelper.RefreshWindowFrame(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarMediaPopupWindow] 毛玻璃背景应用失败，降级为不透明遮罩: {ex.Message}");
            OpacityOverlay.Visibility = Visibility.Visible;
            AcrylicTintOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
