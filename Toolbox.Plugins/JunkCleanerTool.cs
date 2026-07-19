using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// C 盘垃圾清理 —— 分类扫描系统/开发缓存垃圾，勾选后清理（默认移入回收站）
/// </summary>
public class JunkCleanerTool : ITool
{
    // ===== 数据模型 =====

    private class JunkCategory
    {
        public required string Name { get; init; }
        public required string Subtitle { get; init; }
        public required Func<List<string>> Roots { get; init; }
        public bool DefaultChecked { get; init; }
        public bool IsRecycleBin { get; init; }
        public long SizeBytes { get; set; }
        public int FileCount { get; set; }
        public CheckBox? Check { get; set; }
        public TextBlock? SizeText { get; set; }
    }

    private readonly List<JunkCategory> _categories;
    private Button? _scanButton;
    private Button? _cleanButton;
    private TextBlock? _progressText;
    private TextBlock? _statusText;
    private TextBlock? _errorText;
    private CheckBox? _protectRecentCheck;
    private CancellationTokenSource? _cts;
    private bool _isBusy;

    private static readonly Color BgDark = Color.FromRgb(0x2D, 0x2D, 0x2D);
    private static readonly Color TextPrimary = Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly Color TextSecondary = Color.FromRgb(0x80, 0x80, 0x80);
    private static readonly Color Success = Color.FromRgb(0x63, 0xD4, 0x7E);
    private static readonly Color Danger = Color.FromRgb(0xF0, 0x70, 0x70);
    private static readonly Color Warning = Color.FromRgb(0xE0, 0xA0, 0x30);

    public string Name => "C盘垃圾清理";
    public string Description => "分类扫描 C 盘常见垃圾文件，勾选后一键清理，文件先移入回收站。";
    public string Category => Toolbox.Models.ToolCategory.System;
    public string IconGlyph => "🗑️";

