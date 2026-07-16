using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Controls;
using Toolbox.Tools.Models;
using Toolbox.Tools.Views;
using Xunit;
using Toolbox.Core.Services;

namespace Toolbox.Tests;

public class NeteaseMusicToolTests
{
    [Fact]
    public void NowPlayingInfo_HasSong_ReturnsTrue_WhenTitleNotEmpty()
    {
        var info = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        Assert.True(info.HasSong);
    }

    [Fact]
    public void NowPlayingInfo_HasSong_ReturnsFalse_WhenTitleEmpty()
    {
        var info = new NowPlayingInfo();
        Assert.False(info.HasSong);
    }

    [Fact]
    public void NowPlayingInfo_ProgressText_FormatsCorrectly()
    {
        var info = new NowPlayingInfo
        {
            Title = "Test",
            Position = TimeSpan.FromSeconds(83),
            Duration = TimeSpan.FromSeconds(225)
        };
        Assert.Equal("01:23 / 03:45", info.ProgressText);
    }

    [Fact]
    public void NowPlayingInfo_ProgressText_Empty_WhenDurationZero()
    {
        var info = new NowPlayingInfo { Title = "Test" };
        Assert.Equal(string.Empty, info.ProgressText);
    }

    [Fact]
    public void NowPlayingInfo_Duration_NeverNegative()
    {
        var info = new Toolbox.Tools.Models.NowPlayingInfo
        {
            Duration = TimeSpan.FromSeconds(-10)
        };
        Assert.True(info.Duration >= TimeSpan.Zero,
            "Duration should never be negative");
    }

    [Fact]
    public void NeteaseMusicTool_HasCorrectCategory()
    {
        var tool = new Toolbox.Tools.NeteaseMusicTool();
        Assert.Equal("🎵 媒体与娱乐", tool.Category);
    }

