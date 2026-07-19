using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
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
        /// <summary>文件名过滤（如 "thumbcache_*.db"）；非空时只统计/删除根目录下匹配的文件，不递归、不删目录</summary>
        public string[]? FileFilter { get; init; }
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
    private Border? _statusArea;
    private System.Windows.Shapes.Ellipse? _spinner;
    private TextBlock? _doNotCloseText;

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
            // ---- 需用户确认，默认不勾选 ----
            new JunkCategory
            {
                Name = "回收站",
                Subtitle = "⚠️ 清理为永久删除，不可恢复",
                Roots = () => [@"C:\$Recycle.Bin"],
                IsRecycleBin = true
            },

            // ---- 系统垃圾，默认勾选 ----
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
                Subtitle = "系统级临时文件（建议以管理员身份运行 Toolbox 以完全清理）",
                Roots = () => [Path.Combine(windowsDir, "Temp")],
                DefaultChecked = true
            },
            new JunkCategory
            {
                Name = "Windows 错误报告",
                Subtitle = "程序崩溃后留下的诊断报告",
                Roots = () => [Path.Combine(programData, "Microsoft", "Windows", "WER")]
            },

            // ---- 开发缓存，默认不勾选 ----
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
                Roots = () => [Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache")],
                DefaultChecked = true
            },

            // ---- 其他缓存，默认勾选 ----
            new JunkCategory
            {
                Name = "Windows Update 下载缓存",
                Subtitle = "更新安装包残留（建议以管理员身份运行 Toolbox 以完全清理）",
                Roots = () => [Path.Combine(windowsDir, "SoftwareDistribution", "Download")],
                DefaultChecked = true
            },
            new JunkCategory
            {
                Name = "缩略图缓存",
                Subtitle = "资源管理器的缩略图/图标缓存，删除后自动重建",
                Roots = () => [Path.Combine(localAppData, "Microsoft", "Windows", "Explorer")],
                FileFilter = ["thumbcache_*.db", "iconcache_*.db"],
                DefaultChecked = true
            },
            new JunkCategory
            {
                Name = "DirectX 着色器缓存",
                Subtitle = "游戏/图形程序的着色器缓存，删除后自动重建",
                Roots = () => [Path.Combine(localAppData, "D3DSCache")]
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

        // 状态栏（顶部显眼位置，含进度、结果、加载动画）
        _statusArea = new Border
        {
            Background = new SolidColorBrush(BgDark),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 10, 0, 6),
            Visibility = Visibility.Collapsed
        };
        var statusStack = new StackPanel();

        // 第一行：旋转动画 + 进度文字
        var spinnerRow = new StackPanel { Orientation = Orientation.Horizontal };
        _spinner = new System.Windows.Shapes.Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = new SolidColorBrush(Success),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection(new double[] { 25, 75 }),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Visibility = Visibility.Collapsed
        };
        var transformGroup = new TransformGroup();
        var scaleTransform = new ScaleTransform(1.0, 1.0);
        transformGroup.Children.Add(scaleTransform);
        _spinner.RenderTransform = transformGroup;

        // 呼吸（10px ↔ 15px，基准 14px）
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(10.0 / 14.0, 15.0 / 14.0, new Duration(TimeSpan.FromSeconds(0.8)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(10.0 / 14.0, 15.0 / 14.0, new Duration(TimeSpan.FromSeconds(0.8)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

        // 粗细同步呼吸（最大时 2.5，最小时 1.0）
        _spinner.BeginAnimation(System.Windows.Shapes.Ellipse.StrokeThicknessProperty,
            new DoubleAnimation(1.5, 2.5, new Duration(TimeSpan.FromSeconds(0.8)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

        _progressText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(TextPrimary),
            Margin = new Thickness(8, -1, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        spinnerRow.Children.Add(_spinner);
        spinnerRow.Children.Add(_progressText);

        // 第二行：状态结果
        _statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, -1, 0, 0)
        };

        // 第三行：请勿退出提示
        _doNotCloseText = new TextBlock
        {
            Text = "⚠️ 请勿关闭程序，清理正在进行中...",
            FontSize = 12,
            Foreground = new SolidColorBrush(Warning),
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };

        statusStack.Children.Add(spinnerRow);
        statusStack.Children.Add(_statusText);
        statusStack.Children.Add(_doNotCloseText);
        _statusArea.Child = statusStack;
        root.Children.Add(_statusArea);

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
                        var (size, count) = ScanDirectory(root, token, cat.FileFilter);
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
    /// 迭代式遍历目录，只累加大小和文件数，不持有文件列表（内存 O(1)）。
    /// fileFilter 非空时只统计根目录下匹配的文件，不递归子目录。
    /// </summary>
    private static (long size, int count) ScanDirectory(
        string rootPath, CancellationToken token, string[]? fileFilter = null)
    {
        long size = 0;
        int count = 0;
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            // 物化当前目录的枚举结果（仅限单目录，不累积整棵树），
            // 避免延迟枚举在 foreach 中抛出权限异常逃逸 catch
            List<string> files;
            List<string> subDirs;
            try
            {
                files = (fileFilter is { Length: > 0 } filter
                    ? filter.SelectMany(p => Directory.EnumerateFiles(dir, p))
                    : Directory.EnumerateFiles(dir)).ToList();
                subDirs = Directory.EnumerateDirectories(dir).ToList();
            }
            catch (IOException) { continue; }              // 目录不可读，跳过
            catch (UnauthorizedAccessException) { continue; }  // 权限拒绝，跳过

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    size += new FileInfo(file).Length;
                    count++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            // 过滤模式不递归（只针对根目录下的匹配文件）
            if (fileFilter is { Length: > 0 }) continue;

            foreach (var sub in subDirs)
            {
                if (!IsReparsePoint(sub))
                    stack.Push(sub);
            }
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
                ? "其中包含「回收站」，清空后不可恢复。\n其余类别的文件会先移入回收站，可随时恢复。"
                : "文件会先移入回收站，误删可恢复。");

        var dialog = new ConfirmDialog(message, "确认清理", hasRecycleBin);
        dialog.ShowDialog();
        if (!dialog.Confirmed)
            return;

        SetBusy(true);
        HideError();
        _statusText!.Text = "";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        bool protectRecent = _protectRecentCheck?.IsChecked == true;
        var recentThreshold = DateTime.Now.AddHours(-24);

        int cleanedCats = 0, skippedItems = 0;
        bool recycleBinFailed = false;
        try
        {
            await Task.Run(() =>
            {
                foreach (var cat in selected)
                {
                    token.ThrowIfCancellationRequested();
                    ReportProgress($"正在清理：{cat.Name}");

                    if (cat.IsRecycleBin)
                    {
                        recycleBinFailed = !EmptyRecycleBin();
                    }
                    else
                    {
                        skippedItems += CleanCategory(cat, protectRecent, recentThreshold, token);
                    }

                    var c = cat;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        cleanedCats++;
                        _statusText!.Text = $"已清理 {cleanedCats}/{selected.Count} 类...";
                    });
                }
            }, token);

            var notes = new List<string>();
            if (skippedItems > 0)
                notes.Add($"{skippedItems} 个文件因被占用或权限不足被跳过");
            if (recycleBinFailed)
                notes.Add("回收站清空失败");

            _statusText!.Text = notes.Count > 0
                ? $"✅ 清理完成（{string.Join("；", notes)}），正在重新扫描..."
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
    /// 删除类别根目录下的垃圾条目（保留根目录本身），移入回收站。
    /// FileFilter 非空时只删除根目录下匹配的文件，不动目录和其他文件。
    /// 返回因被占用/权限不足/受保护而跳过的条目数。
    /// </summary>
    private static int CleanCategory(
        JunkCategory cat, bool protectRecent, DateTime recentThreshold, CancellationToken token)
    {
        int skipped = 0;

        foreach (var root in cat.Roots())
        {
            if (!Directory.Exists(root)) continue;

            // ---- 过滤模式：只删匹配文件（如 thumbcache_*.db），不碰其他内容 ----
            if (cat.FileFilter is { Length: > 0 } filter)
            {
                List<string> matched;
                try { matched = filter.SelectMany(p => Directory.EnumerateFiles(root, p)).ToList(); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                foreach (var file in matched)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (protectRecent && File.GetLastWriteTime(file) > recentThreshold)
                        {
                            skipped++;
                            continue;
                        }
                        DeleteToRecycleBin(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped++;  // 被占用/权限不足，跳过
                    }
                }
                continue;
            }

            // ---- 常规模式：删除根目录下所有顶层条目 ----
            List<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(root).ToList(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                bool isDir = Directory.Exists(entry);
                try
                {
                    if (protectRecent)
                    {
                        // 目录需递归检查内部文件（目录自身时间戳不反映内容新旧）
                        bool hasRecent = isDir
                            ? DirectoryContainsRecentFile(entry, recentThreshold, token)
                            : File.GetLastWriteTime(entry) > recentThreshold;
                        if (hasRecent)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    if (isDir)
                        DeleteToRecycleBin(entry);
                    else
                        DeleteToRecycleBin(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;  // 被占用/权限不足，跳过
                }
            }
        }

        return skipped;
    }

    /// <summary>
    /// 递归检查目录内是否有 threshold 之后修改的文件。
    /// 流式枚举 + 命中即短路返回，不持有文件列表。
    /// </summary>
    private static bool DirectoryContainsRecentFile(
        string dirPath, DateTime threshold, CancellationToken token)
    {
        var stack = new Stack<string>();
        stack.Push(dirPath);

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            List<string> files;
            List<string> subDirs;
            try
            {
                files = Directory.EnumerateFiles(dir).ToList();
                subDirs = Directory.EnumerateDirectories(dir).ToList();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                try
                {
                    if (File.GetLastWriteTime(file) > threshold)
                        return true;  // 命中即返回，不再遍历
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            foreach (var sub in subDirs)
            {
                if (!IsReparsePoint(sub))
                    stack.Push(sub);
            }
        }

        return false;
    }

    /// <summary>检查路径是否为 junction/符号链接等重解析点，避免遍历死循环。</summary>
    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch { return true; }  // 无法判断时保守视为链接，跳过
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    /// <summary>清空回收站（永久删除）。返回是否成功。</summary>
    private static bool EmptyRecycleBin()
    {
        try
        {
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return hr == 0;  // S_OK
        }
        catch { return false; }
    }

    // ===== SHFileOperation：静默删除到回收站，不弹错误对话框 =====

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_SILENT = 0x0004;

    /// <summary>静默删除到回收站（占用的文件自动跳过，不弹对话框）。失败时抛异常。</summary>
    private static void DeleteToRecycleBin(string path)
    {
        var shfos = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = path + '\0',  // 必须双 null 结尾
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
        };
        int result = SHFileOperation(ref shfos);
        if (result != 0)
            throw new IOException($"删除失败: 0x{result:X}");
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
                var (size, count) = await Task.Run(() => ScanDirectory(root, token, cat.FileFilter), token);
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
        if (busy && _statusArea != null) _statusArea.Visibility = Visibility.Visible;
        if (_spinner != null) _spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (_doNotCloseText != null) _doNotCloseText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateCleanButton();
    }

    private void ReportProgress(string? text)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_progressText == null) return;
            bool hasText = text != null;
            _progressText.Text = text ?? "";
            _progressText.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            if (_spinner != null) _spinner.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
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

    // ===== 确认弹窗 =====

    private sealed class ConfirmDialog : Window
    {
        public bool Confirmed { get; private set; }

        public ConfirmDialog(string message, string title, bool recycleBinWarning)
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
            var warningColor = Color.FromRgb(0xE0, 0xA0, 0x30);

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
                Margin = new Thickness(0, 0, 0, recycleBinWarning ? 10 : 22)
            });

            // 回收站警告
            if (recycleBinWarning)
            {
                root.Children.Add(new TextBlock
                {
                    Text = "⚠️ 回收站清空后不可恢复！",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(warningColor),
                    Margin = new Thickness(0, 0, 0, 18)
                });
            }

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
                Content = "确定清理",
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
}
