using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Toolbox.Tools.Models;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 网易云音乐实时信息悬浮窗（精简版）。
/// 单例模式，通过 Instance 属性获取唯一实例。
/// 悬浮窗固定在桌面左侧，置顶显示，支持拖拽移动。
/// 支持大模式和紧凑模式两种显示形态。
/// </summary>
public partial class MusicFloatWindow : Window
{
    private static MusicFloatWindow? _instance;
    private static readonly object _lock = new();
    private bool _isDisposed;

    private readonly SMTCListener _listener = new();
    private NowPlayingInfo _previousInfo = new();
    private int _lastSongChangeVersion = -1;

    // 歌名滚动（跑马灯）
    private readonly System.Windows.Threading.DispatcherTimer _marqueeTimer;
    private double _marqueeOffset;
    private bool? _isOnLeftSide = null;

    // ── 双形态 ──
    private FloatSizeMode _sizeMode = FloatSizeMode.Large;

    public FloatSizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            if (_sizeMode == value) return;
            _sizeMode = value;
            if (IsLoaded) ApplySizeMode();
        }
    }

    // ── 透明度遮罩 ──
    private bool _overlayEnabled;

    // ── 锁定 ──
    private bool _isLocked;

    /// <summary>
    /// 切换悬浮窗背景遮罩（45% 透明灰色层）
    /// </summary>
    public void SetWindowOpacity(bool enabled)
    {
        _overlayEnabled = enabled;
        if (IsLoaded)
        {
            Dispatcher.Invoke(() =>
                OpacityOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed);
        }
    }

    /// <summary>
    /// 设置悬浮窗锁定状态
    /// </summary>
    public void SetWindowLocked(bool locked)
    {
        _isLocked = locked;
    }

    private MusicFloatWindow()
    {
        InitializeComponent();

        // 歌名滚动定时器：每 40ms 移动一次（约 25fps）
        _marqueeTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(40),
            System.Windows.Threading.DispatcherPriority.Normal,
            OnMarqueeTick,
            Dispatcher);
        _marqueeTimer.Stop();

        // 监听 SMTC 事件
        _listener.NowPlayingChanged += OnNowPlayingChanged;

        // 窗口位置：屏幕左侧居中
        Loaded += (s, e) =>
        {
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = 0;
            Top = (screenHeight - Height) / 2;
            // Large 模式下强制确保 ContentPanel 顶部偏移和遮罩尺寸正确
            //（双保险：Loaded 可能在 ApplySizeMode 之后才触发，此处确保值不丢失）
            if (_sizeMode == FloatSizeMode.Large)
            {
                ApplyLargeMargins();
            }
            OnWindowLocationChanged(null, EventArgs.Empty);
        };

        // 联动关闭：主窗口关闭时也关闭悬浮窗
        try
        {
            if (Application.Current?.MainWindow != null)
                Application.Current.MainWindow.Closed += (s, e) => Close();
        }
        catch { }
    }

    /// <summary>
    /// 获取悬浮窗的唯一实例。首次调用时自动创建窗口。
    /// 线程安全（双重检查锁定）。
    /// </summary>
    public static MusicFloatWindow Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new MusicFloatWindow();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 强制重置单例。仅用于测试。
    /// 关闭旧实例（通过其 Dispatcher 编组）并将 _instance 置空。
    /// 调用方（测试）应在 STA 线程中调用此方法。
    /// </summary>
    internal static void ForceResetInstance()
    {
        MusicFloatWindow? oldInstance = null;
        lock (_lock)
        {
            oldInstance = _instance;
            _instance = null;
        }
        // 在锁外通过 Dispatcher 关闭旧实例，避免死锁
        if (oldInstance != null)
        {
            try
            {
                if (oldInstance.Dispatcher.CheckAccess())
                {
                    oldInstance.Close();
                }
                else
                {
                    oldInstance.Dispatcher.Invoke(() => oldInstance.Close());
                }
            }
            catch
            {
                // 忽略跨线程关闭异常，旧实例会被 GC 回收
            }
        }
    }

    /// <summary>
    /// 打开悬浮窗并开始监听 SMTC 会话。
    /// </summary>
    public new void Show()
    {
        if (!_listener.IsListening)
        {
            _ = _listener.StartAsync();
        }
        base.Show();
        // 始终调用 ApplySizeMode() 确保遮罩层尺寸被正确初始化
        //（即使默认 Large 模式也需要设置 OpacityOverlay 的 Width/Height）
        ApplySizeMode();
    }

    /// <summary>
    /// 隐藏悬浮窗。
    /// </summary>
    public new void Hide()
    {
        base.Hide();
    }

    /// <summary>
    /// 窗口关闭时释放资源。
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _marqueeTimer.Stop();
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        _isDisposed = true;
        lock (_lock) { _instance = null; }
        base.OnClosed(e);
    }

    // ── 事件处理 ──────────────────────────────────────────

    private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            var isNewSong = NowPlayingInfo.IsSongChanged(_previousInfo, info);
            var isCoverUpdate = !isNewSong
                && NowPlayingInfo.IsThumbnailChanged(_previousInfo, info);

            if (isNewSong)
            {
                if (info.RefreshVersion < _lastSongChangeVersion)
                {
                    _previousInfo = info;
                    return;
                }

                _lastSongChangeVersion = info.RefreshVersion;
                _previousInfo = info;

                PlaySongSwitchAnimation(
                    onMidpoint: () =>
                    {
                        ApplySongInfo(info);
                    },
                    onPhase2Complete: () =>
                    {
                        StartOrStopTitleMarquee();
                    });
            }
            else if (isCoverUpdate)
            {
                _previousInfo = info;

                if (info.RefreshVersion >= _lastSongChangeVersion)
                {
                    LoadCoverFromData(info.ThumbnailData);
                }
            }
            else
            {
                _previousInfo = info;
            }
        });
    }

    /// <summary>
    /// 用 NowPlayingInfo 统一更新歌曲标题、歌手和封面。
    /// </summary>
    private void ApplySongInfo(NowPlayingInfo info)
    {
        SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
        SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
        LoadCoverFromData(info.ThumbnailData);
    }

    /// <summary>
    /// 从字节数组加载封面到 UI，使用双层交叉淡入实现无闪烁切换。
    /// </summary>
    private void LoadCoverFromData(byte[]? thumbnailData)
    {
        if (thumbnailData == null || thumbnailData.Length == 0)
        {
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

            CoverImageBack.Source = CoverImage.Source;
            CoverImage.Source = bitmap;
            CoverImage.Opacity = 0;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            fadeIn.Completed += (_, _) =>
            {
                CoverImageBack.Source = null;
                CoverImage.BeginAnimation(UIElement.OpacityProperty, null);
                CoverImage.Opacity = 1;
            };
            CoverImage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        catch
        {
        }
    }

    // ── 双形态核心 ────────────────────────────────────────

    /// <summary>
    /// 应用当前 SizeMode：迁移子元素、更新封面尺寸、调整窗口大小。
    /// </summary>
    private void ApplySizeMode()
    {
        if (_sizeMode == FloatSizeMode.Large)
        {
            EnsureChildInPanel(LayoutLarge, CoverGrid);
            EnsureChildInPanel(LayoutLarge, TitleCanvas);
            EnsureChildInPanel(LayoutLarge, SongArtist);
            ClearGridAttachedProperties(CoverGrid, TitleCanvas, SongArtist);
            LayoutLarge.Visibility = Visibility.Visible;
            LayoutCompact.Visibility = Visibility.Collapsed;
            ApplyCoverMetrics(180, 10, 15, 4);
            UpdateWindowSize(252, 292);
            ApplyLargeMargins();
            OpacityOverlay.CornerRadius = new CornerRadius(10);
        }
        else
        {
            MoveChildToSlot(CoverGrid, CompactCoverSlot);
            MoveChildToSlot(TitleCanvas, CompactTextSlot);
            MoveChildToSlot(SongArtist, CompactTextSlot);
            LayoutCompact.Visibility = Visibility.Visible;
            LayoutLarge.Visibility = Visibility.Collapsed;
            ApplyCoverMetrics(60, 3, 5, 1.3);
            UpdateWindowSize(190, 96);
            SetCompactMargins();
        }
        StartOrStopTitleMarquee();
        ApplyAlignment(_isOnLeftSide ?? true);
    }

    private void ApplyCoverMetrics(double size, double radius, double blur, double depth)
    {
        CoverGrid.Width = CoverGrid.Height = size;
        CoverContainer.CornerRadius = new CornerRadius(radius);
        CoverClipContainer.Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
        var shadow = CoverContainer.Effect as DropShadowEffect;
        if (shadow != null)
        {
            shadow.BlurRadius = blur;
            shadow.ShadowDepth = depth;
        }
    }

    private void UpdateWindowSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    // ── 元素迁移小工具 ──

    private static void EnsureChildInPanel(Panel panel, UIElement child)
    {
        var currentParent = VisualTreeHelper.GetParent(child);
        if (currentParent == panel) return;

        // 处理 Decorator 父容器（如 Border），其通过 Child 属性而非 Children 集合持有元素
        if (currentParent is Decorator decorator)
        {
            decorator.Child = null;
        }
        else if (currentParent is Panel parentPanel)
        {
            parentPanel.Children.Remove(child);
        }

        panel.Children.Add(child);
    }

    private static void MoveChildToSlot(UIElement child, Border slot)
    {
        var currentParent = VisualTreeHelper.GetParent(child) as Panel;
        currentParent?.Children.Remove(child);
        slot.Child = child;
    }

    private static void MoveChildToSlot(UIElement child, Panel panel)
    {
        var currentParent = VisualTreeHelper.GetParent(child) as Panel;
        currentParent?.Children.Remove(child);
        panel.Children.Add(child);
    }

    private static void ClearGridAttachedProperties(params UIElement[] elements)
    {
        foreach (var e in elements)
        {
            Grid.SetColumn(e, 0);
            Grid.SetRow(e, 0);
        }
    }

    private void ApplyLargeMargins()
    {
        // 大模式：内容下移 10px 为遮罩顶部圆角留出缓冲区（避免 DWM 裁剪）
        ContentPanel.Margin = new Thickness(4, 10, 0, 0);
        // 紧凑间距 — 8px padding, 10px gap
        CoverGrid.Margin = new Thickness(12, 0, 12, 0);
        TitleCanvas.Margin = new Thickness(12, 5, 12, 0);
        SongArtist.Margin = new Thickness(12, 5, 12, 0);

        // 根据实际内容高度计算遮罩层尺寸，确保遮罩底部紧贴内容底部
        // LayoutLarge = CoverGrid(h:180) + TitleCanvas(gap:5, h:24) + SongArtist(gap:5, text:h)
        // SongArtist 行高 ≈ FontSize(12) × 行间距系数(≈1.17) ≈ 14
        const double songArtistEstimatedHeight = 14;
        double layoutLargeHeight = 0 /*CoverGrid margin top*/
            + 180 /*CoverGrid*/
            + 0 /*CoverGrid margin bottom*/
            + 5 /*TitleCanvas margin top*/
            + 24 /*TitleCanvas*/
            + 0 /*TitleCanvas margin bottom*/
            + 5 /*SongArtist margin top*/
            + songArtistEstimatedHeight
            + 10 /*SongArtist margin bottom (下方间距 10px)*/;

        // 遮罩从窗口顶部(y=0)包裹到 ContentPanel 底部
        // ContentPanel.Margin.Top(10) + layoutLargeHeight(含底部10px间距)
        double contentBottom = ContentPanel.Margin.Top + layoutLargeHeight;
        OpacityOverlay.Width = 236;
        OpacityOverlay.Height = contentBottom;
        OpacityOverlay.Margin = new Thickness(6, 0, 8, 0);
    }

    private void SetCompactMargins()
    {
        // 紧凑模式：ContentPanel 偏移 (左10, 右10)，不修改任何工具组件 margin
        ContentPanel.Margin = new Thickness(10, 0, 10, 0);
        CoverGrid.Margin = new Thickness(4, 0, 4, 0);   // 原值
        TitleCanvas.Margin = new Thickness(0);            // 原值
        SongArtist.Margin = new Thickness(0);             // 原值

        // 紧凑模式遮罩基本属性（左右 Margin 由 ApplyAlignment 根据取向设置）
        OpacityOverlay.CornerRadius = new CornerRadius(10);
        OpacityOverlay.Width = 186;
        OpacityOverlay.Height = 80;
        LayoutCompact.Margin = new Thickness(10, 18, 0, 0);
    }

    // ── 拖拽 ──────────────────────────────────────────────

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked) return;
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var isLeft = AlignmentHelper.IsLeftSide(Left, screenWidth);

        if (isLeft == _isOnLeftSide) return;
        _isOnLeftSide = isLeft;

        ApplyAlignment(isLeft);
    }

    private void ApplyAlignment(bool isLeft)
    {
        var halign = AlignmentHelper.GetHorizontalAlignment(isLeft);
        var talign = AlignmentHelper.GetTextAlignment(isLeft);

        if (_sizeMode == FloatSizeMode.Large)
        {
            // 大模式保留原始逻辑不变
            CoverGrid.HorizontalAlignment = halign;
            TitleCanvas.HorizontalAlignment = halign;
            SongArtist.TextAlignment = talign;
            if (!_marqueeTimer.IsEnabled && SongTitle.Width == 220)
            {
                SongTitle.TextAlignment = talign;
            }
        }
        else
        {
            // 紧凑模式镜像布局
            Grid.SetColumn(CompactCoverSlot, isLeft ? 0 : 1);
            Grid.SetColumn(CompactTextSlot, isLeft ? 1 : 0);
            // 同步交换 ColumnDefinitions 的 Width
            // 默认：列0=Auto（封面），列1=*（文本）
            // 右侧镜像：列0=*（封面在右侧占据剩余空间），列1=Auto（文本在左侧自适应）
            LayoutCompact.ColumnDefinitions[0].Width =
                isLeft ? new GridLength(0, GridUnitType.Auto) : new GridLength(1, GridUnitType.Star);
            LayoutCompact.ColumnDefinitions[1].Width =
                isLeft ? new GridLength(1, GridUnitType.Star) : new GridLength(0, GridUnitType.Auto);
            CoverGrid.HorizontalAlignment = halign;
            CompactTextSlot.HorizontalAlignment = halign;
            SongArtist.TextAlignment = talign;
            if (!_marqueeTimer.IsEnabled)
                SongTitle.TextAlignment = talign;
        }

        // 紧凑模式遮罩左右边距按取向设置，确保 10px 间距
        if (_sizeMode == FloatSizeMode.Compact)
        {
            if (isLeft)
            {
                // OverlayLeft(4) + 10 = ContentLeft(14) → 间距 10px
                // OverlayRight(190) - 10 = ContentRight(180) → 间距 10px
                OpacityOverlay.Margin = new Thickness(4, 8, 0, 0);
            }
            else
            {
                // 右侧取向：OverlayLeft(0) + 10 = ContentLeft(10) → 间距 10px
                // OverlayRight(186) - 10 = ContentRight(176) → 间距 10px
                OpacityOverlay.Margin = new Thickness(0, 8, 4, 0);
            }
        }
    }

    // ── 歌名滚动 ──────────────────────────────────────────

    private void StartOrStopTitleMarquee()
    {
        _marqueeTimer.Stop();
        _marqueeOffset = 0;
        TitleTranslate.X = 0;

        var title = SongTitle.Text ?? "";
        double availableWidth;

        if (_sizeMode == FloatSizeMode.Compact)
        {
            // 文本列宽度 = 窗口(190) - ContentPanel margin(20) - cover slot(68)
            availableWidth = 190 - 20 - 68;  // = 102
            TitleCanvas.Width = availableWidth;
        }
        else
        {
            availableWidth = TitleCanvas.Width;
        }

        var fontSize = SongTitle.FontSize;

        if (TitleMarquee.NeedsScroll(title, availableWidth, fontSize))
        {
            SongTitle.TextAlignment = AlignmentHelper.GetTextAlignment(_isOnLeftSide);
            SongTitle.Width = double.NaN;

            Dispatcher.BeginInvoke(
                new Action(() => _marqueeTimer.Start()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            SongTitle.TextAlignment = AlignmentHelper.GetTextAlignment(_isOnLeftSide);
            SongTitle.Width = availableWidth;
        }
    }

    private void OnMarqueeTick(object? sender, EventArgs e)
    {
        _marqueeOffset -= 0.3;

        var textWidth = SongTitle.ActualWidth;
        var visibleWidth = TitleCanvas.Width;

        if (_marqueeOffset < -(textWidth + 30))
        {
            _marqueeOffset = visibleWidth;
        }

        TitleTranslate.X = _marqueeOffset;
    }

    // ── 动画 ──────────────────────────────────────────────

    internal static void ResetPanelToVisibleState(StackPanel panel, Thickness? restoreMargin = null)
    {
        if (panel == null) return;
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        panel.BeginAnimation(FrameworkElement.MarginProperty, null);
        var transform = panel.RenderTransform as TranslateTransform;
        transform?.BeginAnimation(TranslateTransform.XProperty, null);
        panel.RenderTransform = new TranslateTransform(0, 0);
        panel.Opacity = 1;
        panel.Margin = restoreMargin ?? new Thickness(0);
    }

    private void PlaySongSwitchAnimation(Action onMidpoint, Action? onPhase2Complete = null)
    {
        var panel = ContentPanel;

        // 切歌动画重置面板时，需恢复 ContentPanel 的正确 margin（Large 模式偏移：left=4, top=10）
        var restoreMargin = _sizeMode == FloatSizeMode.Large
            ? new Thickness(4, 10, 0, 0)
            : new Thickness(0);
        ResetPanelToVisibleState(panel, restoreMargin);

        bool isLeft = _isOnLeftSide ?? true;
        double phase1Slide = isLeft ? -35 : 35;
        double phase2From = isLeft ? 35 : -35;

        // Phase 1: 淡出 + 滑动
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        var slideOut = new DoubleAnimation(0, phase1Slide, TimeSpan.FromMilliseconds(200));

        var sbOut = new Storyboard();
        Storyboard.SetTarget(fadeOut, panel);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
        Storyboard.SetTarget(slideOut, panel);
        Storyboard.SetTargetProperty(slideOut, new PropertyPath("RenderTransform.(TranslateTransform.X)"));
        sbOut.Children.Add(fadeOut);
        sbOut.Children.Add(slideOut);

        sbOut.Completed += (_, _) =>
        {
            panel.BeginAnimation(UIElement.OpacityProperty, null);
            var transform = panel.RenderTransform as TranslateTransform;
            transform?.BeginAnimation(TranslateTransform.XProperty, null);

            onMidpoint();

            panel.Opacity = 0;
            panel.RenderTransform = new TranslateTransform(phase2From, 0);

            // Phase 2: 从另一侧淡入
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideIn = new DoubleAnimation(phase2From, 0, TimeSpan.FromMilliseconds(200));

            var sbIn = new Storyboard();
            Storyboard.SetTarget(fadeIn, panel);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
            Storyboard.SetTarget(slideIn, panel);
            Storyboard.SetTargetProperty(slideIn, new PropertyPath("RenderTransform.(TranslateTransform.X)"));
            sbIn.Children.Add(fadeIn);
            sbIn.Children.Add(slideIn);

            sbIn.Completed += (_, _) =>
            {
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                var t = panel.RenderTransform as TranslateTransform;
                t?.BeginAnimation(TranslateTransform.XProperty, null);
                panel.Opacity = 1;
                panel.RenderTransform = new TranslateTransform(0, 0);

                onPhase2Complete?.Invoke();
            };

            sbIn.Begin();
        };

        sbOut.Begin();
    }

    // ── 工具类 ──────────────────────────────────────────────

    public static class TitleMarquee
    {
        private const double CnCharWidthFactor = 1.05;
        private const double EnCharWidthFactor = 0.55;

        public static bool NeedsScroll(string text, double availableWidth, double fontSize)
        {
            if (string.IsNullOrEmpty(text)) return false;

            double estimatedWidth = 0;
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF
                    || c >= 0x3400 && c <= 0x4DBF
                    || c >= 0xF900 && c <= 0xFAFF
                    || c >= 0x3000 && c <= 0x303F
                    || c >= 0xFF00 && c <= 0xFFEF)
                {
                    estimatedWidth += fontSize * CnCharWidthFactor;
                }
                else
                {
                    estimatedWidth += fontSize * EnCharWidthFactor;
                }
            }

            return estimatedWidth > availableWidth;
        }
    }

    public static class AlignmentHelper
    {
        public static bool IsLeftSide(double windowLeft, double screenWidth)
        {
            return windowLeft <= screenWidth / 2.0;
        }

        public static HorizontalAlignment GetHorizontalAlignment(bool? isLeft)
        {
            return isLeft switch
            {
                true => HorizontalAlignment.Left,
                false => HorizontalAlignment.Right,
                null => HorizontalAlignment.Center
            };
        }

        public static System.Windows.TextAlignment GetTextAlignment(bool? isLeft)
        {
            return isLeft switch
            {
                true => System.Windows.TextAlignment.Left,
                false => System.Windows.TextAlignment.Right,
                null => System.Windows.TextAlignment.Center
            };
        }
    }
}

public enum FloatSizeMode { Large, Compact }