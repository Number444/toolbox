using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Toolbox.Core.Controls;

/// <summary>
/// 深色圆角主题菜单窗口（代码构建，无 XAML）。
/// 用于系统托盘右键菜单、音乐悬浮窗右键菜单，
/// 视觉与主窗口深色圆角风格（#2D2D2D）一致。
/// 点击菜单外部（窗口失焦）或按 Esc 自动关闭。
/// </summary>
public sealed class ThemedMenuWindow : Window
{
    /// <summary>菜单项。IsSeparator 为 true 时其余字段忽略。</summary>
    public sealed class Item
    {
        public string Text { get; init; } = "";
        public bool IsChecked { get; init; }
        public bool IsEnabled { get; init; } = true;
        public bool IsSeparator { get; init; }
        public Action? Action { get; init; }

        public static Item Separator() => new() { IsSeparator = true };
    }

    /// <summary>关闭流程已开始标记——防止 Deactivated 在 Close 期间重入调用 Close。</summary>
    private bool _closeInitiated;

    /// <summary>卡片四周留给投影的透明边距（px）。
    /// 投影需要外扩空间，否则会被窗口方形边界切成圆角外的半透明尖角。</summary>
    private const double ShadowMargin = 16;

    /// <summary>统一关闭入口（带重入守卫）。</summary>
    private void InitiateClose()
    {
        if (_closeInitiated) return;
        _closeInitiated = true;
        Close();
    }

    private ThemedMenuWindow(IEnumerable<Item> items)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel();
        foreach (var item in items)
            panel.Children.Add(item.IsSeparator ? BuildSeparator() : BuildRow(item));

        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x2D, 0x2D, 0x2D)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            // 透明边距：给投影留出外扩空间，避免被窗口边界切成尖角
            Margin = new Thickness(ShadowMargin),
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.45,
                Color = Colors.Black
            },
            Child = panel
        };

        // 点击菜单外部 → 窗口失焦 → 自动收回
        // （Close 过程中也会触发 Deactivated，必须守卫重入，否则 VerifyNotClosing 崩溃）
        Deactivated += (_, _) => InitiateClose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) InitiateClose(); };

        // 透明边距（投影区域）上的点击视为"点击外部"。
        // 菜单项在 MouseLeftButtonDown 标记 Handled，不会冒泡到这里
        MouseLeftButtonDown += (_, _) => InitiateClose();
        MouseRightButtonDown += (_, _) => InitiateClose();
    }

    private static Border BuildSeparator()
    {
        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(6, 4, 6, 4)
        };
        // 吞掉 Down，避免冒泡到窗口被当作"点击外部"关闭菜单
        separator.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return separator;
    }

    private Border BuildRow(Item item)
    {
        var grid = new Grid { MinWidth = 150 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new TextBlock
        {
            Text = item.IsChecked ? "✓" : "",
            Width = 18,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0xD4, 0x7E)),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(check);

        var label = new TextBlock
        {
            Text = item.Text,
            FontSize = 13,
            Foreground = new SolidColorBrush(item.IsEnabled
                ? Color.FromRgb(0xF0, 0xF0, 0xF0)
                : Color.FromRgb(0x80, 0x80, 0x80)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var row = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 16, 6),
            Cursor = item.IsEnabled ? Cursors.Hand : Cursors.Arrow,
            Child = grid
        };

        // 吞掉 Down，避免冒泡到窗口被当作"点击外部"关闭菜单
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;

        if (item.IsEnabled)
        {
            var hoverBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            row.MouseEnter += (_, _) => row.Background = hoverBrush;
            row.MouseLeave += (_, _) => row.Background = null;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                InitiateClose();
                // 菜单关闭后再执行动作，避免动作里的窗口切换干扰 Close
                Dispatcher.BeginInvoke(new Action(() => item.Action?.Invoke()));
            };
        }

        return row;
    }

    /// <summary>
    /// 在指定屏幕坐标（DIP）弹出菜单，卡片左上角对齐光标，
    /// 自动夹紧到主屏工作区内（光标靠近屏幕底边时向上翻）。
    /// </summary>
    public static void ShowAt(Point screenPosDip, IEnumerable<Item> items)
    {
        var menu = new ThemedMenuWindow(items)
        {
            // 窗口含透明边距，向左上偏移使卡片（而非窗口）对齐光标
            Left = screenPosDip.X - ShadowMargin,
            Top = screenPosDip.Y - ShadowMargin
        };
        menu.Show();
        menu.Activate(); // 必须激活，后续失焦（点击外部）才能触发 Deactivated 收回

        menu.Dispatcher.BeginInvoke(new Action(() =>
        {
            var wa = SystemParameters.WorkArea;
            double cardWidth = menu.ActualWidth - ShadowMargin * 2;
            double cardHeight = menu.ActualHeight - ShadowMargin * 2;

            if (menu.Left + ShadowMargin + cardWidth > wa.Right)
                menu.Left = wa.Right - cardWidth - ShadowMargin;
            if (menu.Top + ShadowMargin + cardHeight > wa.Bottom)
                menu.Top = screenPosDip.Y - cardHeight - ShadowMargin; // 卡片底边贴光标上方
            if (menu.Left + ShadowMargin < wa.Left)
                menu.Left = wa.Left - ShadowMargin;
            if (menu.Top + ShadowMargin < wa.Top)
                menu.Top = wa.Top - ShadowMargin;
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
