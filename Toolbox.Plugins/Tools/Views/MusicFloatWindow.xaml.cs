using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Tools.Models;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 网易云音乐实时信息悬浮窗（精简版）。
/// 单例模式，通过 Instance 属性获取唯一实例。
/// 悬浮窗固定在桌面左侧，置顶显示，支持拖拽移动。
/// 仅显示封面、歌曲标题和歌手，无控制按钮/进度条。
/// </summary>
public partial class MusicFloatWindow : Window
{
    private static MusicFloatWindow? _instance;
    private static readonly object _lock = new();

    private readonly SMTCListener _listener = new();
    private NowPlayingInfo _previousInfo = new();
    private int _lastSongChangeVersion = -1; // 最新切歌的 RefreshVersion，用于防过期加载覆盖

    // 歌名滚动（跑马灯）
    private readonly System.Windows.Threading.DispatcherTimer _marqueeTimer;
    private double _marqueeOffset;
    private bool? _isOnLeftSide = null;

    private MusicFloatWindow()
    {
        InitializeComponent();

        // 歌名滚动定时器：每 40ms 移动一次（约 25fps，流畅缓慢滚动）
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
            OnWindowLocationChanged(null, EventArgs.Empty);
        };

        // 联动关闭：主窗口关闭时也关闭悬浮窗
        if (Application.Current.MainWindow != null)
        {
            Application.Current.MainWindow.Closed += (s, e) => Close();
        }
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
    /// 打开悬浮窗并开始监听 SMTC 会话。
    /// </summary>
    public new void Show()
    {
        if (!_listener.IsListening)
        {
            _ = _listener.StartAsync();
        }
        base.Show();
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
        base.OnClosed(e);
    }

    // ── 事件处理 ──────────────────────────────────────────

