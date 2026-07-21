using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Toolbox.Core.Controls;
using Toolbox.Core.Services;
using Toolbox.Services;
using Toolbox.Tools.Models;
using Toolbox.Tools.Views;
using Windows.Media.Control;

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

    // ── 封面交叉淡入动画追踪 ──
    private DoubleAnimation? _currentFadeIn;

    // ── 切歌动画进行中标记，防止封面空数据时误清旧封面 ──
    private bool _isSwitchingSong;

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

        // 控件卸载时停止跑马灯定时器，避免空转
        Unloaded += (_, _) => _marqueeTimer.Stop();

        // 窗口隐藏 / 贴边缩入导致内容不可见时停止跑马灯，重新可见时按需恢复
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) StartOrStopTitleMarquee();
            else _marqueeTimer.Stop();
        };

        // 将整个内容区域的鼠标按下事件作为拖拽请求
        ContentPanel.MouseLeftButtonDown += (_, e) =>
        {
            // 点击播放控制按钮时不触发窗口拖拽
            if (e.OriginalSource is DependencyObject src && IsWithin(src, PlaybackControls))
                return;
            try { DragRequested?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Debug.WriteLine($"[MusicContentControl] DragRequested 异常: {ex.Message}"); }
        };

        // 右键菜单：悬浮窗控制收口（游戏模式下窗口穿透，此事件本就不会触发）
        ContentPanel.MouseRightButtonUp += (_, e) =>
        {
            if (AudioflowSettings.Instance.ClickThroughEnabled) return;
            e.Handled = true;
            ShowFloatContextMenu();
        };

        // 悬停触发播放控制：统一在根容器做 MouseMove 区域判定。
        // 覆盖层是根容器的子元素，鼠标移到覆盖层上不会误判为"离开"，
        // 避免"弹出→遮住触发区→收起→再弹出"的闪烁循环
        RootGrid.MouseMove += (_, e) =>
        {
            if (_sizeMode == FloatSizeMode.Compact)
            {
                ShowPlaybackControlsIfAllowed(); // 紧凑模式：整个窗口都是触发范围
                return;
            }
            var pos = e.GetPosition(CoverGrid);
            if (pos.X >= 0 && pos.Y >= 0
                && pos.X <= CoverGrid.ActualWidth && pos.Y <= CoverGrid.ActualHeight)
                ShowPlaybackControlsIfAllowed();
            else
                HidePlaybackControls();
        };
        RootGrid.MouseLeave += (_, _) => HidePlaybackControls();
        WirePlaybackButton(BtnPrev, MusicFloatWindowManager.Instance.SkipPrevious,
            Color.FromArgb(0x26, 0x00, 0x00, 0x00));
        WirePlaybackButton(BtnPlayPause, MusicFloatWindowManager.Instance.TogglePlayPause,
            Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A));
        WirePlaybackButton(BtnNext, MusicFloatWindowManager.Instance.SkipNext,
            Color.FromArgb(0x26, 0x00, 0x00, 0x00));

        // 设置面板/右键菜单关闭播放控制时，若按钮正浮出则立即隐藏
        AudioflowSettings.Instance.PropertyChanged += OnAudioflowSettingChanged;
        Unloaded += (_, _) => AudioflowSettings.Instance.PropertyChanged -= OnAudioflowSettingChanged;

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
        if (info == null) return;
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
        Dispatcher.InvokeAsync(() =>
        {
            // 窗口已关闭或控件已卸载时不处理
            if (!IsLoaded) return;

            var isNewSong = NowPlayingInfo.IsSongChanged(_previousInfo, info);
            var isCoverUpdate = !isNewSong
                && NowPlayingInfo.IsThumbnailChanged(_previousInfo, info);
            var isStatusChanged = !isNewSong
                && _previousInfo.PlaybackStatus != info.PlaybackStatus;

            if (isNewSong)
            {
                if (info.RefreshVersion < _lastSongChangeVersion)
                {
                    // 直接忽略过期事件，不要污染 _previousInfo 状态
                    return;
                }

                _lastSongChangeVersion = info.RefreshVersion;
                if (_previousInfo.PlaybackStatus == null)
                {
                    // 首次同步：直接用快照状态初始化播放/暂停图标
                    UpdatePlayPauseGlyph(info.PlaybackStatus);
                }
                else
                {
                    // 切歌快照可能携带过渡态的过期播放状态（SMTC 切换瞬时读数不可靠），
                    // 延迟 70ms 等状态稳定后，用 Manager 缓存的最新信息重同步图标
                    ScheduleGlyphSync();
                }
                _previousInfo = info;

                PlaySongSwitchAnimation(
                    onMidpoint: () => ApplySongInfo(info),
                    onPhase2Complete: () =>
                    {
                        StartOrStopTitleMarquee();
                    });
            }
            else if (isCoverUpdate)
            {
                _previousInfo = info;
                if (info.RefreshVersion >= _lastSongChangeVersion)
                    LoadCoverFromData(info.ThumbnailData);
            }
            else if (isStatusChanged)
            {
                _previousInfo = info;
                // 图标刷新与封面状态动画同路径：以 PlaybackInfoChanged 事件为准，
                // 70ms 后再按最新缓存状态复核一次
                UpdatePlayPauseGlyph(info.PlaybackStatus);
                ScheduleGlyphSync();
                AnimateCoverForPlaybackStatus(info.PlaybackStatus);
            }
            else
            {
                _previousInfo = info;
            }
        });
        }
        catch (TaskCanceledException) { /* 进程关闭中 */ }
        catch (InvalidOperationException) { /* Dispatcher 已关闭 */ }
        catch (Exception ex) { Debug.WriteLine($"[MusicContentControl] UpdateSongInfo 异常: {ex.Message}"); }
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
        if (thumbnailData == null || thumbnailData.Length == 0)
        {
            // 切歌动画期间 SMTC 封面可能尚未就绪，保留旧封面避免闪空
            if (!_isSwitchingSong)
            {
                CoverImage.Source = null;
                CoverImageBack.Source = null;
            }
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

            // 取消上一个正在进行的淡入动画，避免 Completed 回调错乱
            if (_currentFadeIn != null)
            {
                _currentFadeIn.Completed -= OnFadeInCompleted;
                CoverImage.BeginAnimation(OpacityProperty, null);
                CoverImage.Opacity = 1; // 上一个动画中断时，立即完成显示
            }

            CoverImageBack.Source = CoverImage.Source;
            CoverImage.Source = bitmap;
            CoverImage.Opacity = 0;

            _currentFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            _currentFadeIn.Completed += OnFadeInCompleted;
            CoverImage.BeginAnimation(OpacityProperty, _currentFadeIn);
        }
        catch { }
    }

    private void OnFadeInCompleted(object? sender, EventArgs e)
    {
        // 仅在当前动画仍为完成的对象时清理，避免误触后续动画
        if (sender != _currentFadeIn) return;
        CoverImageBack.Source = null;
        CoverImage.BeginAnimation(OpacityProperty, null);
        CoverImage.Opacity = 1;
        _currentFadeIn = null;
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
            // 大模式：播放控制移回封面内部，随封面移动，对齐天然正确
            MovePlaybackControlsTo(CoverGrid);
            PlaybackControls.VerticalAlignment = VerticalAlignment.Center;
            PlaybackControls.Margin = new Thickness(0);
        }
        else
        {
            MoveChildToSlot(CoverGrid, CompactCoverSlot);
            MoveChildToSlot(TitleCanvas, CompactTextSlot);
            MoveChildToSlot(SongArtist, CompactTextSlot);
            LayoutCompact.Visibility = Visibility.Visible;
            LayoutLarge.Visibility = Visibility.Collapsed;
            ApplyCoverMetrics(60, 3, 5, 1.3);
            FireSizeRequired(198, 96);
            SetCompactMargins();
            // 紧凑模式：播放控制移到根容器，整窗居中（封面只有 60px 放不下）
            MovePlaybackControlsTo(RootGrid);
            PlaybackControls.VerticalAlignment = VerticalAlignment.Center;
            PlaybackControls.Margin = new Thickness(0);
        }
        StartOrStopTitleMarquee();
        ApplyAlignment(_isOnLeftSide ?? true);
    }

    /// <summary>把播放控制覆盖层移动到指定父容器（大模式=封面内，紧凑模式=根容器）。</summary>
    private void MovePlaybackControlsTo(Panel newParent)
    {
        if (PlaybackControls.Parent == newParent) return;
        if (PlaybackControls.Parent is Panel oldParent)
            oldParent.Children.Remove(PlaybackControls);
        newParent.Children.Add(PlaybackControls);
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
        ContentPanel.Margin = new Thickness(5, 10, 5, 0);
        CoverGrid.Margin = new Thickness(5, 0, 5, 0);
        TitleCanvas.Margin = new Thickness(5, 5, 5, 0);
        SongArtist.Margin = new Thickness(5, 5, 5, 12);
    }

    private void SetCompactMargins()
    {
        ContentPanel.Margin = new Thickness(0, 0, 0, 0);
        CoverGrid.Margin = new Thickness(4, 0, 4, 0);
        TitleCanvas.Margin = new Thickness(0);
        SongArtist.Margin = new Thickness(0);
        LayoutCompact.Margin = new Thickness(11, 18, 11, 0);
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
            if (!_marqueeTimer.IsEnabled && _sizeMode == FloatSizeMode.Large)
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
            availableWidth = 198 - 0 - 22 - 68; // 窗口(198) - ContentPanel margin(0) - LayoutCompact margin(22) - cover slot
            TitleCanvas.Width = availableWidth;
        }
        else
        {
            TitleCanvas.Width = 220; // 恢复 XAML 初始宽度
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
        try
        {
            _marqueeOffset -= 0.45; // 滚动速度（px/40ms）
            var textWidth = SongTitle.ActualWidth;
            var visibleWidth = TitleCanvas.Width;
            if (_marqueeOffset < -(textWidth + 24))
            {
                // 从右边缘渐隐区内重新进入，缩短完全空白的间隔
                _marqueeOffset = Math.Max(visibleWidth - 12, 40);
            }
            TitleTranslate.X = _marqueeOffset;
        }
        catch { /* 控件卸载中，忽略 */ }
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
            ? new Thickness(5, 10, 5, 0)
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
                _isSwitchingSong = false;
                onPhase2Complete?.Invoke();
            };

            sbIn.Begin();
        };

        _isSwitchingSong = true;
        sbOut.Begin();
    }

    // ── 播放状态 → 封面缩放动画 ─────────────────────────

    /// <summary>
    /// 根据播放状态缩放封面：暂停 → 90%，播放 → 100%，300ms 缓出动画。
    /// </summary>
    private void AnimateCoverForPlaybackStatus(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
    {
        double targetScale = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused
            ? 0.85 : 1.0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(300);

        // 先读当前动画中的实时值作为起点，再启动新动画（BeginAnimation 会直接替换旧动画）。
        // 不能先 BeginAnimation(null) 清动画——那会把 scale 瞬间弹回基准值 1.0，造成"闪现"
        double fromX = CoverScaleTransform.ScaleX;
        double fromY = CoverScaleTransform.ScaleY;

        CoverScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(fromX, targetScale, duration) { EasingFunction = ease });
        CoverScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(fromY, targetScale, duration) { EasingFunction = ease });
    }

    // ── 播放控制按钮 & 右键菜单 ─────────────────────────

    /// <summary>播放控制当前是否处于浮出状态（防止 MouseMove 反复重启动画）。</summary>
    private bool _controlsShown;

    private void ShowPlaybackControlsIfAllowed()
    {
        if (_controlsShown) return; // 已浮出，避免 MouseMove 反复重启动画
        if (!AudioflowSettings.Instance.ShowPlaybackControls) return;
        if (AudioflowSettings.Instance.ClickThroughEnabled) return; // 游戏模式不浮出
        _controlsShown = true;
        PlaybackControls.IsHitTestVisible = true;
        FadeTo(PlaybackControls, 1);
        SlideTo(PlaybackControls, 0);
    }

    private void HidePlaybackControls()
    {
        if (!_controlsShown) return;
        _controlsShown = false;
        PlaybackControls.IsHitTestVisible = false;
        FadeTo(PlaybackControls, 0);
        SlideTo(PlaybackControls, 8);
    }

    private static void FadeTo(UIElement element, double targetOpacity)
    {
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>浮出/收起时伴随轻微纵向滑动，增加层次感。</summary>
    private static void SlideTo(UIElement element, double targetY)
    {
        if (element.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform(0, targetY);
            element.RenderTransform = translate;
        }
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>绑定播放按钮：悬停高亮 + 点击缩放反馈 + 执行动作。</summary>
    private void WirePlaybackButton(Border btn, Action action, Color hoverBackground)
    {
        var normalBrush = btn.Background;
        var hoverBrush = new SolidColorBrush(hoverBackground);
        btn.MouseEnter += (_, _) => btn.Background = hoverBrush;
        btn.MouseLeave += (_, _) => btn.Background = normalBrush;
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ClickFeedback(btn);
            action();
        };
    }

    /// <summary>点击反馈：瞬间缩到 0.82 再弹性回 1。</summary>
    private static void ClickFeedback(Border btn)
    {
        if (btn.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            btn.RenderTransform = scale;
            btn.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var anim = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>判断元素是否位于某祖先元素的视觉树内。</summary>
    private static bool IsWithin(DependencyObject child, DependencyObject ancestor)
    {
        for (var d = child; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d == ancestor) return true;
        }
        return false;
    }

    /// <summary>图标延迟重同步定时器（一次性）。</summary>
    private System.Windows.Threading.DispatcherTimer? _glyphSyncTimer;

    /// <summary>
    /// 延迟 70ms 后重同步播放/暂停图标。
    /// 必须实时读取 SMTC 会话状态——缓存快照本身可能就是携带过渡态旧值的那条。
    /// 连续触发时重置计时。
    /// </summary>
    private void ScheduleGlyphSync()
    {
        _glyphSyncTimer?.Stop();
        _glyphSyncTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(70),
            System.Windows.Threading.DispatcherPriority.Normal,
            (_, _) =>
            {
                _glyphSyncTimer?.Stop();
                UpdatePlayPauseGlyph(MusicFloatWindowManager.Instance.GetLivePlaybackStatus());
            },
            Dispatcher);
        _glyphSyncTimer.Start();
    }

    /// <summary>设置变更回调：播放控制被关闭时立即收起重出的按钮。</summary>
    private void OnAudioflowSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioflowSettings.ShowPlaybackControls)
            && !AudioflowSettings.Instance.ShowPlaybackControls)
            HidePlaybackControls();
    }

    private void UpdatePlayPauseGlyph(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
    {
        var isPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayIcon.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>悬浮窗右键菜单：集中锁定/大小/毛玻璃/游戏模式/复位等控制。</summary>
    private void ShowFloatContextMenu()
    {
        var settings = AudioflowSettings.Instance;
        var isCompact = AppSettings.Instance.MusicFloatSizeMode == "Compact";

        var items = new List<ThemedMenuWindow.Item>
        {
            new() { Text = "锁定位置", IsChecked = settings.LockFloatWindow,
                Action = () => settings.LockFloatWindow = !settings.LockFloatWindow },
            new() { Text = "贴边自动缩入", IsChecked = settings.EdgeDockEnabled,
                Action = () => settings.EdgeDockEnabled = !settings.EdgeDockEnabled },
            new() { Text = "毛玻璃背景", IsChecked = settings.FloatWindowBlurEnabled,
                Action = () => settings.FloatWindowBlurEnabled = !settings.FloatWindowBlurEnabled },
            new() { Text = "游戏模式（鼠标穿透）", IsChecked = settings.ClickThroughEnabled,
                Action = () => settings.ClickThroughEnabled = !settings.ClickThroughEnabled },
            new() { Text = "悬停播放控制", IsChecked = settings.ShowPlaybackControls,
                Action = () => settings.ShowPlaybackControls = !settings.ShowPlaybackControls },
            ThemedMenuWindow.Item.Separator(),
            new() { Text = "大模式", IsChecked = !isCompact, Action = () => SwitchSizeMode("Large") },
            new() { Text = "紧凑模式", IsChecked = isCompact, Action = () => SwitchSizeMode("Compact") },
            ThemedMenuWindow.Item.Separator(),
            new() { Text = "复位位置", Action = MusicFloatWindowManager.Instance.ResetPosition },
        };

        // PointToScreen 返回物理像素，TransformFromDevice 转回 DIP（菜单位置用）
        var pt = PointToScreen(Mouse.GetPosition(this));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            pt = target.TransformFromDevice.Transform(pt);

        ThemedMenuWindow.ShowAt(pt, items);
    }

    private static void SwitchSizeMode(string mode)
    {
        AppSettings.Instance.MusicFloatSizeMode = mode;
        MusicFloatWindowManager.Instance.SetSizeMode(
            mode == "Compact" ? FloatSizeMode.Compact : FloatSizeMode.Large);
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
