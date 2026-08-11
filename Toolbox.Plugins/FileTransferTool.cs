using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Toolbox.Core.Models;
using Toolbox.Models;
using Toolbox.Plugins.Helpers;
using Toolbox.Plugins.Services;

namespace Toolbox.Tools;

/// <summary>
/// 局域网文件传输工具：手机浏览器与电脑双向传大文件（HTTP over TCP，流式不落内存）。
/// 与远程控制共用同一服务/端口/控制页（用户决策 2026-08-11：同端口同页面），
/// 服务生命周期归远程控制页管理——本面板只做接收目录、待发送清单与传输记录。
/// </summary>
public class FileTransferTool : ITool
{
    private static FileTransferService Service => FileTransferService.Instance;

    private Ellipse? _statusDot;
    private TextBlock? _statusTitle;
    private TextBlock? _statusCaption;
    private Button? _gotoRemoteButton;
    private TextBlock? _dirValue;
    private ItemsControl? _shareItems;
    private ItemsControl? _recordItems;

    public string Name => "文件传输";
    public string Description => "局域网内手机与电脑双向传输大文件（与远程控制共用服务与端口，手机浏览器扫码即用）。";
    public string Category => ToolCategory.Network;
    public string IconGlyph => "📁";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // ① 说明文字
        panel.Children.Add(new TextBlock
        {
            Text = "与远程控制共用同一服务：手机浏览器打开控制页即可双向传输文件（发送到电脑 / 下载电脑共享的文件）。大文件流式传输，不占用内存。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 16)
        });

