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

    /// <summary>卡片四周的透明边距（px）。双重用途：① 投影外扩空间（否则被窗口方形边界切成
    /// 圆角外的半透明尖角）；② PopupAnimator 抛出位移/过冲的动画安全区（24px 抛出 + 2px 过冲 < 40）。
    /// 边距透明不可见，其上的点击视为"点击外部"关闭菜单。</summary>
    private const double SafeMargin = 40;

    /// <summary>动画承载层（最外层 Border）：PopupAnimator 的 BlurEffect/变换挂它身上。</summary>
    private Border? _animHost;
    /// <summary>打开动画抛出起点（按菜单相对光标的展开方向取 ±24），关闭倒放复用同一方向。</summary>
    private Point _flyFrom = new(0, -24);
    /// <summary>关闭动画播完置位后真正关窗（OnClosing 首次拦截播倒放动画）。</summary>
    private bool _allowClose;

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

        // 三层结构（dsh-app AppMenuPanel 同款分层）：
        // 最内层 = 视觉卡片（背景/描边/圆角）；中层 = 静态阴影（Effect 同一元素只能挂一个，
        // 阴影与 PopupAnimator 动画模糊必须分层）；最外层 = 动画承载 + 透明安全区。
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x2D, 0x2D, 0x2D)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = panel
        };
        var shadowLayer = new Border
        {
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.45,
                Color = Colors.Black
            },
            Child = card
        };
        var animHost = new Border
        {
            Margin = new Thickness(SafeMargin),
            Child = shadowLayer
        };
        Content = animHost;
        _animHost = animHost;

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
            Left = screenPosDip.X - SafeMargin,
            Top = screenPosDip.Y - SafeMargin
        };
        // Loaded 必须在 Show() 之前挂——AllowsTransparency 分层窗口的时序事实（2026-08-19 实测三连）：
        // ① Show() 内部同步触发 Loaded；② Show() 内部同步合成并呈现首帧（不等调度器），
        //    Show() 返回后再设起始态 = 最终态已呈现一帧（"瞬间出现→消失→再播动画"的闪帧）；
        // ③ Loaded 在同步呈现之前触发，此处设起始态 + 夹紧定位，首帧即动画起点。
        // Loaded 触发时布局已完成，ActualWidth/ActualHeight 可用。
        menu.Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            double cardWidth = menu.ActualWidth - SafeMargin * 2;
            double cardHeight = menu.ActualHeight - SafeMargin * 2;

            if (menu.Left + SafeMargin + cardWidth > wa.Right)
                menu.Left = wa.Right - cardWidth - SafeMargin;
            if (menu.Top + SafeMargin + cardHeight > wa.Bottom)
            {
                menu.Top = screenPosDip.Y - cardHeight - SafeMargin; // 卡片底边贴光标上方
                menu._flyFrom = new Point(0, 24); // 上翻展开：从下方抛出
            }
            if (menu.Left + SafeMargin < wa.Left)
                menu.Left = wa.Left - SafeMargin;
            if (menu.Top + SafeMargin < wa.Top)
                menu.Top = wa.Top - SafeMargin;

            // 打开动画（dsh-app 菜单同款：抛出回弹 + 放大 + 模糊渐清）
            PopupAnimator.PlayOpen(menu._animHost!, menu._flyFrom);
        };

        menu.Show();
        menu.Activate(); // 必须激活，后续失焦（点击外部）才能触发 Deactivated 收回
    }

    /// <summary>
    /// 关闭先播倒放动画（打开的严格时间反转，沿打开时的抛出方向飞回）：首次 Closing 取消关窗、
    /// 播收拢动画，完成后回调里置 _allowClose 真正关窗。系统关闭"菜单动画"时 PlayClose 立即回调 = 直接关。
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Dispatcher.HasShutdownStarted：应用退出（如托盘菜单"退出"动作）正在关窗时必须放行，
        // 否则关窗动画拦截会中止整个 Shutdown
        if (!_allowClose && IsLoaded && _animHost is not null
            && !Application.Current!.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            PopupAnimator.PlayClose(_animHost, () =>
            {
                _allowClose = true;
                Close();
            }, null, _flyFrom);
        }
        base.OnClosing(e);
    }
}
