using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 重启资源管理器 —— 当任务栏或桌面卡死时一键结束并重启 explorer.exe
/// </summary>
public class RestartExplorerTool : ITool
{
    // 与全局主题（App.xaml）及其它工具一致的配色常量
    private static readonly Color BgDark = Color.FromRgb(0x2D, 0x2D, 0x2D);
    private static readonly Color Success = Color.FromRgb(0x63, 0xD4, 0x7E);
    private static readonly Color Danger = Color.FromRgb(0xF0, 0x70, 0x70);
    private static readonly Color Warning = Color.FromRgb(0xE0, 0xA0, 0x30);

    public string Name => "重启资源管理器";
    public string Description => "当任务栏或桌面卡死时，一键结束并重启 explorer.exe 进程。";
    public string Category => Toolbox.Models.ToolCategory.System;
    public string IconGlyph => "🔄";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 结果反馈（固定在底部）
        var resultBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };

        // 警告文字
        var warning = new TextBlock
        {
            Text = "⚠️ 此操作会关闭所有文件资源管理器窗口，请先保存工作。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Warning),
            Margin = new Thickness(0, 0, 0, 12)
        };

        // 重启按钮
        var restartButton = new Button
        {
            Content = "🔄 重启资源管理器",
            FontSize = 14,
            Padding = new Thickness(12),
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        restartButton.Click += (_, _) =>
        {
            try
            {
                // Step 1: 结束 explorer.exe
                var killProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/f /im explorer.exe",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                killProc?.WaitForExit();

                // Step 2: 等待 500ms 确保进程已退出
                System.Threading.Thread.Sleep(500);

                // Step 3: 重新启动 explorer
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });

                resultBlock.Text = "✅ 资源管理器已重启";
                resultBlock.Foreground = new SolidColorBrush(Success);
            }
            catch (Exception ex)
            {
                resultBlock.Text = $"❌ 操作失败：{ex.Message}";
                resultBlock.Foreground = new SolidColorBrush(Danger);
            }
        };

        // 卡片：警告 + 按钮
        var inner = new StackPanel();
        inner.Children.Add(warning);
        inner.Children.Add(restartButton);

        var card = new Border
        {
            Background = new SolidColorBrush(BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = inner
        };
        GlowCardMarker.SetIsGlowCard(card, true);

        panel.Children.Add(card);
        panel.Children.Add(resultBlock);

        return panel;
    }
}
