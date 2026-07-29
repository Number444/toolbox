using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;
using Toolbox.Tools.Helpers;

namespace Toolbox.Tools;

/// <summary>
/// 快捷系统操作 —— 重启资源管理器(主操作,置顶显眼) + 锁屏 / 关闭显示器 / 睡眠。
/// 电源类操作由 SystemPowerHelper 实现(与首页快捷操作卡共用)。
/// </summary>
public class QuickSystemTool : ITool
{
    // 配色统一使用 Toolbox.Models.ThemeColors
    public string Name => "快捷系统操作";
    public string Description => "一键锁屏、关闭显示器、睡眠,或在任务栏卡死时重启资源管理器。";
    public string Category => ToolCategory.System;
    public string IconGlyph => "⚡";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 结果反馈(固定在底部)
        var resultBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };

        // ====== 卡片 1:重启资源管理器(主操作,页面第一视觉落点) ======
        var explorerInner = new StackPanel();
        explorerInner.Children.Add(BuildCardTitle("资源管理器"));

        var warning = new TextBlock
        {
            Text = "⚠️ 此操作会关闭所有文件资源管理器窗口,请先保存工作。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.Warning),
            Margin = new Thickness(0, 0, 0, 10)
        };
        explorerInner.Children.Add(warning);

        var restartButton = new Button
        {
            Content = "🔄 重启资源管理器",
            FontSize = 15,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        restartButton.Click += (_, _) => RestartExplorer(resultBlock);
        explorerInner.Children.Add(restartButton);

        panel.Children.Add(BuildCard(explorerInner));

        // ====== 卡片 2:电源操作(三等分等宽按钮,间距统一) ======
        var powerInner = new StackPanel();
        powerInner.Children.Add(BuildCardTitle("电源操作"));

        var buttonGrid = new Grid();
        for (int i = 0; i < 3; i++)
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Button MakePowerButton(string text, int column)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 14,
                Height = 40,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(btn, column);
            return btn;
        }

        var lockButton = MakePowerButton("🔒 锁定电脑", 0);
        lockButton.Click += (_, _) => Report(SystemPowerHelper.Lock(), "已锁定", resultBlock);

        var monitorButton = MakePowerButton("🖥️ 关闭显示器", 1);
        monitorButton.Click += (_, _) =>
            Report(SystemPowerHelper.TurnOffMonitor(), "显示器已关闭,移动鼠标唤醒", resultBlock);

        var sleepButton = MakePowerButton("😴 睡眠", 2);
        sleepButton.Click += (_, _) => Report(SystemPowerHelper.Sleep(), "已进入睡眠", resultBlock);

        buttonGrid.Children.Add(lockButton);
        buttonGrid.Children.Add(monitorButton);
        buttonGrid.Children.Add(sleepButton);
        powerInner.Children.Add(buttonGrid);

        panel.Children.Add(BuildCard(powerInner));

        panel.Children.Add(resultBlock);
        return panel;
    }

    /// <summary>统一的操作结果反馈(即时生效的操作通常看不到成功文字——锁屏/睡眠后 UI 已不可见,仅作失败兜底)</summary>
    private static void Report(bool ok, string successText, TextBlock resultBlock)
    {
        if (ok)
        {
            resultBlock.Text = $"✅ {successText}";
            resultBlock.Foreground = new SolidColorBrush(ThemeColors.Success);
        }
        else
        {
            resultBlock.Text = "❌ 操作失败";
            resultBlock.Foreground = new SolidColorBrush(ThemeColors.Danger);
        }
    }

    private static void RestartExplorer(TextBlock resultBlock)
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
            resultBlock.Foreground = new SolidColorBrush(ThemeColors.Success);
        }
        catch (Exception ex)
        {
            resultBlock.Text = $"❌ 操作失败:{ex.Message}";
            resultBlock.Foreground = new SolidColorBrush(ThemeColors.Danger);
        }
    }

    /// <summary>卡片内标题(13px 次要色,与内容间距 8)</summary>
    private static TextBlock BuildCardTitle(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
        Margin = new Thickness(0, 0, 0, 8)
    };

    /// <summary>统一样式卡片(圆角深色底 + 鼠标照亮 opt-in,统一 Padding 14 / 间距 12)</summary>
    private static Border BuildCard(UIElement content)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(ThemeColors.BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content
        };
        GlowCardMarker.SetIsGlowCard(card, true);
        return card;
    }
}
