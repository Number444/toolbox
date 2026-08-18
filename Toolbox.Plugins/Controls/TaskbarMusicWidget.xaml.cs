using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Toolbox.Plugins.Helpers;
using Toolbox.Tools.Models;
using Windows.Media.Control;

namespace Toolbox.Plugins.Controls;

/// <summary>
/// 任务栏迷你媒体控件（固定宽 210px，纯展示——播放操作集中在弹出卡片）。
/// 平时全透明与任务栏融为一体；悬停时显现 15% 圆角选中框。
/// 深浅主题自动适配文字/选中框颜色。
/// </summary>
public partial class TaskbarMusicWidget : UserControl
{
    // ── 歌名滚动（跑马灯）──
    private readonly System.Windows.Threading.DispatcherTimer _marqueeTimer;
    private double _marqueeOffset;

    // ── 当前歌曲信息 ──
    private string _actualTitle = string.Empty;
    private string _actualArtist = string.Empty;
    private NowPlayingInfo? _lastInfo;

    // ── 固定宽度（与宿主窗口一致，切歌不跳宽）──
    public const double FixedWidth = 210;
    public const double FixedHeight = 40;

    // ── 主题 ──
    private bool _isLightTheme;
    private Brush _textBrush = Brushes.White;
    private Brush _secondaryTextBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

    // ── 悬停状态 ──
    private bool _isHovering;

    public TaskbarMusicWidget()
    {
        InitializeComponent();

        // 全透明背景（与任务栏融为一体）
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        // 歌名滚动定时器
        _marqueeTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(40),
            System.Windows.Threading.DispatcherPriority.Normal,
            OnMarqueeTick,
            Dispatcher);
        _marqueeTimer.Stop();

        // 控件卸载时清理
        Unloaded += (_, _) => _marqueeTimer.Stop();

        ApplyTheme(TaskbarThemeHelper.IsLightTheme());
    }

    // ── 主题适配 ──

    /// <summary>应用深浅主题配色（文字、图标、悬停色）。</summary>
    public void ApplyTheme(bool light)
    {
        _isLightTheme = light;
        var text = TaskbarThemeHelper.TextColor(light);
        var secondary = TaskbarThemeHelper.SecondaryTextColor(light);

        _textBrush = new SolidColorBrush(text);
        _textBrush.Freeze();
        _secondaryTextBrush = new SolidColorBrush(secondary);
        _secondaryTextBrush.Freeze();

        SongTitle.Foreground = _textBrush;
        SongArtist.Foreground = _secondaryTextBrush;
        SongImageBorder.BorderBrush = new SolidColorBrush(TaskbarThemeHelper.CoverStrokeColor(light));

        // 悬停中则立即刷新选中框颜色
        if (_isHovering && MainBorder.Background is SolidColorBrush scb)
        {
            scb.Color = TaskbarThemeHelper.HoverHighlightColor(light);
        }
    }

    // ── 歌曲信息更新 ──

    public void UpdateSongInfo(NowPlayingInfo info)
    {
        if (info == null) return;
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded) return;

            string title = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
            string artist = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;

            bool isNewSong = NowPlayingInfo.IsSongChanged(_lastInfo, info);
            _actualTitle = title;
            _actualArtist = artist;

            SongTitle.Text = title;
            SongArtist.Text = artist;

            // 播放状态：封面右下角角标（播放中 ▶ / 已暂停 ⏸，无歌曲时隐藏）
            bool isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool hasSong = info.HasSong;
            PlayStateBadge.Visibility = hasSong ? Visibility.Visible : Visibility.Collapsed;
            PlayStateBadgeGlyph.Text = isPlaying ? "\uE768" : "\uE769";

            // 封面变化时才重新加载（字节比较，防同一封面反复解码）
            if (NowPlayingInfo.IsThumbnailChanged(_lastInfo, info))
                LoadCover(info.ThumbnailData);

            if (isNewSong)
                StartOrStopTitleMarquee();

            _lastInfo = info;
        });
    }

    private void LoadCover(byte[]? thumbnailData)
    {
        if (thumbnailData == null || thumbnailData.Length == 0)
        {
            SongImageBrush.ImageSource = null;
            SongImagePlaceholder.Visibility = Visibility.Visible;
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

            SongImageBrush.ImageSource = bitmap;
            SongImagePlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarMusicWidget] 封面加载失败: {ex.Message}");
        }
    }

    // ── 悬停效果：15% 圆角选中框 ──

    private void MainBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovering = true;

        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
        }

        var targetColor = TaskbarThemeHelper.HoverHighlightColor(_isLightTheme);
        var bgAnim = new ColorAnimation
        {
            To = targetColor,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
    }

    private void MainBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovering = false;

        if (MainBorder.Background is SolidColorBrush scb)
        {
            scb.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Colors.Transparent, TimeSpan.FromMilliseconds(200)));
        }
    }

    // ── 歌名滚动 ──

    private void StartOrStopTitleMarquee()
    {
        _marqueeTimer.Stop();
        _marqueeOffset = 0;
        TitleTranslate.X = 0;
        ArtistTranslate.X = 0;

        double availableWidth = SongTitleContainer.ActualWidth > 0 ? SongTitleContainer.ActualWidth : 100;

        double titleWidth = MeasureTextWidth(SongTitle.Text, 12.5, FontWeights.SemiBold);
        double artistWidth = MeasureTextWidth(SongArtist.Text, 10);

        if (titleWidth > availableWidth || artistWidth > availableWidth)
        {
            _marqueeTimer.Start();
        }
    }

    private void OnMarqueeTick(object? sender, EventArgs e)
    {
        try
        {
            _marqueeOffset -= 0.45;
            double titleWidth = MeasureTextWidth(SongTitle.Text, 12.5);
            double artistWidth = MeasureTextWidth(SongArtist.Text, 10);
            double maxWidth = Math.Max(titleWidth, artistWidth);
            double visibleWidth = SongTitleContainer.ActualWidth > 0 ? SongTitleContainer.ActualWidth : 100;

            if (_marqueeOffset < -(maxWidth + 24))
            {
                _marqueeOffset = visibleWidth;
            }

            TitleTranslate.X = _marqueeOffset;
            ArtistTranslate.X = _marqueeOffset;
        }
        catch { /* 控件卸载中，忽略 */ }
    }

    /// <summary>用 FormattedText 精确测量文本宽度（字体族/字重与 XAML 显示一致，替代字符估算）。</summary>
    private double MeasureTextWidth(string text, double fontSize, FontWeight? weight = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        try
        {
            var ft = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
                    FontStyles.Normal,
                    weight ?? FontWeights.Normal,
                    FontStretches.Normal),
                fontSize,
                _textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return ft.Width;
        }
        catch
        {
            return text.Length * fontSize * 0.6;
        }
    }

    // ── 尺寸（固定宽度）──

    public (double logicalWidth, double logicalHeight) CalculateSize() => (FixedWidth, FixedHeight);
}
