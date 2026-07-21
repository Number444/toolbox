using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 定时关机工具 —— 合并了取消关机功能
/// </summary>
public class ShutdownTool : ITool
{
    // 与全局主题（App.xaml）及其它工具（JunkCleanerTool 等）一致的画刷配色
    private static readonly Color BgDark = Color.FromRgb(0x2D, 0x2D, 0x2D);
    private static readonly Color TextPrimary = Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly Color TextSecondary = Color.FromRgb(0x80, 0x80, 0x80);
    private static readonly Color Success = Color.FromRgb(0x63, 0xD4, 0x7E);
    private static readonly Color Danger = Color.FromRgb(0xF0, 0x70, 0x70);

    public string Name => "定时关机";
    public string Description => "设置指定时间后自动关机，或取消已计划的关机任务";
    public string Category => Toolbox.Models.ToolCategory.System;
    public string IconGlyph => "⏰";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // ====== 说明文字 ======
        var desc = new TextBlock
        {
            Text = "选择预设时长快速关机，或输入自定义分钟数。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 0, 0, 16)
        };

        // ====== 结果反馈（固定在底部） ======
        var resultBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var minuteInput = new TextBox
        {
            Width = 100,
            Height = 32,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        // ====== 快捷关机卡片（2x3 网格，最常用放最上） ======
        var quickGrid = new UniformGrid
        {
            Rows = 2,
            Columns = 3
        };

        var quickOptions = new (string text, int minutes)[]
        {
            ("1 分钟", 1),
            ("5 分钟", 5),
            ("10 分钟", 10),
            ("30 分钟", 30),
            ("1 小时", 60),
            ("2 小时", 120),
        };

        foreach (var (text, minutes) in quickOptions)
        {
            var btn = new Button
            {
                Content = text,
                Height = 42,
                FontSize = 14,
                Margin = new Thickness(3)
            };

            btn.Click += (_, _) =>
            {
                minuteInput.Text = minutes.ToString();
                int seconds = minutes * 60;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = $"/s /t {seconds} /c \"定时关机：{minutes} 分钟后\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
                resultBlock.Text = $"✅ 已设置 {minutes} 分钟后关机";
                resultBlock.Foreground = new SolidColorBrush(Success);
            };

            quickGrid.Children.Add(btn);
        }

        var quickCard = BuildCard("快捷关机", quickGrid);
        quickCard.Margin = new Thickness(0, 0, 0, 12);

        // ====== 自定义时长卡片（输入 + 设置 + 取消同一行） ======
        var inputRow = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        var label = new TextBlock
        {
            Text = "分钟数：",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Foreground = new SolidColorBrush(TextPrimary),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var shutdownButton = new Button
        {
            Content = "🔌 设置定时关机",
            FontSize = 14,
            Padding = new Thickness(10),
            Margin = new Thickness(8, 0, 0, 0)
        };

        shutdownButton.Click += (_, _) =>
        {
            if (int.TryParse(minuteInput.Text, out int minutes) && minutes > 0)
            {
                int seconds = minutes * 60;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = $"/s /t {seconds} /c \"定时关机：{minutes} 分钟后\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
                resultBlock.Text = $"✅ 已设置 {minutes} 分钟后关机";
                resultBlock.Foreground = new SolidColorBrush(Success);
            }
            else
            {
                resultBlock.Text = "⚠️ 请输入有效的正整数";
                resultBlock.Foreground = new SolidColorBrush(Danger);
            }
        };

        // 取消按钮（危险色，与全局 DangerBrush 一致）
        var cancelButton = new Button
        {
            Content = "🛑 取消定时关机",
            FontSize = 14,
            Padding = new Thickness(10),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Danger),
            Foreground = Brushes.White
        };

        cancelButton.Click += (_, _) =>
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/a",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });

                if (proc == null)
                {
                    resultBlock.Text = "❌ 无法启动 shutdown 进程";
                    resultBlock.Foreground = new SolidColorBrush(Danger);
                    return;
                }

                proc.WaitForExit(1000);

                if (proc.ExitCode == 0)
                {
                    resultBlock.Text = "✅ 已取消所有定时关机任务";
                    resultBlock.Foreground = new SolidColorBrush(Success);
                }
                else
                {
                    resultBlock.Text = "⚠️ 没有找到可取消的定时关机任务";
                    resultBlock.Foreground = new SolidColorBrush(Danger);
                }
            }
            catch (Exception ex)
            {
                resultBlock.Text = $"❌ 操作失败：{ex.Message}";
                resultBlock.Foreground = new SolidColorBrush(Danger);
            }
        };

        inputRow.Children.Add(label);
        inputRow.Children.Add(minuteInput);
        inputRow.Children.Add(shutdownButton);
        inputRow.Children.Add(cancelButton);

        var customCard = BuildCard("自定义时长", inputRow);

        panel.Children.Add(desc);
        panel.Children.Add(quickCard);
        panel.Children.Add(customCard);
        panel.Children.Add(resultBlock);

        return panel;
    }

    /// <summary>构建分组卡片：深灰圆角容器 + 组标题 + 内容（与 C盘清理/二维码工具的卡片风格一致）</summary>
    private static Border BuildCard(string title, UIElement content)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TextPrimary),
            Margin = new Thickness(0, 0, 0, 10)
        });
        inner.Children.Add(content);

        return new Border
        {
            Background = new SolidColorBrush(BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = inner
        };
    }
}
