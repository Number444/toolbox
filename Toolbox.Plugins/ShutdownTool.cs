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
    public string Name => "定时关机";
    public string Description => "设置指定时间后自动关机，或取消已计划的关机任务";
    public string Category => Toolbox.Models.ToolCategory.System;
    public string IconGlyph => "⏰";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // ====== 定时关机区域 ======

        var desc = new TextBlock
        {
            Text = "选择预设时长快速关机，或输入自定义分钟数。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 20)
        };

        var resultBlock = new TextBlock
        {
            Text = "",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20)),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var minuteInput = new TextBox
        {
            Width = 100,
            Height = 32,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        // ====== 快捷关机按钮（2x3 网格） ======
        var quickLabel = new TextBlock
        {
            Text = "快捷关机",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var quickGrid = new UniformGrid
        {
            Rows = 2,
            Columns = 3,
            Margin = new Thickness(0, 0, 0, 20)
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
                resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
            };

            quickGrid.Children.Add(btn);
        }

        // ====== 输入 + 设置按钮 + 取消按钮（放在同一行） ======
        var inputRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 24)
        };

        var label = new TextBlock
        {
            Text = "分钟数：",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
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
                resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
            }
            else
            {
                resultBlock.Text = "⚠️ 请输入有效的正整数";
                resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
            }
        };

        // 红色取消按钮（放在设置按钮右侧）
        var cancelButton = new Button
        {
            Content = "🛑 取消定时关机",
            FontSize = 14,
            Padding = new Thickness(10),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40)),
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
                    resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
                    return;
                }

                proc.WaitForExit(1000);

                if (proc.ExitCode == 0)
                {
                    resultBlock.Text = "✅ 已取消所有定时关机任务";
                    resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
                }
                else
                {
                    resultBlock.Text = "⚠️ 没有找到可取消的定时关机任务";
                    resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
                }
            }
            catch (Exception ex)
            {
                resultBlock.Text = $"❌ 操作失败：{ex.Message}";
                resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
            }
        };

        inputRow.Children.Add(label);
        inputRow.Children.Add(minuteInput);
        inputRow.Children.Add(shutdownButton);
        inputRow.Children.Add(cancelButton);

        panel.Children.Add(desc);
        panel.Children.Add(inputRow);
        panel.Children.Add(resultBlock);
        panel.Children.Add(quickLabel);
        panel.Children.Add(quickGrid);

        // 取消按钮已移到上方输入行右侧，此处不再重复

        return panel;
    }
}