    public JunkCleanerTool()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        _categories =
        [
            // ---- 第一层：系统垃圾，绝对安全，默认勾选 ----
            new JunkCategory
            {
                Name = "用户临时文件",
                Subtitle = "应用程序运行产生的临时文件",
                Roots = () => [Path.GetTempPath()],
                DefaultChecked = true
            },
            new JunkCategory
            {
                Name = "Windows 临时文件",
                Subtitle = "系统级临时文件（部分需要管理员权限）",
                Roots = () => [Path.Combine(windowsDir, "Temp")],
                DefaultChecked = true
            },
            new JunkCategory
            {
                Name = "Windows 错误报告",
                Subtitle = "程序崩溃后留下的诊断报告",
                Roots = () => [Path.Combine(programData, "Microsoft", "Windows", "WER")],
                DefaultChecked = true
            },
            new JunkCategory
            {
                Name = "缩略图缓存",
                Subtitle = "资源管理器的图片预览缓存，删除后自动重建",
                Roots = () => [Path.Combine(localAppData, "Microsoft", "Windows", "Explorer")],
                DefaultChecked = true
            },
            new JunkCategory
            {
                Name = "DirectX 着色器缓存",
                Subtitle = "游戏/图形程序的着色器缓存，删除后自动重建",
                Roots = () => [Path.Combine(localAppData, "D3DSCache")],
                DefaultChecked = true
            },

            // ---- 第二层：安全但占用大，默认勾选 ----
            new JunkCategory
            {
                Name = "Windows Update 下载缓存",
                Subtitle = "更新安装包残留（部分需要管理员权限）",
                Roots = () => [Path.Combine(windowsDir, "SoftwareDistribution", "Download")],
                DefaultChecked = true
            },

            // ---- 第三层：需用户确认，默认不勾选 ----
            new JunkCategory
            {
                Name = "回收站",
                Subtitle = "⚠️ 清理为永久删除，不可恢复",
                Roots = () => [@"C:\$Recycle.Bin"],
                IsRecycleBin = true
            },
            new JunkCategory
            {
                Name = "NuGet 包缓存",
                Subtitle = ".NET 开发的全局包缓存，删除后按需重新下载",
                Roots = () => [Path.Combine(userProfile, ".nuget", "packages")]
            },
            new JunkCategory
            {
                Name = "npm 缓存",
                Subtitle = "Node.js 开发的包缓存，删除后按需重新下载",
                Roots = () => [Path.Combine(localAppData, "npm-cache")]
            },
            new JunkCategory
            {
                Name = "pip 缓存",
                Subtitle = "Python 开发的包缓存，删除后按需重新下载",
                Roots = () => [Path.Combine(localAppData, "pip", "cache")]
            },
            new JunkCategory
            {
                Name = "Chrome 缓存",
                Subtitle = "浏览器网页缓存（清理前请关闭 Chrome）",
                Roots = () => [Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache")]
            },
            new JunkCategory
            {
                Name = "Edge 缓存",
                Subtitle = "浏览器网页缓存（清理前请关闭 Edge）",
                Roots = () => [Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache")]
            },
        ];
    }

    // ===== UI =====

    public UIElement CreateContent()
    {
        var root = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 描述
        root.Children.Add(new TextBlock
        {
            Text = "扫描 C 盘常见垃圾文件，按类别勾选后一键清理。清理的文件会先移入回收站，可随时恢复。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 0, 0, 14)
        });

        // 操作栏
        var actionBar = new StackPanel { Orientation = Orientation.Horizontal };
        _scanButton = new Button
        {
            Content = "🔍 开始扫描",
            FontSize = 13,
            Padding = new Thickness(10, 5, 10, 5)
        };
        _scanButton.Click += async (_, _) => await StartScanAsync();

        _cleanButton = new Button
        {
            Content = "🗑️ 清理已选",
            FontSize = 13,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        _cleanButton.Click += async (_, _) => await StartCleanAsync();

        _protectRecentCheck = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            Content = "跳过 24 小时内修改的文件",
            IsChecked = true,
            Foreground = new SolidColorBrush(TextSecondary),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };

        actionBar.Children.Add(_scanButton);
        actionBar.Children.Add(_cleanButton);
        actionBar.Children.Add(_protectRecentCheck);
        root.Children.Add(actionBar);

        // 错误/提示
        _errorText = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Danger),
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_errorText);

        // 类别列表
        var listBorder = new Border
        {
            Background = new SolidColorBrush(BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 12, 0, 0)
        };
        var categoryPanel = new StackPanel();
        foreach (var cat in _categories)
            categoryPanel.Children.Add(BuildCategoryRow(cat));
        listBorder.Child = categoryPanel;
        root.Children.Add(listBorder);

