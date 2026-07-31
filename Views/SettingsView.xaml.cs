using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Core.Services;
using Toolbox.Models;
using Toolbox.Plugins.Helpers;
using Toolbox.Tools;
using Toolbox.ViewModels;

namespace Toolbox.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // 每次进入设置页（Visibility 切换为可见）时重新检测引擎状态，
        // 覆盖"启动后下载/删除引擎再进设置"的状态过期场景
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) UpdateEngineStatus();
        };
        UpdateEngineStatus();
    }

    /// <summary>刷新 OCR 引擎状态文字（路径 + 大小 + 状态）</summary>
    private void UpdateEngineStatus()
    {
        var engineDir = EngineDownloader.DefaultEngineDirectory;
        var text = EngineStatusText;
        if (text == null) return; // InitializeComponent 未完成（构造函数早期调用防御）

        if (EngineDownloader.IsDownloaded)
        {
            text.Text = $"已下载：{FormatSize(GetDirectorySize(engineDir))}\n{engineDir}";
            text.Foreground = new SolidColorBrush(ThemeColors.TextSecondary);
        }
        else if (Directory.Exists(engineDir))
        {
            text.Text = $"引擎文件不完整（{FormatSize(GetDirectorySize(engineDir))}）\n{engineDir}";
            text.Foreground = new SolidColorBrush(ThemeColors.Warning);
        }
        else
        {
            text.Text = "未下载";
            text.Foreground = new SolidColorBrush(ThemeColors.TextSecondary);
        }
    }

    private void DeleteEngineButton_Click(object sender, RoutedEventArgs e)
    {
        var engineDir = EngineDownloader.DefaultEngineDirectory;
        if (!Directory.Exists(engineDir))
        {
            ShowEngineNotice("未检测到已下载的引擎。", ThemeColors.Warning);
            return;
        }

        var size = FormatSize(GetDirectorySize(engineDir));
        var confirm = new ConfirmDialog(
            $"将删除已下载的 OCR 高精度引擎（{size}）及其全部模型文件：\n{engineDir}\n\n" +
            "删除后截图识字将回退到 Windows 内置引擎，可随时重新下载。此操作不可撤销。",
            "删除 OCR 引擎", "删除");
        confirm.ShowDialog();
        if (!confirm.Confirmed) // ConfirmDialog 不设 DialogResult，以 Confirmed 属性为准
            return;

        // 先卸载已加载的引擎，释放原生 DLL 的进程锁定（不卸载则 Directory.Delete 必失败）
        if (Application.Current?.MainWindow?.DataContext is MainViewModel vm)
        {
            foreach (var tool in vm.Tools)
            {
                if (tool is OcrTool ocr)
                    ocr.UnloadEngine();
            }
        }

        try
        {
            Directory.Delete(engineDir, true);
            ShowEngineNotice($"✅ 已删除（{size}），可随时重新下载。", ThemeColors.Success);
        }
        catch (IOException)
        {
            ShowEngineNotice("⚠️ 部分引擎文件被占用，删除未完成。请重启 Toolbox 后再试。", ThemeColors.Danger);
        }
        catch (Exception ex)
        {
            ShowEngineNotice($"❌ 删除失败：{ex.Message}", ThemeColors.Danger);
        }
    }

    private void ShowEngineNotice(string message, Color color)
    {
        EngineStatusText.Text = message;
        EngineStatusText.Foreground = new SolidColorBrush(color);
    }

    /// <summary>递归计算目录占用字节数</summary>
    private static long GetDirectorySize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch
        {
            return 0L;
        }
    }

    /// <summary>格式化文件大小</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        // 触发一个可路由的 BackRequested 事件
        RaiseEvent(new RoutedEventArgs(BackRequestedEvent));
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow main)
            main.Shutdown();
    }

    public static readonly RoutedEvent BackRequestedEvent =
        EventManager.RegisterRoutedEvent("BackRequested", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(SettingsView));

    public event RoutedEventHandler BackRequested
    {
        add => AddHandler(BackRequestedEvent, value);
        remove => RemoveHandler(BackRequestedEvent, value);
    }
}