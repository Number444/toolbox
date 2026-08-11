using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    private TextBlock? _serverStatus;
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

        // ② 服务状态卡片
        var statusCard = BuildCard("服务状态");
        var statusInner = (StackPanel)statusCard.Child;
        _serverStatus = new TextBlock { FontSize = 14, Margin = new Thickness(0, 0, 0, 10) };
        statusInner.Children.Add(_serverStatus);
        _gotoRemoteButton = new Button
        {
            Content = "🛰️ 前往远程控制启动服务",
            FontSize = 13,
            Padding = new Thickness(12, 5, 12, 5),
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _gotoRemoteButton.Click += (_, _) => ToolNavigation.Request("远程控制");
        statusInner.Children.Add(_gotoRemoteButton);
        statusCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(statusCard);

        // ③ 接收设置卡片
        var receiveCard = BuildCard("接收设置");
        var receiveInner = (StackPanel)receiveCard.Child;
        _dirValue = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            Margin = new Thickness(0, 0, 0, 10)
        };
        receiveInner.Children.Add(_dirValue);
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal };
        var changeButton = new Button
        {
            Content = "📂 更改目录…",
            FontSize = 13,
            Padding = new Thickness(12, 5, 12, 5),
            Height = 34
        };
        changeButton.Click += (_, _) => ChangeDirectory();
        var openButton = new Button
        {
            Content = "🗂 打开目录",
            FontSize = 13,
            Padding = new Thickness(12, 5, 12, 5),
            Height = 34,
            Margin = new Thickness(10, 0, 0, 0)
        };
        openButton.Click += (_, _) => OpenDirectory();
        dirRow.Children.Add(changeButton);
        dirRow.Children.Add(openButton);
        receiveInner.Children.Add(dirRow);
        receiveCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(receiveCard);

        // ④ 待发送文件卡片（手机端可见的下载清单）
        var shareCard = BuildCard("待发送文件（手机端可下载）");
        var shareInner = (StackPanel)shareCard.Child;
        _shareItems = new ItemsControl { FontSize = 13 };
        shareInner.Children.Add(_shareItems);
        var shareRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var addButton = new Button
        {
            Content = "➕ 添加文件…",
            FontSize = 13,
            Padding = new Thickness(12, 5, 12, 5),
            Height = 34
        };
        addButton.Click += (_, _) => AddFiles();
        var clearButton = new Button
        {
            Content = "🗑 清空",
            FontSize = 13,
            Padding = new Thickness(12, 5, 12, 5),
            Height = 34,
            Margin = new Thickness(10, 0, 0, 0)
        };
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
        _serverStatus!.Text = running
            ? "● 服务运行中（手机打开控制页即可传输）"
            : "● 服务未启动（传输服务与远程控制同端口，需先启动）";
        _serverStatus.Foreground = new SolidColorBrush(running ? ThemeColors.Success : ThemeColors.Warning);
        _gotoRemoteButton!.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshDirectory() => _dirValue!.Text = "手机上传的文件保存到：" + Service.SaveDirectory;

    private void RefreshShares()
    {
        _shareItems!.Items.Clear();
        var files = Service.SharedFiles;
        if (files.Count == 0)
        {
            _shareItems.Items.Add(new TextBlock
            {
                Text = "（暂无共享文件）",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
            });
            return;
        }
        foreach (var file in files)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var remove = new Button
            {
                Content = "移除",
                FontSize = 12,
                Padding = new Thickness(8, 2, 8, 2),
                Height = 26,
                Margin = new Thickness(8, 0, 0, 0),
                Tag = file.Id
            };
            DockPanel.SetDock(remove, Dock.Right); // 附加属性不能对象初始化器
            remove.Click += (s, _) =>
            {
                if (s is Button { Tag: int id }) Service.RemoveSharedFile(id);
                RefreshShares();
            };
            var text = new TextBlock
            {
                Text = $"📄 {file.Name} · {FormatSize(file.Size)}",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
            };
            row.Children.Add(remove);
            row.Children.Add(text);
            _shareItems.Items.Add(row);
        }
    }

    private void RefreshRecords()
    {
        _recordItems!.Items.Clear();
        var records = Service.RecentRecords;
        if (records.Count == 0)
        {
            _recordItems.Items.Add(new TextBlock
            {
                Text = "（暂无传输）",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
            });
            return;
        }
        foreach (var record in records)
            _recordItems.Items.Add(BuildRecordRow(record));
    }

    /// <summary>传输记录行：方向图标 + 文件名/状态 + 进度条（进行中显示百分比）</summary>
    private static UIElement BuildRecordRow(TransferProgress record)
    {
        var container = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };

        var direction = record.Direction == TransferDirection.Upload ? "⬆️" : "⬇️";
        var (stateText, stateColor) = record.State switch
        {
            TransferState.InProgress => record.Total > 0
                ? ($"{record.Transferred * 100 / record.Total}%", ThemeColors.TextSecondary)
                : ("传输中", ThemeColors.TextSecondary),
            TransferState.Completed => ("✅ 完成", ThemeColors.Success),
            _ => ($"❌ {record.Error ?? "失败"}", ThemeColors.Danger)
        };

        container.Children.Add(new TextBlock
        {
            Text = $"{direction} {record.FileName} · {FormatSize(record.Total)} · {stateText}",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(record.State == TransferState.InProgress
                ? ThemeColors.TextPrimary
                : stateColor)
        });

        if (record.State == TransferState.InProgress && record.Total > 0)
        {
            container.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = record.Total,
                Value = record.Transferred,
                Height = 6,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        return container;
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