        // 进度
        _progressText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_progressText);

        // 状态栏
        _statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 6, 0, 12)
        };
        root.Children.Add(_statusText);

        UpdateCleanButton();
        return root;
    }

    private UIElement BuildCategoryRow(JunkCategory cat)
    {
        var row = new Grid { Margin = new Thickness(4, 5, 4, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = cat.DefaultChecked
        };
        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = cat.Name,
            FontSize = 13,
            Foreground = new SolidColorBrush(TextPrimary)
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = cat.Subtitle,
            FontSize = 11,
            Foreground = new SolidColorBrush(cat.IsRecycleBin ? Warning : TextSecondary)
        });
        check.Content = textPanel;
        check.Checked += (_, _) => UpdateCleanButton();
        check.Unchecked += (_, _) => UpdateCleanButton();
        cat.Check = check;

        var sizeText = new TextBlock
        {
            Text = "未扫描",
            FontSize = 12,
            Foreground = new SolidColorBrush(TextSecondary),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 4, 0)
        };
        cat.SizeText = sizeText;

        Grid.SetColumn(check, 0);
        Grid.SetColumn(sizeText, 1);
        row.Children.Add(check);
        row.Children.Add(sizeText);
        return row;
    }

    // ===== 扫描 =====

    private async Task StartScanAsync()
    {
        if (_isBusy) return;
        SetBusy(true);
        HideError();
        _statusText!.Text = "";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        foreach (var cat in _categories)
        {
            cat.SizeBytes = 0;
            cat.FileCount = 0;
            cat.SizeText!.Text = "扫描中...";
        }

        int scanned = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var cat in _categories)
                {
                    token.ThrowIfCancellationRequested();
                    ReportProgress($"正在扫描：{cat.Name}");

                    foreach (var root in cat.Roots())
                    {
                        if (!Directory.Exists(root)) continue;
                        var (size, count) = ScanDirectory(root, token);
                        cat.SizeBytes += size;
                        cat.FileCount += count;
                    }

                    var c = cat;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        c.SizeText!.Text = c.FileCount > 0
                            ? $"{FormatSize(c.SizeBytes)} ({c.FileCount:N0} 个文件)"
                            : "无垃圾文件";
                        scanned++;
                        _statusText!.Text = $"已扫描 {scanned}/{_categories.Count} 类...";
                        UpdateCleanButton();
                    });
                }
            }, token);

            long totalSize = _categories.Sum(c => c.SizeBytes);
            _statusText!.Text = $"✅ 扫描完成，共发现 {FormatSize(totalSize)} 可清理内容";
            _statusText.Foreground = new SolidColorBrush(Success);
        }
        catch (OperationCanceledException)
        {
            _statusText!.Text = "扫描已取消";
            _statusText.Foreground = new SolidColorBrush(Warning);
        }
        catch (Exception ex)
        {
            ShowError($"⚠️ 扫描失败：{ex.Message}");
        }
        finally
        {
            ReportProgress(null);
            SetBusy(false);
        }
    }

    /// <summary>
    /// 迭代式遍历目录，只累加大小和文件数，不持有文件列表（内存 O(1)）
    /// </summary>
    private static (long size, int count) ScanDirectory(string rootPath, CancellationToken token)
    {
        long size = 0;
        int count = 0;
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            IEnumerable<string> files;
            IEnumerable<string> subDirs;
            try
            {
                files = Directory.EnumerateFiles(dir);
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch { continue; }  // 权限拒绝等，整个目录跳过

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    size += new FileInfo(file).Length;
                    count++;
                }
                catch { }  // 文件被占用/已删除，跳过
            }

            foreach (var sub in subDirs)
                stack.Push(sub);
        }

        return (size, count);
    }

    // ===== 清理 =====

    private async Task StartCleanAsync()
    {
        if (_isBusy) return;

        var selected = _categories
            .Where(c => c.Check?.IsChecked == true && c.FileCount > 0)
            .ToList();

        if (selected.Count == 0)
        {
            ShowError("没有可清理的已选类别（请先扫描，或勾选项均无垃圾）。");
            return;
        }

        long totalSize = selected.Sum(c => c.SizeBytes);
        bool hasRecycleBin = selected.Any(c => c.IsRecycleBin);

        var message = $"将清理 {selected.Count} 个类别，共 {FormatSize(totalSize)}。\n\n"
            + (hasRecycleBin
                ? "⚠️ 其中包含「回收站」，清空后不可恢复！\n其余类别的文件会先移入回收站。"
                : "文件会先移入回收站，误删可恢复。")
            + "\n\n确定继续吗？";

        if (MessageBox.Show(message, "确认清理", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        HideError();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        bool protectRecent = _protectRecentCheck?.IsChecked == true;
        var recentThreshold = DateTime.Now.AddHours(-24);

        int cleanedCats = 0, skippedItems = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var cat in selected)
                {
                    token.ThrowIfCancellationRequested();
                    ReportProgress($"正在清理：{cat.Name}");

                    int skipped = cat.IsRecycleBin
                        ? EmptyRecycleBin()
                        : CleanCategory(cat, protectRecent, recentThreshold, token);
                    skippedItems += skipped;

                    var c = cat;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        cleanedCats++;
                        _statusText!.Text = $"已清理 {cleanedCats}/{selected.Count} 类...";
                    });
                }
            }, token);

            _statusText!.Text = skippedItems > 0
                ? $"✅ 清理完成（{skippedItems} 个被占用的文件已跳过），正在重新扫描..."
                : "✅ 清理完成，正在重新扫描...";
            _statusText.Foreground = new SolidColorBrush(Success);

            // 重新扫描被清理的类别，刷新显示
            await RescanCategoriesAsync(selected, token);
        }
        catch (OperationCanceledException)
        {
            _statusText!.Text = "清理已取消";
            _statusText.Foreground = new SolidColorBrush(Warning);
        }
        catch (Exception ex)
        {
            ShowError($"⚠️ 清理失败：{ex.Message}");
        }
        finally
        {
            ReportProgress(null);
            SetBusy(false);
        }
    }

    /// <summary>
    /// 删除类别根目录下的所有顶层条目（保留根目录本身），移入回收站。
    /// 返回因被占用等原因跳过的条目数。
    /// </summary>
    private static int CleanCategory(
        JunkCategory cat, bool protectRecent, DateTime recentThreshold, CancellationToken token)
    {
        int skipped = 0;

        foreach (var root in cat.Roots())
        {
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(root).ToList(); }
            catch { continue; }

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (protectRecent && File.GetLastWriteTime(entry) > recentThreshold)
                    {
                        skipped++;
                        continue;
                    }

                    if (Directory.Exists(entry))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            entry,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            entry,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch { skipped++; }  // 被占用/权限不足，跳过
            }
        }

        return skipped;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    /// <summary>清空回收站（永久删除）。返回 0，失败按跳过处理。</summary>
    private static int EmptyRecycleBin()
    {
        try
        {
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            // 0x8000FFFF 等异常直接走 catch；空回收站返回错误码也忽略
            return hr == 0 ? 0 : 0;
        }
        catch { return 0; }
    }

    private async Task RescanCategoriesAsync(List<JunkCategory> cats, CancellationToken token)
    {
        foreach (var cat in cats)
        {
            cat.SizeBytes = 0;
            cat.FileCount = 0;
            foreach (var root in cat.Roots())
            {
                if (!Directory.Exists(root)) continue;
                var (size, count) = await Task.Run(() => ScanDirectory(root, token), token);
                cat.SizeBytes += size;
                cat.FileCount += count;
            }

            var c = cat;
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                c.SizeText!.Text = c.FileCount > 0
                    ? $"{FormatSize(c.SizeBytes)} ({c.FileCount:N0} 个文件)"
                    : "无垃圾文件";
            });
        }

        long remainTotal = _categories.Sum(c => c.SizeBytes);
        _statusText!.Text = $"✅ 清理完成，剩余可清理 {FormatSize(remainTotal)}";
        UpdateCleanButton();
    }

    // ===== 辅助 =====

    private void UpdateCleanButton()
    {
        if (_cleanButton == null) return;
        long selectedSize = _categories
            .Where(c => c.Check?.IsChecked == true && c.FileCount > 0)
            .Sum(c => c.SizeBytes);
        _cleanButton.IsEnabled = !_isBusy && selectedSize > 0;
        _cleanButton.Content = selectedSize > 0
            ? $"🗑️ 清理已选 ({FormatSize(selectedSize)})"
            : "🗑️ 清理已选";
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        if (_scanButton != null) _scanButton.IsEnabled = !busy;
        UpdateCleanButton();
    }

    private void ReportProgress(string? text)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_progressText == null) return;
            _progressText.Text = text ?? "";
            _progressText.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    private void ShowError(string msg)
    {
        if (_errorText == null) return;
        _errorText.Text = msg;
        _errorText.Visibility = Visibility.Visible;
        _statusText?.SetCurrentValue(TextBlock.ForegroundProperty, new SolidColorBrush(TextSecondary));
    }

    private void HideError()
    {
        if (_errorText != null) _errorText.Visibility = Visibility.Collapsed;
        if (_statusText != null)
            _statusText.Foreground = new SolidColorBrush(TextSecondary);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:F1} {units[unit]}";
    }

    private static Style? FindResourceStyle(string key)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Style style)
                return style;
        }
        catch { }
        return null;
    }
}
