using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Toolbox.Helpers;

namespace Toolbox;

/// <summary>
/// 主窗口代码后置 —— 初始化 Win11 外观特性 + 标题栏按钮事件
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 窗口加载后，通过 P/Invoke 启用 Win11 圆角和 Mica 材质
        Loaded += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32Helper.EnableRoundedCorners(hwnd);         // 1. 圆角
            Win32Helper.EnableMicaBackdrop(hwnd);           // 2. Mica 材质
            Win32Helper.ExtendFrameIntoClientArea(hwnd);    // 3. 扩展帧到标题栏

            // 更新四角落遮盖（等窗口实际尺寸确定后）
            Dispatcher.BeginInvoke(new Action(() => UpdateCornerMask()),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // 初始化分组高度（展开的为 Auto，折叠的为 0）
            Dispatcher.BeginInvoke(new Action(InitGroupHeights),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // 初始化导航高亮位置（等布局完成后）
            Dispatcher.BeginInvoke(new Action(InitHighlight),
                System.Windows.Threading.DispatcherPriority.Loaded);
        };

        // 窗口状态变更时更新最大化/还原图标
        StateChanged += (_, _) => UpdateMaximizeIcon();
    }

    /// <summary>更新四角遮盖形状（全矩形 减 内圆角矩形 = 四个角落区域）</summary>
    private void UpdateCornerMask()
    {
        double r = 8; // 圆角半径，与 WindowChrome.CornerRadius 和 Border.CornerRadius 保持一致

        var outerRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var innerRect = new RectangleGeometry(new Rect(r, r, ActualWidth - 2 * r, ActualHeight - 2 * r), r, r);

        CornerMask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerRect);
    }

    /// <summary>窗口大小变更时更新四角色块形状</summary>
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCornerMask();
    }

    // --- 导航高亮移动动画 ---

    /// <summary>初始化导航高亮到第一个选中项（跨分组遍历）</summary>
    private void InitHighlight()
    {
        // 遍历 VisibleGroups 中展开组的子 ItemsControl，找首个可见的 Tool Border
        foreach (var groupBorder in FindVisualChildren<Border>(NavContainer))
        {
            if (groupBorder.DataContext is Models.ITool && groupBorder.Visibility == Visibility.Visible)
            {
                PositionHighlight(groupBorder);
                return;
            }
        }
    }

    /// <summary>导航项点击事件——选中工具 + 移动高亮</summary>
    private void NavItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.ITool tool)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.SelectedTool = tool;
            }
            PositionHighlight(element);
        }
    }

    /// <summary>将高亮指示器动画移动到指定元素的位置（跨分组定位）</summary>
    private void PositionHighlight(FrameworkElement itemElement)
    {
        // 计算 item 相对于 NavContainer 的位置
        var transform = itemElement.TransformToAncestor(NavContainer);
        var position = transform.Transform(new Point(0, 0));
        double top = position.Y; // 精确对齐，无需补偿

        var targetMargin = new Thickness(10, top, 12, 0);

        if (HighlightBar.Visibility == Visibility.Collapsed)
        {
            // 清除残留的旧动画，防止 Visibility 恢复后旧动画覆盖新 Margin
            HighlightBar.BeginAnimation(Border.MarginProperty, null);
            // 首次显示，直接定位（无动画）
            HighlightBar.Margin = targetMargin;
            HighlightBar.Visibility = Visibility.Visible;
        }
        else
        {
            // 已有位置：带动画平滑过渡
            var anim = new ThicknessAnimation
            {
                To = targetMargin,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            HighlightBar.BeginAnimation(Border.MarginProperty, anim);
        }
    }

    /// <summary>分类标题头点击——切换展开/折叠（带动画）</summary>
    private void GroupHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.ToolGroup group)
        {
            bool nowExpanded = !group.IsExpanded;
            group.IsExpanded = nowExpanded;

            // 找到动画容器 Border（StackPanel → Children[1]）
            Border? animContainer = null;
            if (VisualTreeHelper.GetParent(element) is StackPanel sp
                && sp.Children.Count > 1
                && sp.Children[1] is Border b
                && b.Tag is string tag && tag == "GroupAnimContainer")
            {
                animContainer = b;
            }

            // 动画完成回调：布局稳定后重定位高亮（解决展开上方组导致高亮错位）
            Action onAnimCompleted = () => Dispatcher.BeginInvoke(
                new Action(ScheduleHighlightReposition),
                System.Windows.Threading.DispatcherPriority.Background);

            // 执行 Height 动画（完成后回调重定位高亮）
            if (animContainer != null)
                AnimateGroupHeight(animContainer, nowExpanded, onAnimCompleted);
            else
                onAnimCompleted();

            // 折叠时：若选中工具在当前组，立即切换到下一个可见组
            if (!nowExpanded && DataContext is ViewModels.MainViewModel vm
                && vm.SelectedTool != null && group.Tools.Contains(vm.SelectedTool))
            {
                var firstVisible = FindFirstVisibleTool();
                if (firstVisible != null)
                {
                    vm.SelectedTool = firstVisible;
                    var t = FindToolBorderByTool(firstVisible);
                    if (t != null) PositionHighlight(t);
                }
                else
                {
                    HighlightBar.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    /// <summary>分组容器 Height 动画——展开/折叠（带有推入/收回效果）</summary>
    private static void AnimateGroupHeight(Border container, bool expand, Action? onCompleted = null)
    {
        container.BeginAnimation(FrameworkElement.HeightProperty, null);

        if (expand)
        {
            // 展开：先测量实际高度，再从 0 动画到目标
            container.Height = double.NaN; // Auto
            container.UpdateLayout();
            double targetH = container.ActualHeight;
            if (targetH <= 0) { container.Height = double.NaN; onCompleted?.Invoke(); return; }

            container.Height = 0;
            var a = new DoubleAnimation(0, targetH, TimeSpan.FromMilliseconds(200));
            a.Completed += (_, _) =>
            {
                container.BeginAnimation(FrameworkElement.HeightProperty, null);
                container.Height = double.NaN;
                onCompleted?.Invoke();
            };
            container.BeginAnimation(FrameworkElement.HeightProperty, a);
        }
        else
        {
            // 折叠：从当前高度动画到 0
            double curH = container.ActualHeight;
            if (curH <= 0) { container.Height = 0; onCompleted?.Invoke(); return; }

            var a = new DoubleAnimation(curH, 0, TimeSpan.FromMilliseconds(200));
            a.Completed += (_, _) =>
            {
                container.BeginAnimation(FrameworkElement.HeightProperty, null);
                container.Height = 0;
                onCompleted?.Invoke();
            };
            container.BeginAnimation(FrameworkElement.HeightProperty, a);
        }
    }

    /// <summary>在下一个布局周期调度高亮重定位</summary>
    private void ScheduleHighlightReposition()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DataContext is ViewModels.MainViewModel v && v.SelectedTool != null)
            {
                // 检查选中工具所在分组是否已折叠→若折叠则隐藏高亮
                var toolGroup = v.VisibleGroups.FirstOrDefault(g => g.Tools.Contains(v.SelectedTool));
                if (toolGroup != null && !toolGroup.IsExpanded)
                {
                    HighlightBar.Visibility = Visibility.Collapsed;
                    return;
                }

                var t = FindToolBorderByTool(v.SelectedTool);
                if (t != null && t.IsVisible)
                    PositionHighlight(t);
                else
                    HighlightBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                HighlightBar.Visibility = Visibility.Collapsed;
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>初始化所有分组的 Height（展开的设为 Auto，折叠的保持 0）</summary>
    private void InitGroupHeights()
    {
        foreach (var child in FindVisualChildren<Border>(NavContainer))
        {
            if (child.Tag is string tag && tag == "GroupAnimContainer"
                && child.DataContext is Models.ToolGroup g)
            {
                // 清除动画、设初始高度
                child.BeginAnimation(FrameworkElement.HeightProperty, null);
                child.Height = g.IsExpanded ? double.NaN : 0;
            }
        }
    }

    /// <summary>分类头鼠标进入——切换图标为箭头</summary>
    private void GroupHeader_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.ToolGroup group)
            group.IsHovered = true;
    }

    /// <summary>分类头鼠标离开——切换图标为文件夹</summary>
    private void GroupHeader_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.ToolGroup group)
            group.IsHovered = false;
    }

    /// <summary>搜索框按下 Enter 键——跳转到第一个匹配工具</summary>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (DataContext is ViewModels.MainViewModel vm && vm.VisibleGroups.Count > 0)
        {
            var firstGroup = vm.VisibleGroups[0];
            if (firstGroup.Tools.Count > 0)
            {
                vm.SelectedTool = firstGroup.Tools[0];
                var target = FindToolBorderByTool(firstGroup.Tools[0]);
                if (target != null)
                    PositionHighlight(target);
            }
        }
    }

    /// <summary>搜索框鼠标滚轮转发到导航滚动条（避免文本滚动取代页面滚动）</summary>
    private void SearchBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent
        };
        NavScrollViewer.RaiseEvent(args);
    }

    /// <summary>通过 ITool 引用在视觉树中查找对应的 Border 元素</summary>
    private Border? FindToolBorderByTool(Models.ITool tool)
    {
        foreach (var border in FindVisualChildren<Border>(NavContainer))
        {
            if (border.DataContext == tool)
                return border;
        }
        return null;
    }

    /// <summary>在所有 VisibleGroups 中找第一个可见的工具</summary>
    private static Models.ITool? FindFirstVisibleTool()
    {
        // 通过 Application.Current.MainWindow.DataContext 访问 ViewModel
        if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            foreach (var group in vm.VisibleGroups)
            {
                if (group.IsExpanded && group.Tools.Count > 0)
                    return group.Tools[0];
            }
        }
        return null;
    }

    /// <summary>递归查找视觉树中指定类型的所有子元素</summary>
    private static List<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var results = new List<T>();
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                results.Add(typedChild);
            results.AddRange(FindVisualChildren<T>(child));
        }
        return results;
    }

    /// <summary>自定义标题栏拖拽移动（自动跳过按钮点击以免干扰 DragMove）</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Bug 修复: 沿视觉树检查点击是否来源于某个按钮——若是则跳过拖拽
        if (IsDescendantOfButton(e.OriginalSource as DependencyObject))
            return;

        if (e.ClickCount == 2)
        {
            // 双击最大化/还原
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    /// <summary>沿视觉树向上遍历，检查元素是否为 Button 的子孙节点</summary>
    private static bool IsDescendantOfButton(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    // --- 标题栏按钮事件 ---

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>切换最大化/还原图标</summary>
    private void UpdateMaximizeIcon()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        MaximizePath.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
        RestorePath.Visibility = isMaximized ? Visibility.Visible : Visibility.Collapsed;
    }
}