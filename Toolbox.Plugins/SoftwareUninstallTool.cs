using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Toolbox.Models;
using Toolbox.Services;

namespace Toolbox.Tools;

/// <summary>
/// 软件卸载管理器 —— 展示已安装软件列表，双击执行卸载
/// </summary>
public class SoftwareUninstallTool : ITool
{
    private TextBox? _searchBox;
    private ListView? _softwareList;
    private TextBlock? _statusBlock;
    private TextBlock? _errorBlock;
    private Button? _refreshButton;
    private readonly ObservableCollection<InstalledSoftware> _allSoftware = new();
    private List<InstalledSoftware> _loadedSoftware = new();
    private readonly HashSet<string> _pendingUninstall = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _pollCts;

    private static readonly Color BgDark = Color.FromRgb(0x2D, 0x2D, 0x2D);
    private static readonly Color BgHover = Color.FromRgb(0x3A, 0x3A, 0x3A);
    private static readonly Color Accent = Color.FromRgb(0x76, 0xB5, 0x80);
    private static readonly Color TextPrimary = Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly Color TextSecondary = Color.FromRgb(0x80, 0x80, 0x80);
    private static readonly Color Success = Color.FromRgb(0x20, 0xA0, 0x20);
    private static readonly Color Danger = Color.FromRgb(0xC0, 0x40, 0x40);

    public string Name => "软件卸载管理器";
    public string Description => "查看已安装的软件列表，双击条目即可卸载。需要管理员权限。";
    public string Category => Toolbox.Models.ToolCategory.File;
    public string IconGlyph => "🧹";

    public UIElement CreateContent()
    {
        var root = new StackPanel { Margin = new Thickness(8, 8, 0, 0) };

        // 描述文字
        var desc = new TextBlock
        {
            Text = "电脑上所有已安装的软件，双击即可调用其自带的卸载程序。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 0, 0, 12)
        };

        // 搜索框
        _searchBox = new TextBox
        {
            Height = 32,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _searchBox.TextChanged += OnSearchTextChanged;

        // 错误/信息提示
        _errorBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Danger),
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 加载提示
        var loadingBlock = new TextBlock
        {
            Text = "正在扫描已安装软件...",
            FontSize = 14,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 软件列表（GridView 模式 + 深色主题）
        _softwareList = CreateStyledListView();

        // 状态栏
        _statusBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 0, 0, 4)
        };

