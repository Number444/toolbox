using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Toolbox.Tools.Models;

namespace Toolbox.Controls;

/// <summary>
/// 网易云音乐悬浮窗的内容控件。
/// 封装封面渲染、歌名跑马灯、大小模式切换、切歌动画等逻辑，
/// 供 TransparentMusicWindow 和 AcrylicMusicWindow 复用。
/// </summary>
public partial class MusicContentControl : UserControl
{
    // ── 歌名滚动（跑马灯）──
    private readonly System.Windows.Threading.DispatcherTimer _marqueeTimer;
    private double _marqueeOffset;
    private bool? _isOnLeftSide;

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

    /// <summary>窗口尺寸需要变更时触发（width, height）。</summary>
    public event EventHandler<(double Width, double Height)>? SizeRequired;

    /// <summary>用户拖拽请求时触发。</summary>
    public event EventHandler? DragRequested;

    public MusicContentControl()
    {
        InitializeComponent();

        // 歌名滚动定时器：每 40ms 移动一次（约 25fps）
        _marqueeTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(40),
            System.Windows.Threading.DispatcherPriority.Normal,
            OnMarqueeTick,
            Dispatcher);
        _marqueeTimer.Stop();

        // 将整个内容区域的鼠标按下事件作为拖拽请求
        ContentPanel.MouseLeftButtonDown += (_, _) => DragRequested?.Invoke(this, EventArgs.Empty);