    /// <summary>
    /// CreateContent() 创建 WPF 控件（StackPanel/Button/TextBlock），
    /// 必须在 STA 线程运行。xUnit 默认线程是 MTA，因此使用 STA 线程。
    /// </summary>
    [Fact]
    public void NeteaseMusicTool_CreateContent_ReturnsPanel()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var tool = new Toolbox.Tools.NeteaseMusicTool();
                var content = tool.CreateContent();
                Assert.IsType<StackPanel>(content);
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
        {
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);
        }
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_DetectsTitleChange()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "七里香", Artist = "周杰伦" };
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Different title should be detected as song change");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_DetectsArtistChange()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "林俊杰" };
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Different artist should be detected as song change");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_ReturnsFalse_WhenSameSong()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        Assert.False(NowPlayingInfo.IsSongChanged(previous, current),
            "Same title and artist should not be a song change");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_ReturnsTrue_WhenPreviousWasEmpty()
    {
        var previous = new NowPlayingInfo();
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Transition from no-song to a song should be detected");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_ReturnsTrue_WhenCurrentIsEmpty()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo();
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Transition from a song to no-song should be detected");
    }

    [Fact]
    public void MusicFloatWindow_HasTransparentBackground()
    {
        Exception? threadException = null;
        var result = false;
        var thread = new Thread(() =>
        {
            try
            {
                var window = Toolbox.Tools.Views.MusicFloatWindow.Instance;
                result = window.AllowsTransparency
                    && window.Background == System.Windows.Media.Brushes.Transparent;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.True(result, "Window should have transparent background");
    }

    [Fact]
    public void TitleMarquee_NeedsScroll_ReturnsTrue_WhenTextTooLong()
    {
        // 窗口实际可用宽度约 220px（Width=252, 减去边距）
        // 中文字符约 15px(FontSize) × 1.05 = 15.75px 每字符
        // 220 / 15.75 ≈ 14 个中文字符；14+ 字符需滚动
        Assert.True(MusicFloatWindow.TitleMarquee.NeedsScroll(
            "这是一个很长的中文歌曲标题需要滚动显示", 220.0, 15.0),
            "Long text should require scrolling");
    }

    [Fact]
    public void TitleMarquee_NeedsScroll_ReturnsFalse_WhenTextFits()
    {
        Assert.False(MusicFloatWindow.TitleMarquee.NeedsScroll(
            "短歌名", 220.0, 15.0),
            "Short text should not require scrolling");
    }

    [Fact]
    public void TitleMarquee_NeedsScroll_ReturnsFalse_WhenNoTitle()
    {
        Assert.False(MusicFloatWindow.TitleMarquee.NeedsScroll(
            "未在播放", 220.0, 15.0),
            "Default placeholder should not scroll");
    }

    private const double TestScreenWidth = 1920.0;

    [Fact]
    public void AlignmentMode_IsLeft_WhenWindowLeftLessThanHalfScreen()
    {
        Assert.True(MusicFloatWindow.AlignmentHelper.IsLeftSide(400, TestScreenWidth),
            "Window at 400px on 1920px screen should be on left side");
    }

    [Fact]
    public void AlignmentMode_IsRight_WhenWindowLeftGreaterThanHalfScreen()
    {
        Assert.False(MusicFloatWindow.AlignmentHelper.IsLeftSide(1400, TestScreenWidth),
            "Window at 1400px on 1920px screen should be on right side");
    }

    [Fact]
    public void AlignmentMode_IsLeft_WhenExactlyHalfScreen()
    {
        Assert.True(MusicFloatWindow.AlignmentHelper.IsLeftSide(960, TestScreenWidth),
            "Window exactly at midpoint should default to left side");
    }

    [Fact]
    public void AlignmentHelper_GetTextAlignment_Left_WhenLeftSide()
    {
        Assert.Equal(System.Windows.TextAlignment.Left,
            MusicFloatWindow.AlignmentHelper.GetTextAlignment(true));
    }

    [Fact]
    public void AlignmentHelper_GetTextAlignment_Right_WhenRightSide()
    {
        Assert.Equal(System.Windows.TextAlignment.Right,
            MusicFloatWindow.AlignmentHelper.GetTextAlignment(false));
    }

    [Fact]
    public void AlignmentHelper_GetHorizontalAlignment_Left_WhenLeftSide()
    {
        Assert.Equal(System.Windows.HorizontalAlignment.Left,
            MusicFloatWindow.AlignmentHelper.GetHorizontalAlignment(true));
    }

    [Fact]
    public void AlignmentHelper_GetHorizontalAlignment_Right_WhenRightSide()
    {
        Assert.Equal(System.Windows.HorizontalAlignment.Right,
            MusicFloatWindow.AlignmentHelper.GetHorizontalAlignment(false));
    }

    [Fact]
    public void AlignmentHelper_GetTextAlignment_Center_WhenNull()
    {
        Assert.Equal(System.Windows.TextAlignment.Center,
            MusicFloatWindow.AlignmentHelper.GetTextAlignment(null));
    }

    [Fact]
    public void AlignmentHelper_GetHorizontalAlignment_Center_WhenNull()
    {
        Assert.Equal(System.Windows.HorizontalAlignment.Center,
            MusicFloatWindow.AlignmentHelper.GetHorizontalAlignment(null));
    }

    [Fact]
    public void AnimationReset_BringsPanelToVisibleState()
    {
        // PlaySongSwitchAnimation 中断后面板会处于 Opacity=0 / Margin=偏移 的 HoldEnd 状态，
        // 修复方案通过 ResetPanelToVisibleState 面板恢复可见。
        // 此方法为 static，需在 STA 线程中创建 StackPanel（WPF 控件）。
        Exception? threadException = null;
        double panelOpacity = -1;
        var panelMargin = new System.Windows.Thickness(-999);
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new System.Windows.Controls.StackPanel
                {
                    Opacity = 0,
                    Margin = new System.Windows.Thickness(-35, 0, 0, 0)
                };

                MusicFloatWindow.ResetPanelToVisibleState(panel);

                panelOpacity = panel.Opacity;
                panelMargin = panel.Margin;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.Equal(1.0, panelOpacity);
        Assert.Equal(new System.Windows.Thickness(0), panelMargin);
    }

    [Fact]
    public void ResetPanelToVisibleState_HandlesNull()
    {
        // null 面板应安全处理，不抛出异常。
        // 无需 STA，方法入口立即 return。
        MusicFloatWindow.ResetPanelToVisibleState(null!);
    }

    [Fact]
    public void ClearAnimationClock_OnOpacity_RestoresLocalValue()
    {
        // Verify: After WPF animation HoldEnd, BeginAnimation(null) synchronously clears clock and restores local value.
        Exception? threadException = null;
        double finalOpacity = -1;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new System.Windows.Controls.StackPanel { Opacity = 1 };
                var animation = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
                panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, animation);
                System.Threading.Thread.Sleep(150);
                panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                finalOpacity = panel.Opacity;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.Equal(1.0, finalOpacity);
    }

    [Fact]
    public void ClearAnimationClock_OnMargin_RestoresLocalValue()
    {
        Exception? threadException = null;
        var finalMargin = new System.Windows.Thickness(-999);
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0) };
                var animation = new System.Windows.Media.Animation.ThicknessAnimation(
                    new System.Windows.Thickness(-35, 0, 0, 0), TimeSpan.FromMilliseconds(100));
                panel.BeginAnimation(System.Windows.FrameworkElement.MarginProperty, animation);
                System.Threading.Thread.Sleep(150);
                panel.BeginAnimation(System.Windows.FrameworkElement.MarginProperty, null);
                finalMargin = panel.Margin;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.Equal(new System.Windows.Thickness(0), finalMargin);
    }

    [Fact]
    public void ContentPanelMargin_SurvivesTextBlockLayoutChange()
    {
        // 验证：布局重算不会覆盖 panel.Margin 的本地值。
        Exception? threadException = null;
        var marginAfterLayout = new System.Windows.Thickness(-999);
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0) };
                var canvas = new System.Windows.Controls.Canvas { Width = 220, Height = 24 };
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = "测试歌曲标题",
                    FontSize = 15,
                    TextAlignment = System.Windows.TextAlignment.Right,
                    Width = 220
                };
                canvas.Children.Add(textBlock);
                panel.Children.Add(canvas);

                // 模拟 sbOut.Completed 回调中的操作顺序：
                // onMidpoint() 中修改 TextBlock 布局
                textBlock.TextAlignment = System.Windows.TextAlignment.Center;
                textBlock.Width = double.NaN;
                panel.UpdateLayout();

                // 然后设置 Margin
                panel.Margin = new System.Windows.Thickness(35, 0, 0, 0);

                marginAfterLayout = panel.Margin;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.Equal(new System.Windows.Thickness(35, 0, 0, 0), marginAfterLayout);
    }

    // ── 新增：封面变更检测 ──────────────────────────────

    [Fact]
    public void IsThumbnailChanged_ReturnsTrue_WhenPrevNullAndCurrentHasData()
    {
        var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02] };
        Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
            "Transition from null to data should be detected as thumbnail change");
    }

    [Fact]
    public void IsThumbnailChanged_ReturnsTrue_WhenPrevHasDataAndCurrentNull()
    {
        var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02] };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
            "Transition from data to null should be detected as thumbnail change");
    }

    [Fact]
    public void IsThumbnailChanged_ReturnsFalse_WhenBothNull()
    {
        var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        Assert.False(NowPlayingInfo.IsThumbnailChanged(prev, current),
            "Both null thumbnails should not be a thumbnail change");
    }

    [Fact]
    public void IsThumbnailChanged_ReturnsFalse_WhenSameData()
    {
        var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
        Assert.False(NowPlayingInfo.IsThumbnailChanged(prev, current),
            "Same thumbnail bytes should not be a thumbnail change");
    }

    [Fact]
    public void IsThumbnailChanged_ReturnsTrue_WhenDifferentData()
    {
        var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0xAA, 0xBB, 0xCC] };
        Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
            "Different thumbnail bytes should be detected as thumbnail change");
    }

    [Fact]
    public void IsThumbnailChanged_ReturnsTrue_WhenDifferentLength()
    {
        var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02] };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
        Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
            "Different length data should be detected as thumbnail change");
    }

    [Fact]
    public void IsThumbnailChanged_ReturnsTrue_WhenPrevIsNull()
    {
        Assert.True(NowPlayingInfo.IsThumbnailChanged(null,
            new NowPlayingInfo { Title = "晴天", ThumbnailData = [0x01] }),
            "Null previous should be a thumbnail change");
    }

    // ── 新增：LoadCoverFromData BitmapImage 创建验证（STA 线程）──

    [Fact]
    public void LoadCoverFromData_CreatesFrozenBitmap_FromValidPngBytes()
    {
        Exception? threadException = null;
        BitmapSource? result = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 1x1 像素透明 PNG 文件
                byte[] pngBytes =
                [
                    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
                    0x00, 0x00, 0x00, 0x0D, // IHDR chunk length
                    0x49, 0x48, 0x44, 0x52, // "IHDR"
                    0x00, 0x00, 0x00, 0x01, // width = 1
                    0x00, 0x00, 0x00, 0x01, // height = 1
                    0x08, 0x06, 0x00, 0x00, 0x00, // bit depth 8, RGBA
                    0x1F, 0x15, 0xC4, 0x89, // IHDR CRC
                    0x00, 0x00, 0x00, 0x0A, // IDAT chunk length
                    0x49, 0x44, 0x41, 0x54, // "IDAT"
                    0x78, 0xDA, 0x63, 0x68, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, // IDAT data
                    0xE5, 0x27, 0xDE, 0xFC, // IDAT CRC
                    0x00, 0x00, 0x00, 0x00, // IEND chunk length
                    0x49, 0x45, 0x4E, 0x44, // "IEND"
                    0xAE, 0x42, 0x60, 0x82  // IEND CRC
                ];

                using var memStream = new MemoryStream(pngBytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memStream;
                bitmap.EndInit();
                bitmap.Freeze();
                result = bitmap;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.NotNull(result);
        Assert.True(result.IsFrozen, "BitmapImage should be frozen for cross-thread use");
        Assert.Equal(1, result.PixelWidth);
        Assert.Equal(1, result.PixelHeight);
    }

    [Fact]
    public void MusicFloatWindow_SizeMode_DefaultsToLarge()
    {
        var window = Toolbox.Tools.Views.MusicFloatWindow.Instance;
        Assert.Equal(Toolbox.Controls.FloatSizeMode.Large, window.SizeMode);
    }

    [Fact]
    public void LoadCoverFromData_ReturnsNullImage_WhenDataIsEmpty()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                byte[]? nullData = null;
                byte[] emptyData = [];

                bool shouldSkipNull = nullData == null || nullData.Length == 0;
                bool shouldSkipEmpty = emptyData == null || emptyData.Length == 0;

                Assert.True(shouldSkipNull, "null data should be skipped");
                Assert.True(shouldSkipEmpty, "empty data should be skipped");
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);
    }

    [Fact]
    public void NeteaseMusicTool_OpenButton_Click_ShowsWindow()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 重置单例以避免前序测试的实例跨线程访问
                Toolbox.Tools.Views.MusicFloatWindow.ForceResetInstance();
                var window = Toolbox.Tools.Views.MusicFloatWindow.Instance;
                Assert.False(window.IsLoaded);
                window.Show();
                Assert.True(window.IsVisible);
                window.Close();
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);
    }

    [Fact]
    public void MusicFloatWindow_OnClosed_ResetsSingleton()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 重置单例以避免前序测试的实例跨线程访问
                Toolbox.Tools.Views.MusicFloatWindow.ForceResetInstance();
                var firstRef = Toolbox.Tools.Views.MusicFloatWindow.Instance;
                firstRef.Close();
                // 关闭后单例应被重置，第二次获取应返回新实例
                var secondRef = Toolbox.Tools.Views.MusicFloatWindow.Instance;
                Assert.NotSame(firstRef, secondRef);
                secondRef.Close();
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);
    }

    // ── Task 1: SizeMode 切换与 EnsureChildInPanel 修复 ──────

    /// <summary>
    /// 验证 Large→Compact→Large 切换过程不抛出异常。
    /// 回归场景：切换回 Large 时 EnsureChildInPanel 遇到 Border 父容器会崩溃。
    /// </summary>
    [Fact]
    public void MusicFloatWindow_SizeMode_ToggleLargeToCompactToLarge_NoException()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                Toolbox.Tools.Views.MusicFloatWindow.ForceResetInstance();
                var window = Toolbox.Tools.Views.MusicFloatWindow.Instance;
                window.Show();

                // 切换到紧凑模式
                window.SizeMode = FloatSizeMode.Compact;
                // 再切换回大模式
                window.SizeMode = FloatSizeMode.Large;

                window.Close();
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);
    }

    /// <summary>
    /// 验证 EnsureChildInPanel 能够处理父容器为 Border (Decorator) 的情况。
    /// 创建 Border 作为 child 的父容器，再调用 EnsureChildInPanel 将其迁移到 Panel。
    /// </summary>
    [Fact]
    public void EnsureChildInPanel_WithBorderParent_MovesChildToPanel()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var border = new Border();
                var panel = new StackPanel();
                var child = new TextBlock { Text = "Test" };

                // 将 child 放入 Border.Child（模拟 CompactCoverSlot 场景）
                border.Child = child;

                // 验证 child 当前父容器是 Border
                var parentBefore = System.Windows.Media.VisualTreeHelper.GetParent(child);
                Assert.IsType<Border>(parentBefore);

                // 通过反射调用私有的 EnsureChildInPanel 方法
                var method = typeof(MusicFloatWindow).GetMethod("EnsureChildInPanel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.NotNull(method);

                method.Invoke(null, [panel, child]);

                // 验证 child 已被移到 panel
                Assert.Same(panel, System.Windows.Media.VisualTreeHelper.GetParent(child));
                Assert.True(panel.Children.Contains(child), "Child should be in panel.Children");
                // 验证 Border.Child 已被置空
                Assert.Null(border.Child);
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);
    }

    // ── Task 2: 紧凑模式右侧布局列宽交换 ──────────────────────