        // 刷新按钮
        _refreshButton = new Button
        {
            Content = "🔄 刷新列表",
            FontSize = 13,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 0)
        };
        _refreshButton.Click += (_, _) => LoadSoftwareListAsync();

        root.Children.Add(desc);
        root.Children.Add(_searchBox);
        root.Children.Add(_errorBlock);
        root.Children.Add(loadingBlock);
        root.Children.Add(_softwareList);
        root.Children.Add(_statusBlock);
        root.Children.Add(_refreshButton);

        // 异步加载（不阻塞 UI）
        LoadSoftwareListAsync(loadingBlock);

        return root;
    }

    /// <summary>
    /// 创建深色主题的 ListView + GridView
    /// </summary>
    private ListView CreateStyledListView()
    {
        var listView = new ListView
        {
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 13,
            Background = new SolidColorBrush(BgDark),
            Foreground = new SolidColorBrush(TextPrimary),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F)),
            BorderThickness = new Thickness(1),
        };

        // 清除 ListView 自身的焦点视觉（消灭外缘白色虚线框）
        listView.FocusVisualStyle = null;

        // 自定义 ListView ControlTemplate — 替换默认 Aero2 模板
        // 结构：Border → ScrollViewer(Focusable=false, VScrollBar=Hidden) → ItemsPresenter
        // 无任何 Trigger → 鼠标进入时 ScrollBar 不显形、无边框颜色变化 → 消灭光晕
        var lvTemplate = new ControlTemplate(typeof(ListView));
        var lvBorder = new FrameworkElementFactory(typeof(Border));
        lvBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        lvBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        lvBorder.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        lvBorder.SetValue(Border.SnapsToDevicePixelsProperty, true);

        var lvScrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
        lvScrollViewer.SetValue(ScrollViewer.FocusableProperty, false);
        lvScrollViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        lvScrollViewer.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        lvScrollViewer.SetValue(ScrollViewer.CanContentScrollProperty, false); // 像素级滚动，与 e.Delta/3 兼容

        var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        lvScrollViewer.AppendChild(itemsPresenter);
        lvBorder.AppendChild(lvScrollViewer);
        lvTemplate.VisualTree = lvBorder;
        listView.Template = lvTemplate;

        // 选中项样式：覆盖默认高亮蓝色
        listView.Resources.Add(SystemColors.HighlightBrushKey, new SolidColorBrush(BgHover));
        listView.Resources.Add(SystemColors.HighlightTextBrushKey, new SolidColorBrush(TextPrimary));
        listView.Resources.Add(SystemColors.InactiveSelectionHighlightBrushKey, new SolidColorBrush(BgHover));
        listView.Resources.Add(SystemColors.InactiveSelectionHighlightTextBrushKey, new SolidColorBrush(TextPrimary));

        // ListViewItem 样式：自定义 ControlTemplate + 覆盖默认白色边框和焦点框
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(BgDark)));
        itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        // 自定义 ControlTemplate — 仅含 Border + GridViewRowPresenter，无 FocusVisual 元素
        var itemTemplate = new ControlTemplate(typeof(ListViewItem));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "Bd";
        bd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetValue(Border.SnapsToDevicePixelsProperty, true);

        var presenter = new FrameworkElementFactory(typeof(GridViewRowPresenter));
        presenter.SetBinding(ContentControl.ContentProperty, new Binding("Content") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        presenter.SetBinding(GridViewRowPresenter.ColumnsProperty, new Binding("(GridView.ColumnCollection)") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        presenter.SetBinding(GridViewRowPresenter.SnapsToDevicePixelsProperty, new Binding("SnapsToDevicePixels") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

        bd.AppendChild(presenter);
        itemTemplate.VisualTree = bd;
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));

        // 悬停状态
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(BgHover)));
        hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        itemStyle.Triggers.Add(hoverTrigger);

        // 选中状态
        var selectedTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(BgHover)));
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        itemStyle.Triggers.Add(selectedTrigger);

        // 选中 + 非活动焦点状态
        var multiTrigger = new MultiTrigger();
        multiTrigger.Conditions.Add(new Condition { Property = ListViewItem.IsSelectedProperty, Value = true });
        multiTrigger.Conditions.Add(new Condition { Property = Selector.IsSelectionActiveProperty, Value = false });
        multiTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(BgHover)));
        multiTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        itemStyle.Triggers.Add(multiTrigger);

        listView.Resources.Add(typeof(ListViewItem), itemStyle);

        var gridView = new GridView { AllowsColumnReorder = true };

        // 列头样式
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(BgDark)));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(TextPrimary)));
        headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F))));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));

        // 覆盖默认的蓝色悬浮/按下样式
        var headerTemplate = new ControlTemplate(typeof(GridViewColumnHeader));
        var headerBorder = new FrameworkElementFactory(typeof(Border));
        headerBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        headerBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        headerBorder.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        headerBorder.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
        contentFactory.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
        headerBorder.AppendChild(contentFactory);
        headerTemplate.VisualTree = headerBorder;

        // 悬浮触发器
        var triggerHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        triggerHover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(BgHover)));
        headerTemplate.Triggers.Add(triggerHover);

        headerStyle.Setters.Add(new Setter(Control.TemplateProperty, headerTemplate));
        gridView.ColumnHeaderContainerStyle = headerStyle;

        // ===== 图标列 =====
        var iconColumn = new GridViewColumn { Header = "", Width = 50 };
        var iconFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Image));
        iconFactory.SetBinding(System.Windows.Controls.Image.SourceProperty, new Binding("Icon"));
        iconFactory.SetValue(System.Windows.Controls.Image.WidthProperty, 28.0);
        iconFactory.SetValue(System.Windows.Controls.Image.HeightProperty, 28.0);
        iconFactory.SetValue(System.Windows.Controls.Image.MarginProperty, new Thickness(6, 2, 0, 2));
        iconFactory.SetValue(System.Windows.Controls.Image.StretchProperty, Stretch.Uniform);
        iconFactory.SetValue(System.Windows.Controls.Image.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        iconColumn.CellTemplate = new DataTemplate { VisualTree = iconFactory };
        gridView.Columns.Add(iconColumn);

        gridView.Columns.Add(MakeColumn("名称", 220, "DisplayName"));
        gridView.Columns.Add(MakeColumn("版本", 110, "DisplayVersion"));
        gridView.Columns.Add(MakeColumn("发行商", 150, "Publisher"));
        gridView.Columns.Add(MakeColumn("大小", 85, "SizeDisplay"));
        gridView.Columns.Add(MakeColumn("安装日期", 100, "DateDisplay"));

        listView.View = gridView;
        listView.MouseDoubleClick += OnSoftwareDoubleClick;

        // 修复 1：拦截鼠标滚轮事件，重定向到 ListView 内部 ScrollViewer
        listView.PreviewMouseWheel += OnListPreviewMouseWheel;

        return listView;
    }

    private static GridViewColumn MakeColumn(string header, double width, string bindingPath)
    {
        return new GridViewColumn
        {
            Header = header,
            Width = width,
            DisplayMemberBinding = new Binding(bindingPath)
        };
    }

    // ===== 修复 1：鼠标滚轮 → 重定向到 ListView 内部 ScrollViewer =====

    private void OnListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || _softwareList == null) return;

        var sv = FindVisualChild<ScrollViewer>(_softwareList);
        if (sv == null) return;

        bool canScrollDown = sv.ScrollableHeight > 0;
        bool atTop = e.Delta > 0 && sv.VerticalOffset <= 0;
        bool atBottom = e.Delta < 0 && sv.VerticalOffset >= sv.ScrollableHeight;

        if (canScrollDown && !atTop && !atBottom)
        {
            // 内层可滚动且在范围内 → 在内层滚动
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3);
            e.Handled = true;
            return;
        }

        // 到边界或内容不足一屏 → 转发到外层 ContentScrollViewer
        var outerSv = FindParentScrollViewer(_softwareList);
        if (outerSv != null)
            outerSv.ScrollToVerticalOffset(outerSv.VerticalOffset - e.Delta / 2);
        e.Handled = true;
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject element)
    {
        // 从元素向上遍历视觉树，找到第一个 ScrollViewer 祖先
        var parent = VisualTreeHelper.GetParent(element);
        while (parent != null)
        {
            if (parent is ScrollViewer sv) return sv;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }

    // ===== 加载 =====

    private async void LoadSoftwareListAsync(TextBlock? loadingBlock = null)
    {
        if (loadingBlock != null)
            loadingBlock.Visibility = Visibility.Visible;

        _errorBlock!.Text = "";
        _errorBlock.Foreground = new SolidColorBrush(Danger);

        try
        {
            var softwareList = await Task.Run(() => SoftwareUninstallService.GetInstalledSoftware());

            foreach (var sw in softwareList)
            {
                sw.Icon = SoftwareUninstallService.ExtractIcon(sw.DisplayIcon);
            }

            _loadedSoftware = softwareList;
            _allSoftware.Clear();
            foreach (var sw in softwareList)
                _allSoftware.Add(sw);

            _softwareList!.ItemsSource = _allSoftware;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _errorBlock!.Text = $"⚠️ 读取软件列表失败：{ex.Message}";
        }
        finally
        {
            if (loadingBlock != null)
                loadingBlock.Visibility = Visibility.Collapsed;
        }
    }

    // ===== 搜索 =====

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = _searchBox!.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(filter))
        {
            _softwareList!.ItemsSource = _allSoftware;
        }
        else
        {
            var filtered = _loadedSoftware
                .Where(sw => sw.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _softwareList!.ItemsSource = new ObservableCollection<InstalledSoftware>(filtered);
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_statusBlock == null || _softwareList == null) return;

        int total = _loadedSoftware.Count;
        int showing = _softwareList.ItemsSource switch
        {
            ICollection<InstalledSoftware> col => col.Count,
            _ => total
        };

        _statusBlock.Text = showing == total
            ? $"共 {total} 个已安装软件"
            : $"共 {total} 个，已过滤显示 {showing} 个";
    }

    // ===== 修复 3：卸载 → 进程跟踪 + 注册表轮询确认 =====

    private async void OnSoftwareDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_softwareList!.SelectedItem is not InstalledSoftware software) return;

        var result = MessageBox.Show(
            $"确定要卸载「{software.DisplayName}」吗？\n\n"
            + "将调用其自带的卸载程序，可能需要管理员权限。",
            "确认卸载",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // 标记为待卸载（UI 上显示灰色表示正在处理）
        _pendingUninstall.Add(software.DisplayName);

        bool launched = SoftwareUninstallService.UninstallSoftware(software, out var launchError);

        if (!launched)
        {
            _pendingUninstall.Remove(software.DisplayName);

            // 区分 UAC 被拒绝 vs 其他错误
            if (launchError == "UAC_CANCELLED")
            {
                _errorBlock!.Text = $"⚠️ 卸载被取消：{software.DisplayName}（用户拒绝了 UAC 提权）";
            }
            else
            {
                _errorBlock!.Text = $"⚠️ 启动卸载失败：{launchError}";
            }
            _errorBlock.Foreground = new SolidColorBrush(Danger);
            return;
        }

        _errorBlock!.Text = $"✅ 已启动卸载程序：{software.DisplayName}，正在等待卸载完成...";
        _errorBlock.Foreground = new SolidColorBrush(Success);

        // 后台轮询注册表，确认软件是否真的被卸载
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        try
        {
            await PollUntilUninstalled(software, token);
        }
        catch (OperationCanceledException) { /* 刷新或新卸载操作取消了轮询 */ }
    }

    private async Task PollUntilUninstalled(InstalledSoftware software, CancellationToken token)
    {
        // 等待 2 秒让卸载程序启动
        await Task.Delay(2000, token);

        // 每 3 秒轮询一次，最多 120 秒
        for (int i = 0; i < 40; i++)
        {
            token.ThrowIfCancellationRequested();

            bool stillExists = await Task.Run(() =>
                SoftwareUninstallService.IsSoftwareStillInstalled(software.DisplayName), token);

            if (!stillExists)
            {
                // 确认已从注册表消失 → 从列表中移除
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _allSoftware.Remove(software);
                    _loadedSoftware.Remove(software);
                    _pendingUninstall.Remove(software.DisplayName);
                    _errorBlock!.Text = $"✅ 卸载成功：{software.DisplayName}";
                    _errorBlock.Foreground = new SolidColorBrush(Success);
                    UpdateStatus();
                });
                return;
            }

            await Task.Delay(3000, token);
        }

        // 超时：软件仍在注册表中 → 警告但不移除
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _pendingUninstall.Remove(software.DisplayName);
            _errorBlock!.Text = $"⚠️ 卸载可能未完成：{software.DisplayName}（超过等待时间，请点击刷新确认）";
            _errorBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30));
        });
    }
}