    private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
    {
        // SMTC 事件在 MTA 线程触发，需 Dispatch 到 UI 线程
        Dispatcher.Invoke(() =>
        {
            var isNewSong = NowPlayingInfo.IsSongChanged(_previousInfo, info);
            var isCoverUpdate = !isNewSong
                && NowPlayingInfo.IsThumbnailChanged(_previousInfo, info);

            if (isNewSong)
            {
                // 修复 2：版本守卫——丢弃陈旧切歌事件（快速连切 A→B→C 时防止 B 回退）
                if (info.RefreshVersion < _lastSongChangeVersion)
                {
                    // 仅更新 _previousInfo，不播放动画、不更新 UI
                    _previousInfo = info;
                    return;
                }

                _lastSongChangeVersion = info.RefreshVersion;
                _previousInfo = info;       // 动画前更新基准，避免动画期间事件误判为切歌

                PlaySongSwitchAnimation(
                    onMidpoint: () =>
                    {
                        // onMidpoint 时面板 Opacity=0（不可见），此时换封面无闪烁感知
                        ApplySongInfo(info);
                    },
                    onPhase2Complete: () =>
                    {
                        StartOrStopTitleMarquee();
                    });
            }
            else if (isCoverUpdate)
            {
                // 非切歌事件但封面变了 → 直接交叉淡入，不触发动画
                _previousInfo = info;

                // 防过期
                if (info.RefreshVersion >= _lastSongChangeVersion)
                {
                    LoadCoverFromData(info.ThumbnailData);
                }
            }
            else
            {
                // 封面未变的非切歌事件（如进度更新）→ 仅更新 _previousInfo 引用
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
    ///  ① 将当前 CoverImage.Source 移到 CoverImageBack（旧封面继续显示）
    ///  ② CoverImage 设为新封面，Opacity=0 → 动画到 1（150ms 淡入）
    ///  ③ 淡入完成后清空 CoverImageBack（释放旧封面）
    /// 修复 3(a)：null/空数据不做任何操作，保留现有封面。
    /// </summary>
    private void LoadCoverFromData(byte[]? thumbnailData)
    {
        if (thumbnailData == null || thumbnailData.Length == 0)
        {
            // 修复 3(a)：保留现有封面，不清空
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

            // ① 旧封面移到底层（在淡入期间保持可见）
            CoverImageBack.Source = CoverImage.Source;
            // ② 置新封面到顶层，Opacity=0 准备淡入
            CoverImage.Source = bitmap;
            CoverImage.Opacity = 0;

            // ③ 顶层 0→1 淡入动画（150ms）
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(
                0, 1, TimeSpan.FromMilliseconds(150));
            fadeIn.Completed += (_, _) =>
            {
                // 淡入完成 → 清空底层旧封面，释放引用
                CoverImageBack.Source = null;
                CoverImage.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                CoverImage.Opacity = 1; // 确保最终状态
            };
            CoverImage.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeIn);
        }
        catch
        {
            // 封面加载失败时保持现有封面
        }
    }

    // ── 拖拽 ──────────────────────────────────────────────

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
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

        // 封面 Grid 整体对齐（内部阴影层无 x:Name，随 Grid 一同平移）
        CoverGrid.HorizontalAlignment = halign;
        TitleCanvas.HorizontalAlignment = halign;
        SongArtist.TextAlignment = talign;

        if (!_marqueeTimer.IsEnabled && SongTitle.Width == 220)
        {
            SongTitle.TextAlignment = talign;
        }
    }

    // ── 歌名滚动 ──────────────────────────────────────────

    private void StartOrStopTitleMarquee()
    {
        _marqueeTimer.Stop();
        _marqueeOffset = 0;
        TitleTranslate.X = 0;

        var title = SongTitle.Text ?? "";
        var availableWidth = TitleCanvas.Width;
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
            SongTitle.Width = 220;
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

    /// <summary>
    /// 将面板重置为可见状态（Opacity=1, Margin=0）并清除所有残留动画时钟。
    /// 使用 BeginAnimation(null)（非 Storyboard.Stop()）确保同步移除。
    /// </summary>
    internal static void ResetPanelToVisibleState(System.Windows.Controls.StackPanel panel)
    {
        if (panel == null) return;
        // 用 BeginAnimation(null) 同步清除残留动画时钟
        panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
        panel.BeginAnimation(FrameworkElement.MarginProperty, null);
        // 同时清除 RenderTransform(TranslateTransform) 上的动画时钟
        var transform = panel.RenderTransform as TranslateTransform;
        transform?.BeginAnimation(TranslateTransform.XProperty, null);
        panel.RenderTransform = new TranslateTransform(0, 0);
        // 设置本地值
        panel.Opacity = 1;
        panel.Margin = new Thickness(0);
    }

    /// <summary>
    /// 歌曲切换动画：淡出左滑 → 更新内容 → 从右淡入。
    /// 用 BeginAnimation(null) 替代 Storyboard.Stop() 同步清除动画时钟，
    /// 避免 HoldEnd 残留导致 Phase 2 Begin() 静默失败。
    /// </summary>
    private void PlaySongSwitchAnimation(Action onMidpoint, Action? onPhase2Complete = null)
    {
        var panel = ContentPanel;

        // Step 0: 清除可能残留的动画时钟 + 设置干净起点
        ResetPanelToVisibleState(panel);

        // 根据窗口位置决定动画方向
        // 左侧：淡出左滑(X:-35)→从右滑入(X:+35→0)
        // 右侧：淡出右滑(X:+35)→从左滑入(X:-35→0)
        bool isLeft = _isOnLeftSide ?? true;
        double phase1Slide = isLeft ? -35 : 35;
        double phase2From = isLeft ? 35 : -35;

        // ── Phase 1: 淡出 + 滑动（200ms）──
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        var slideOut = new System.Windows.Media.Animation.DoubleAnimation(
            0, phase1Slide, TimeSpan.FromMilliseconds(200));

        var sbOut = new System.Windows.Media.Animation.Storyboard();
        System.Windows.Media.Animation.Storyboard.SetTarget(fadeOut, panel);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeOut,
            new PropertyPath(System.Windows.UIElement.OpacityProperty));
        System.Windows.Media.Animation.Storyboard.SetTarget(slideOut, panel);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideOut,
            new PropertyPath("RenderTransform.(TranslateTransform.X)"));
        sbOut.Children.Add(fadeOut);
        sbOut.Children.Add(slideOut);

        sbOut.Completed += (_, _) =>
        {
            // 清除 Phase 1 的 HoldEnd 时钟
            panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
            var transform = panel.RenderTransform as TranslateTransform;
            transform?.BeginAnimation(TranslateTransform.XProperty, null);

            onMidpoint();

            panel.Opacity = 0;
            panel.RenderTransform = new TranslateTransform(phase2From, 0);

            // ── Phase 2: 从另一侧淡入（200ms）──
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideIn = new System.Windows.Media.Animation.DoubleAnimation(
                phase2From, 0, TimeSpan.FromMilliseconds(200));

            var sbIn = new System.Windows.Media.Animation.Storyboard();
            System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, panel);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn,
                new PropertyPath(System.Windows.UIElement.OpacityProperty));
            System.Windows.Media.Animation.Storyboard.SetTarget(slideIn, panel);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideIn,
                new PropertyPath("RenderTransform.(TranslateTransform.X)"));
            sbIn.Children.Add(fadeIn);
            sbIn.Children.Add(slideIn);

            sbIn.Completed += (_, _) =>
            {
                // Phase 2 结束：清除 HoldEnd，恢复干净状态
                panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                var t = panel.RenderTransform as TranslateTransform;
                t?.BeginAnimation(TranslateTransform.XProperty, null);
                panel.Opacity = 1;
                panel.RenderTransform = new TranslateTransform(0, 0);

                // Phase 2 完成后执行布局敏感操作（跑马灯等）
                onPhase2Complete?.Invoke();
            };

            sbIn.Begin();
        };

        sbOut.Begin();
    }

    /// <summary>
    /// 歌名滚动判定工具。用于判断文本是否超出可用宽度而需要跑马灯滚动。
    /// </summary>
    public static class TitleMarquee
    {
        private const double CnCharWidthFactor = 1.05;
        private const double EnCharWidthFactor = 0.55;

        /// <summary>
        /// 判断指定文本在给定可用宽度和字体大小下是否需要滚动显示。
        /// </summary>
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