using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Toolbox.Core.Controls;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 自绘确认弹窗（无边框深色风格，与全局主题一致；风格照抄 JunkCleanerTool 内的私有确认弹窗）。
/// 作为 Plugins 层的共享类，供需要"操作前确认"的工具复用（如密码生成器删除/清空历史记录）。
/// 用法：new ConfirmDialog(message, title, confirmText).ShowDialog() 后读取 Confirmed。
/// warningText：可选警示行（如"⚠️ 回收站清空后不可恢复！"），显示在正文下方、按钮上方。
/// 开/关动画：PopupAnimator（dsh-app 菜单同款抛出回弹 + 模糊渐清），2026-08-19 起统一挂载。
/// </summary>
public sealed class ConfirmDialog : Window
{
    /// <summary>用户是否点了确认按钮（取消 / 关闭 / Esc 均为 false）</summary>
    public bool Confirmed { get; private set; }

    /// <summary>开/关动画抛出起点（dsh-app 菜单同款：上方 24px 抛出落位）。</summary>
    private static readonly Point FlyFrom = new(0, -24);

    /// <summary>动画安全区（dsh-app AnimSafePad 同款）：抛出位移/过冲超出窗口客户区的部分会被
    /// AllowsTransparency 分层窗口裁切，四周留 40px 透明余量（视觉不可见）。</summary>
    private const double AnimSafePad = 40;

    private readonly Border _mainBorder;
    /// <summary>关闭动画播完置位后真正关窗（OnClosing 首次拦截播倒放动画）。</summary>
    private bool _allowClose;

    public ConfirmDialog(string message, string title,
        string confirmText = "确定", string cancelText = "取消", string? warningText = null)
    {
        Title = title;
        Width = 480; // 卡片 400 + 两侧动画安全区 40×2
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        var darkBg = ThemeColors.BgDark;
        var textPrimary = ThemeColors.TextPrimary;
        var textSecondary = Color.FromRgb(0xC0, 0xC0, 0xC0); // 弹窗正文专用：比全局次要文本更亮，保证小字号可读
        var borderColor = ThemeColors.BorderSubtle;

        var mainBorder = new Border
        {
            Background = new SolidColorBrush(darkBg),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(AnimSafePad),
        };
        _mainBorder = mainBorder;

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // 标题
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(textPrimary),
            Margin = new Thickness(0, 0, 0, 14)
        });

        // 正文
        root.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(textSecondary),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, warningText is null ? 22 : 10)
        });

        // 警示行（可选，如回收站不可恢复警告；与 JunkCleanerTool 原私有副本同款样式）
        if (warningText is not null)
        {
            root.Children.Add(new TextBlock
            {
                Text = warningText,
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.Warning),
                Margin = new Thickness(0, 0, 0, 18)
            });
        }

        // 按钮行
        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelBtn = new Button
        {
            Content = cancelText,
            Width = 80,
            Height = 32,
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            Foreground = new SolidColorBrush(textPrimary),
            BorderBrush = new SolidColorBrush(borderColor),
            Margin = new Thickness(0, 0, 10, 0)
        };
        cancelBtn.Click += (_, _) => { Confirmed = false; Close(); };

        var confirmBtn = new Button
        {
            Content = confirmText,
            Width = 90,
            Height = 32,
            FontSize = 13,
            Background = new SolidColorBrush(ThemeColors.DangerButton),
            Foreground = new SolidColorBrush(textPrimary),
            BorderThickness = new Thickness(0)
        };
        confirmBtn.Click += (_, _) => { Confirmed = true; Close(); };

        buttonBar.Children.Add(cancelBtn);
        buttonBar.Children.Add(confirmBtn);
        root.Children.Add(buttonBar);

        mainBorder.Child = root;
        Content = mainBorder;

        // 打开动画（dsh-app 菜单同款：抛出回弹 + 放大 + 模糊渐清）。
        // Loaded 再开播：构造内开播时钟会先行消耗几十毫秒，窗口首帧看不到抛出起点；Loaded 开播首帧即起点不闪帧
        Loaded += (_, _) => PopupAnimator.PlayOpen(mainBorder, FlyFrom);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            { Confirmed = false; Close(); }
        };
    }

    /// <summary>
    /// 关闭先播倒放动画（打开的严格时间反转）：首次 Closing 取消关窗、播收拢动画，
    /// 完成后回调里置 _allowClose 真正关窗。系统关闭"菜单动画"时 PlayClose 立即回调 = 无动画直接关。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // Dispatcher.HasShutdownStarted：应用退出正在关窗时必须放行，否则动画拦截会中止 Shutdown
        if (!_allowClose && IsLoaded
            && !Application.Current!.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            PopupAnimator.PlayClose(_mainBorder, () =>
            {
                _allowClose = true;
                Close();
            }, null, FlyFrom);
        }
        base.OnClosing(e);
    }
}
