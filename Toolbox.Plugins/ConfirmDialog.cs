using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Toolbox.Tools;

/// <summary>
/// 自绘确认弹窗（无边框深色风格，与全局主题一致；风格照抄 JunkCleanerTool 内的私有确认弹窗）。
/// 作为 Plugins 层的共享类，供需要"操作前确认"的工具复用（如密码生成器删除/清空历史记录）。
/// 用法：new ConfirmDialog(message, title, confirmText).ShowDialog() 后读取 Confirmed。
/// </summary>
public sealed class ConfirmDialog : Window
{
    /// <summary>用户是否点了确认按钮（取消 / 关闭 / Esc 均为 false）</summary>
    public bool Confirmed { get; private set; }

    public ConfirmDialog(string message, string title, string confirmText = "确定")
    {
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        var darkBg = Color.FromRgb(0x2D, 0x2D, 0x2D);
        var textPrimary = Color.FromRgb(0xF0, 0xF0, 0xF0);
        var textSecondary = Color.FromRgb(0xC0, 0xC0, 0xC0);
        var borderColor = Color.FromRgb(0x45, 0x45, 0x45);

        var mainBorder = new Border
        {
            Background = new SolidColorBrush(darkBg),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

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
            Margin = new Thickness(0, 0, 0, 22)
        });

        // 按钮行
        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelBtn = new Button
        {
            Content = "取消",
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
            Background = new SolidColorBrush(Color.FromRgb(0xD0, 0x40, 0x40)),
            Foreground = new SolidColorBrush(textPrimary),
            BorderThickness = new Thickness(0)
        };
        confirmBtn.Click += (_, _) => { Confirmed = true; Close(); };

        buttonBar.Children.Add(cancelBtn);
        buttonBar.Children.Add(confirmBtn);
        root.Children.Add(buttonBar);

        mainBorder.Child = root;
        Content = mainBorder;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            { Confirmed = false; Close(); }
        };
    }
}