        // ② 服务状态卡片：状态圆点 + 主标题 + 次级说明（对齐远程控制页状态行语言）
        var statusCard = BuildCard("服务状态");
        var statusInner = (StackPanel)statusCard.Child;
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        _statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Margin = new Thickness(1, 1, 8, 0), // 下移 1px 与文字基线视觉对齐（2026-08-11 用户微调）
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusTitle = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
        };
        statusRow.Children.Add(_statusDot);
        statusRow.Children.Add(_statusTitle);
        statusInner.Children.Add(statusRow);
        _statusCaption = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(19, 4, 0, 0)
        };
        statusInner.Children.Add(_statusCaption);
        _gotoRemoteButton = BuildButton("🛰️ 前往远程控制启动服务");
        _gotoRemoteButton.HorizontalAlignment = HorizontalAlignment.Left;
        _gotoRemoteButton.Margin = new Thickness(19, 10, 0, 0);
        _gotoRemoteButton.Click += (_, _) => ToolNavigation.Request("远程控制");
        statusInner.Children.Add(_gotoRemoteButton);
        statusCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(statusCard);

        // ③ 接收设置卡片：目录内嵌底色框（层级清晰）
        var receiveCard = BuildCard("接收设置");
        var receiveInner = (StackPanel)receiveCard.Child;
        receiveInner.Children.Add(new TextBlock
        {
            Text = "手机上传的文件保存到",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 6)
        });
        var dirBox = new Border
        {
            Background = ResBrush("BgCardBrush", Color.FromRgb(0x32, 0x32, 0x32)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        _dirValue = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
        };
        dirBox.Child = _dirValue;
        receiveInner.Children.Add(dirBox);
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal };
        var changeButton = BuildButton("📂 更改目录…");
        changeButton.Click += (_, _) => ChangeDirectory();
        var openButton = BuildButton("🗂 打开目录");
        openButton.Margin = new Thickness(10, 0, 0, 0);
        openButton.Click += (_, _) => OpenDirectory();
        dirRow.Children.Add(changeButton);
        dirRow.Children.Add(openButton);
        receiveInner.Children.Add(dirRow);
        receiveCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(receiveCard);

        // ④ 待发送文件卡片（手机端可见的下载清单）：行项卡片化
        var shareCard = BuildCard("待发送文件（手机端可下载）");
        var shareInner = (StackPanel)shareCard.Child;
        _shareItems = new ItemsControl { FontSize = 13 };
        shareInner.Children.Add(_shareItems);
        var shareRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var addButton = BuildButton("➕ 添加文件…");
        addButton.Click += (_, _) => AddFiles();
        var clearButton = BuildButton("🗑 清空");
        clearButton.Margin = new Thickness(10, 0, 0, 0);
        clearButton.Click += (_, _) => { Service.ClearSharedFiles(); RefreshShares(); };
        shareRow.Children.Add(addButton);
        shareRow.Children.Add(clearButton);
        shareInner.Children.Add(shareRow);
        shareCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(shareCard);

        // ⑤ 传输记录卡片（实时进度）
        var recordCard = BuildCard("传输记录");
        var recordInner = (StackPanel)recordCard.Child;
        _recordItems = new ItemsControl { FontSize = 13 };
        recordInner.Children.Add(_recordItems);
        recordCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(recordCard);

        // 进度事件订阅：Loaded 挂 / Unloaded 摘（内容缓存重建不泄漏，照规范模式）
        panel.Loaded += (_, _) =>
        {
            Service.ProgressChanged += OnProgress;
            RefreshAll();
        };
        panel.Unloaded += (_, _) => Service.ProgressChanged -= OnProgress;

        RefreshAll();
        return panel;
    }

    // ==================== 刷新 ====================

    private void RefreshAll()
    {
        RefreshServerStatus();
        RefreshDirectory();
        RefreshShares();
        RefreshRecords();
    }

    private void RefreshServerStatus()
    {
        var running = RemoteControlTool.IsServerRunning;
        _statusDot!.Fill = new SolidColorBrush(running ? ThemeColors.Success : ThemeColors.Warning);
        _statusTitle!.Text = running ? "服务运行中" : "服务未启动";
        _statusCaption!.Text = running
            ? "手机浏览器打开控制页即可双向传输文件"
            : "传输服务与远程控制同端口，需先在远程控制页启动";
        _gotoRemoteButton!.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshDirectory() => _dirValue!.Text = Service.SaveDirectory;

    private void RefreshShares()
    {
        _shareItems!.Items.Clear();
        var files = Service.SharedFiles;
        if (files.Count == 0)
        {
            _shareItems.Items.Add(BuildEmptyState("📤", "暂无共享文件，添加后手机端即可下载"));
            return;
        }
        foreach (var file in files)
        {
            // 行项卡片：文件名（超长省略 + 悬浮全路径）│ 大小 │ 移除（小按钮）
            var row = new DockPanel();
            var remove = BuildButton("移除", 26);
            remove.FontSize = 12;
            remove.Padding = new Thickness(8, 2, 8, 2);
            remove.Margin = new Thickness(8, 0, 0, 0);
            remove.Tag = file.Id;
            remove.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(remove, Dock.Right); // 附加属性不能对象初始化器
            remove.Click += (s, _) =>
            {
                if (s is Button { Tag: int id }) Service.RemoveSharedFile(id);
                RefreshShares();
            };
            var size = new TextBlock
            {
                Text = FormatSize(file.Size),
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(size, Dock.Right);
            var name = new TextBlock
            {
                Text = "📄 " + file.Name,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = file.FullPath,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
            };
            row.Children.Add(remove);
            row.Children.Add(size);
            row.Children.Add(name);
            _shareItems.Items.Add(WrapRowCard(row));
        }
    }

    private void RefreshRecords()
    {
        _recordItems!.Items.Clear();
        var records = Service.RecentRecords;
        if (records.Count == 0)
        {
            _recordItems.Items.Add(BuildEmptyState("🔄", "暂无传输记录"));
            return;
        }
        foreach (var record in records)
            _recordItems.Items.Add(BuildRecordRow(record));
    }

    /// <summary>传输记录行（卡片化）：方向 + 文件名 │ 状态；进行中显示自绘细进度条，失败显示原因</summary>
    private static UIElement BuildRecordRow(TransferProgress record)
    {
        var container = new StackPanel();

        var direction = record.Direction == TransferDirection.Upload ? "⬆️" : "⬇️";
        var (stateText, stateColor) = record.State switch
        {
            TransferState.InProgress => record.Total > 0
                ? ($"{record.Transferred * 100 / record.Total}%", ThemeColors.TextSecondary)
                : ("传输中", ThemeColors.TextSecondary),
            TransferState.Completed => ("完成", ThemeColors.Success),
            _ => ("失败", ThemeColors.Danger)
        };

        var top = new DockPanel();
        var stateEl = new TextBlock
        {
            Text = stateText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(stateColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(stateEl, Dock.Right);
        var nameEl = new TextBlock
        {
            Text = $"{direction} {record.FileName} · {FormatSize(record.Total)}",
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = record.FileName,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(record.State == TransferState.InProgress
                ? ThemeColors.TextPrimary
                : ThemeColors.TextSecondary)
        };
        top.Children.Add(stateEl);
        top.Children.Add(nameEl);
        container.Children.Add(top);

        if (record.State == TransferState.InProgress && record.Total > 0)
        {
            container.Children.Add(BuildSlimBar((double)record.Transferred / record.Total));
        }
        else if (record.State == TransferState.Failed && !string.IsNullOrEmpty(record.Error))
        {
            container.Children.Add(new TextBlock
            {
                Text = record.Error,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ThemeColors.Danger),
                Margin = new Thickness(0, 3, 0, 0)
            });
        }
        return WrapRowCard(container);
    }

    /// <summary>自绘细进度条（原生 ProgressBar 深色主题下突兀）： sunken 轨道 + Accent 填充，星标列控宽</summary>
    private static UIElement BuildSlimBar(double fraction)
    {
        var grid = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(fraction, 0.001, 0.999), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(1 - fraction, 0.001, 0.999), GridUnitType.Star) });
        var track = new Border
        {
            Background = ResBrush("BgDarkBrush", Color.FromRgb(0x1C, 0x1C, 0x1C)),
            CornerRadius = new CornerRadius(3)
        };
        Grid.SetColumnSpan(track, 2);
        var fill = new Border
        {
            Background = ResBrush("AccentBrush", Color.FromRgb(0x76, 0xB5, 0x80)),
            CornerRadius = new CornerRadius(3)
        };
        grid.Children.Add(track);
        grid.Children.Add(fill);
        return grid;
    }

    /// <summary>行项卡片包装：BgCard 底 + 6px 圆角，清单/记录行共用层级语言</summary>
    private static Border WrapRowCard(UIElement content) => new()
    {
        Background = ResBrush("BgCardBrush", Color.FromRgb(0x32, 0x32, 0x32)),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 7, 10, 7),
        Margin = new Thickness(0, 3, 0, 3),
        Child = content
    };

    /// <summary>空状态：居中图标 + 提示文字（清单/记录共用）</summary>
    private static UIElement BuildEmptyState(string icon, string text)
    {
        var box = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
        box.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        });
        box.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
        });
        return box;
    }

    // ==================== 事件 ====================

    /// <summary>进度事件（服务器连接线程触发）：经 Dispatcher 回 UI 线程整体重建列表（记录 ≤40 条，重建成本可忽略）</summary>
    private void OnProgress(TransferProgress progress)
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_recordItems != null) RefreshRecords();
            });
        }
        catch (Exception) { /* 窗口已关闭时静默 */ }
    }

    private void ChangeDirectory()
    {
        try
        {
            var dialog = new OpenFolderDialog { Title = "选择接收目录" };
            if (dialog.ShowDialog() == true)
                Service.SaveDirectory = dialog.FolderName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileTransferTool] 更改目录失败: {ex.Message}");
        }
        RefreshDirectory();
    }

    private void OpenDirectory()
    {
        try
        {
            var dir = Service.SaveDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileTransferTool] 打开目录失败: {ex.Message}");
        }
    }

    private void AddFiles()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要共享给手机的文件",
                Multiselect = true
            };
            if (dialog.ShowDialog() == true)
            {
                foreach (var path in dialog.FileNames)
                    Service.AddSharedFile(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileTransferTool] 添加文件失败: {ex.Message}");
        }
        RefreshShares();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1073741824 => $"{bytes / 1073741824.0:F1} GB",
        >= 1048576 => $"{bytes / 1048576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };

    // ==================== 样式辅助 ====================

    /// <summary>统一按钮构造（全局隐式 Accent 绿样式；尺寸与面板一致，2026-08-11 用户决策：不分级）</summary>
    private static Button BuildButton(string content, double height = 34) => new()
    {
        Content = content,
        FontSize = 13,
        Padding = new Thickness(12, 5, 12, 5),
        Height = height
    };

    /// <summary>取应用资源画刷（BgCardBrush/BgDarkBrush/AccentBrush），取不到回退同色硬编码</summary>
    private static Brush ResBrush(string key, Color fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    // ==================== 卡片模板（规范 4.2，与 RemoteControlTool 一致） ====================

    private static Border BuildCard(string title)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var card = new Border
        {
            Background = new SolidColorBrush(ThemeColors.BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = inner
        };
        GlowCardMarker.SetIsGlowCard(card, true);
        return card;
    }
}