        // 控件加载后必须应用当前 SizeMode 的布局。
        // 因为 Manager.CreateWindow 在窗口 Show 之前就设置了 SizeMode，
        // 此时 IsLoaded=false，ApplySizeMode 被跳过。
        Loaded += (_, _) => ApplySizeMode();
    }

    // ── 歌曲信息注入 ───────────────────────────────────────

    private NowPlayingInfo _previousInfo = new();
    private int _lastSongChangeVersion = -1;

    /// <summary>
    /// 外部（Manager）注入新的歌曲信息。
    /// 自动处理切歌动画、封面更新。
    /// </summary>
    public void UpdateSongInfo(NowPlayingInfo info)
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
                    onMidpoint: () => ApplySongInfo(info),
                    onPhase2Complete: StartOrStopTitleMarquee);
            }
            else if (isCoverUpdate)
            {
                _previousInfo = info;
                if (info.RefreshVersion >= _lastSongChangeVersion)
                    LoadCoverFromData(info.ThumbnailData);
            }
            else
            {
                _previousInfo = info;
            }
        });
    }

    // ── 内容渲染 ──────────────────────────────────────────

    private void ApplySongInfo(NowPlayingInfo info)
    {
        SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
        SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
        LoadCoverFromData(info.ThumbnailData);
    }

    private void LoadCoverFromData(byte[]? thumbnailData)
    {
        if (thumbnailData == null || thumbnailData.Length == 0) return;

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
                CoverImage.BeginAnimation(OpacityProperty, null);
                CoverImage.Opacity = 1;
            };
            CoverImage.BeginAnimation(OpacityProperty, fadeIn);
        }
        catch { }
    }

    // ── 双形态核心 ────────────────────────────────────────

    public void ApplySizeMode()
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
            FireSizeRequired(242, 252);
            ApplyLargeMargins();
        }
        else
        {
            MoveChildToSlot(CoverGrid, CompactCoverSlot);
            MoveChildToSlot(TitleCanvas, CompactTextSlot);
            MoveChildToSlot(SongArtist, CompactTextSlot);
            LayoutCompact.Visibility = Visibility.Visible;
            LayoutLarge.Visibility = Visibility.Collapsed;
            ApplyCoverMetrics(60, 3, 5, 1.3);
            FireSizeRequired(190, 96);
            SetCompactMargins();
        }
        StartOrStopTitleMarquee();
        ApplyAlignment(_isOnLeftSide ?? true);
    }

    private void FireSizeRequired(double width, double height)
    {
        SizeRequired?.Invoke(this, (width, height));
    }

    private void ApplyCoverMetrics(double size, double radius, double blur, double depth)
    {
        CoverGrid.Width = CoverGrid.Height = size;
        CoverContainer.CornerRadius = new CornerRadius(radius);
        CoverClipContainer.Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
        if (CoverContainer.Effect is DropShadowEffect shadow)
        {
            shadow.BlurRadius = blur;
            shadow.ShadowDepth = depth;
        }
    }

    private void ApplyLargeMargins()
    {
        ContentPanel.Margin = new Thickness(5, 10, 0, 0);
        CoverGrid.Margin = new Thickness(5, 0, 5, 0);
        TitleCanvas.Margin = new Thickness(5, 5, 5, 0);
        SongArtist.Margin = new Thickness(5, 5, 5, 12);
    }

    private void SetCompactMargins()
    {
        ContentPanel.Margin = new Thickness(10, 0, 15, 0);
        CoverGrid.Margin = new Thickness(4, 0, 4, 0);
        TitleCanvas.Margin = new Thickness(0);
        SongArtist.Margin = new Thickness(0);
        LayoutCompact.Margin = new Thickness(10, 18, 0, 0);
    }

    /// <summary>当父窗口位置改变时，更新内容对齐方向。</summary>
    public void SetAlignmentFromParent(bool isLeft)
    {
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
            CoverGrid.HorizontalAlignment = halign;
            TitleCanvas.HorizontalAlignment = halign;
            SongArtist.TextAlignment = talign;
            if (!_marqueeTimer.IsEnabled && SongTitle.Width == 220)
                SongTitle.TextAlignment = talign;
        }
        else
        {
            Grid.SetColumn(CompactCoverSlot, isLeft ? 0 : 1);
            Grid.SetColumn(CompactTextSlot, isLeft ? 1 : 0);
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
    }

    // ── 元素迁移 ──────────────────────────────────────────

    private static void EnsureChildInPanel(Panel panel, UIElement child)
    {
        var currentParent = VisualTreeHelper.GetParent(child);
        if (currentParent == panel) return;

        if (currentParent is Decorator decorator)
            decorator.Child = null;
        else if (currentParent is Panel parentPanel)
            parentPanel.Children.Remove(child);

        panel.Children.Add(child);
    }

    private static void MoveChildToSlot(UIElement child, Border slot)
    {
        if (VisualTreeHelper.GetParent(child) is Panel currentParent)
            currentParent.Children.Remove(child);
        slot.Child = child;
    }

    private static void MoveChildToSlot(UIElement child, Panel panel)
    {
        if (VisualTreeHelper.GetParent(child) is Panel currentParent)
            currentParent.Children.Remove(child);
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
            availableWidth = 190 - 20 - 68; // 窗口 - ContentPanel margin - cover slot
            TitleCanvas.Width = availableWidth - 5;
        }
        else
        {
            availableWidth = TitleCanvas.Width;
        }

        if (TitleMarquee.NeedsScroll(title, availableWidth, SongTitle.FontSize))
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
            _marqueeOffset = visibleWidth;
        TitleTranslate.X = _marqueeOffset;
    }

    // ── 动画 ──────────────────────────────────────────────

    internal static void ResetPanelToVisibleState(StackPanel panel, Thickness? restoreMargin = null)
    {
        if (panel == null) return;
        panel.BeginAnimation(OpacityProperty, null);
        panel.BeginAnimation(MarginProperty, null);
        if (panel.RenderTransform is TranslateTransform transform)
            transform.BeginAnimation(TranslateTransform.XProperty, null);
        panel.RenderTransform = new TranslateTransform(0, 0);
        panel.Opacity = 1;
        panel.Margin = restoreMargin ?? new Thickness(0);
    }

    private void PlaySongSwitchAnimation(Action onMidpoint, Action? onPhase2Complete = null)
    {
        var panel = ContentPanel;

        var restoreMargin = _sizeMode == FloatSizeMode.Large
            ? new Thickness(5, 10, 0, 0)
            : new Thickness(0);
        ResetPanelToVisibleState(panel, restoreMargin);

        bool isLeft = _isOnLeftSide ?? true;
        double phase1Slide = isLeft ? -35 : 35;
        double phase2From = isLeft ? 35 : -35;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        var slideOut = new DoubleAnimation(0, phase1Slide, TimeSpan.FromMilliseconds(200));

        var sbOut = new Storyboard();
        Storyboard.SetTarget(fadeOut, panel);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(slideOut, panel);
        Storyboard.SetTargetProperty(slideOut, new PropertyPath("RenderTransform.(TranslateTransform.X)"));
        sbOut.Children.Add(fadeOut);
        sbOut.Children.Add(slideOut);

        sbOut.Completed += (_, _) =>
        {
            panel.BeginAnimation(OpacityProperty, null);
            if (panel.RenderTransform is TranslateTransform transform)
                transform.BeginAnimation(TranslateTransform.XProperty, null);

            onMidpoint();

            panel.Opacity = 0;
            panel.RenderTransform = new TranslateTransform(phase2From, 0);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideIn = new DoubleAnimation(phase2From, 0, TimeSpan.FromMilliseconds(200));

            var sbIn = new Storyboard();
            Storyboard.SetTarget(fadeIn, panel);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(slideIn, panel);
            Storyboard.SetTargetProperty(slideIn, new PropertyPath("RenderTransform.(TranslateTransform.X)"));
            sbIn.Children.Add(fadeIn);
            sbIn.Children.Add(slideIn);

            sbIn.Completed += (_, _) =>
            {
                panel.BeginAnimation(OpacityProperty, null);
                if (panel.RenderTransform is TranslateTransform t)
                    t.BeginAnimation(TranslateTransform.XProperty, null);
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
                    estimatedWidth += fontSize * CnCharWidthFactor;
                else
                    estimatedWidth += fontSize * EnCharWidthFactor;
            }
            return estimatedWidth > availableWidth;
        }
    }

    public static class AlignmentHelper
    {
        public static bool IsLeftSide(double windowLeft, double screenWidth) =>
            windowLeft <= screenWidth / 2.0;

        public static HorizontalAlignment GetHorizontalAlignment(bool? isLeft) => isLeft switch
        {
            true => HorizontalAlignment.Left,
            false => HorizontalAlignment.Right,
            null => HorizontalAlignment.Center
        };

        public static System.Windows.TextAlignment GetTextAlignment(bool? isLeft) => isLeft switch
        {
            true => System.Windows.TextAlignment.Left,
            false => System.Windows.TextAlignment.Right,
            null => System.Windows.TextAlignment.Center
        };
    }
}

/// <summary>悬浮窗大小模式。</summary>
public enum FloatSizeMode { Large, Compact }
