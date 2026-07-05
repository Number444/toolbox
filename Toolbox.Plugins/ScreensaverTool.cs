using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 系统屏保启动工具 —— 选择系统内置屏保并启动
/// </summary>
public class ScreensaverTool : ITool
{
    public string Name => "系统屏保";
    public string Description => "选择并启动 Windows 内置屏保，仅保留稳定的屏保方案。";
    public string Category => Toolbox.Models.ToolCategory.System;
    public string IconGlyph => "🖥️";

    private static readonly (string label, string scrName)[] ScreenSavers =
    [
        ("◆ 空白 (Blank)", "(无 .scr，仅关闭屏幕)"),
        ("★ 变幻线 (Mystify)", "Mystify"),
        ("★ 彩带 (Ribbons)", "Ribbons"),
        ("★ 文字 (ssText3D)", "ssText3D"),
        ("★ 照片 (PhotoScreenSaver)", "PhotoScreenSaver"),
    ];

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 说明文字
        var desc = new TextBlock
        {
            Text = "选择系统屏保并启动。部分屏保名称可能因系统版本而异。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        };

        // 结果反馈
        var resultBlock = new TextBlock
        {
            Text = "",
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 16)
        };

        // ComboBox 选择屏保
        var combo = new ComboBox
        {
            Width = 260,
            Height = 34,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        foreach (var (label, _) in ScreenSavers)
            combo.Items.Add(label);

        combo.SelectedIndex = 0;

        // 启动按钮
        var launchButton = new Button
        {
            Content = "🖥️ 启动屏保",
            FontSize = 14,
            Padding = new Thickness(12),
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };

        launchButton.Click += (_, _) =>
        {
            int idx = combo.SelectedIndex;
            if (idx < 0 || idx >= ScreenSavers.Length) return;

            var (_, scrName) = ScreenSavers[idx];

            try
            {
                if (scrName == "(无 .scr，仅关闭屏幕)")
                {
                    // 空白屏保：启动 scrnsave.scr（系统内置空白屏保）
                    string blankPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "scrnsave.scr");
                    if (System.IO.File.Exists(blankPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = blankPath,
                            Arguments = "/s",
                            UseShellExecute = true,
                            CreateNoWindow = true
                        });
                        resultBlock.Text = "✅ 空白屏保已启动";
                        resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
                    }
                    else
                    {
                        resultBlock.Text = "⚠️ 未找到空白屏保文件";
                        resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30));
                    }
                    return;
                }

                // 直接运行屏保文件
                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string scrPath = System.IO.Path.Combine(systemDir, $"{scrName}.scr");

                if (System.IO.File.Exists(scrPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = scrPath,
                        Arguments = "/s",   // /s = 全屏启动
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    resultBlock.Text = $"✅ {scrName}.scr 已启动";
                    resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
                }
                else
                {
                    // 系统可能没有该屏保文件，尝试用系统屏保设置
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "control",
                        Arguments = "desk.cpl,,@screensaver",
                        UseShellExecute = true
                    });

                    resultBlock.Text = $"⚠️ 未找到 {scrName}.scr，已打开屏保设置";
                    resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30));
                }
            }
            catch (Exception ex)
            {
                resultBlock.Text = $"❌ 启动失败：{ex.Message}";
                resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
            }
        };

        panel.Children.Add(desc);
        panel.Children.Add(combo);
        panel.Children.Add(launchButton);
        panel.Children.Add(resultBlock);

        return panel;
    }
}