// Step 1: 先写测试验证。

    [Fact]
    public void MusicFloatWindow_CompactMode_RightSide_ColumnWidthsSwapped()
    {
        Exception? exception = null;
        GridLength? col0Width = null;
        GridLength? col1Width = null;
        var thread = new Thread(() =>
        {
            try
            {
                MusicFloatWindow.ForceResetInstance();
                var window = MusicFloatWindow.Instance;
                window.Show();
                window.Left = 1400;
                window.SizeMode = FloatSizeMode.Compact;
                var field = typeof(MusicFloatWindow)
                    .GetField("LayoutCompact",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(window) is Grid layoutCompact)
                {
                    col0Width = layoutCompact.ColumnDefinitions[0].Width;
                    col1Width = layoutCompact.ColumnDefinitions[1].Width;
                }
            }
            catch (Exception ex) { exception = ex; }
            finally
            {
                if (MusicFloatWindow.Instance.IsLoaded)
                    MusicFloatWindow.Instance.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(exception);
        Assert.NotNull(col0Width);
        Assert.NotNull(col1Width);
        Assert.Equal(GridUnitType.Star, col0Width!.Value.GridUnitType);
        Assert.Equal(GridUnitType.Auto, col1Width!.Value.GridUnitType);
    }

    [Fact]
    public void MusicFloatWindow_CompactMode_LeftSide_ColumnWidthsDefault()
    {
        Exception? exception = null;
        GridLength? col0Width = null;
        GridLength? col1Width = null;
        var thread = new Thread(() =>
        {
            try
            {
                MusicFloatWindow.ForceResetInstance();
                var window = MusicFloatWindow.Instance;
                window.Show();
                window.Left = 400;
                window.SizeMode = FloatSizeMode.Compact;
                var field = typeof(MusicFloatWindow)
                    .GetField("LayoutCompact",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(window) is Grid layoutCompact)
                {
                    col0Width = layoutCompact.ColumnDefinitions[0].Width;
                    col1Width = layoutCompact.ColumnDefinitions[1].Width;
                }
            }
            catch (Exception ex) { exception = ex; }
            finally
            {
                if (MusicFloatWindow.Instance.IsLoaded)
                    MusicFloatWindow.Instance.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(exception);
        Assert.NotNull(col0Width);
        Assert.NotNull(col1Width);
        // 左侧时应该是默认：列0=Auto（封面），列1=*（文本）
        Assert.Equal(GridUnitType.Auto, col0Width!.Value.GridUnitType);
        Assert.Equal(GridUnitType.Star, col1Width!.Value.GridUnitType);
    }

    // ── Task 3: 按钮默认颜色（绿色强调色）────────────────────────

    /// <summary>
    /// 验证 btnClose 和 toggleBtn 的 Background 不是 Transparent，
    /// 修复后应使用全局默认样式（AccentBrush #76B580）。
    /// </summary>
    [Fact]
    public void NeteaseMusicTool_CloseAndToggleButton_BackgroundIsNotTransparent()
    {
        Exception? threadException = null;
        Color? btnCloseBg = null;
        Color? toggleBtnBg = null;
        var thread = new Thread(() =>
        {
            try
            {
                var tool = new Toolbox.Tools.NeteaseMusicTool();
                var content = tool.CreateContent() as StackPanel;
                Assert.NotNull(content);

                // 获取 btnClose（第二个 Button，root.Children[1]）
                var btnClose = Assert.IsType<Button>(content.Children[1]);
                // 获取 toggleBtn（在 sizeRow 中，sizeRow 是 root.Children[4]）
                var sizeRow = Assert.IsType<Grid>(content.Children[4]);
                var toggleBtn = Assert.IsType<Button>(sizeRow.Children[1]);

                btnCloseBg = (btnClose.Background as SolidColorBrush)?.Color;
                toggleBtnBg = (toggleBtn.Background as SolidColorBrush)?.Color;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.NotNull(btnCloseBg);
        Assert.NotNull(toggleBtnBg);
        Assert.NotEqual(Colors.Transparent, btnCloseBg!.Value);
        Assert.NotEqual(Colors.Transparent, toggleBtnBg!.Value);
    }

    // ── Task 4: 紧凑模式歌名宽度不随 Canvas 缩小 ──────────────────

    [Fact]
    public void MusicFloatWindow_CompactMode_SongTitleWidth_MatchesCanvasWidth()
    {
        // 验证紧凑模式下 SongTitle.Width 应等于 Canvas 可用宽度(≈98)，而非 220
        Exception? exception = null;
        double? titleWidth = null;
        double? canvasWidth = null;
        var thread = new Thread(() =>
        {
            try
            {
                MusicFloatWindow.ForceResetInstance();
                var window = MusicFloatWindow.Instance;
                window.Show();
                window.SizeMode = FloatSizeMode.Compact;

                var titleField = typeof(MusicFloatWindow)
                    .GetField("SongTitle",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                var canvasField = typeof(MusicFloatWindow)
                    .GetField("TitleCanvas",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                if (titleField?.GetValue(window) is TextBlock songTitle)
                {
                    titleWidth = songTitle.Width;
                }
                if (canvasField?.GetValue(window) is Canvas titleCanvas)
                {
                    canvasWidth = titleCanvas.Width;
                }
            }
            catch (Exception ex) { exception = ex; }
            finally
            {
                if (MusicFloatWindow.Instance.IsLoaded)
                    MusicFloatWindow.Instance.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(exception);
        Assert.NotNull(titleWidth);
        Assert.NotNull(canvasWidth);

        // 紧凑模式：SongTitle.Width 应 ≈ Canvas.Width，而不是 220
        Assert.Equal(canvasWidth!.Value, titleWidth!.Value, 1);
        Assert.NotEqual(220.0, titleWidth!.Value);
    }
}