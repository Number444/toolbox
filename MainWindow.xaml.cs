using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Toolbox.Helpers;
using Toolbox.Core.Helpers;
using Toolbox.Core.Models;
using static Toolbox.Core.Helpers.Win32Native;
using Toolbox.Core.Services;
using Toolbox.Plugins.Services;

namespace Toolbox;

/// <summary>
/// 主窗口代码后置 —— 初始化 Win11 外观特性 + 标题栏按钮事件
/// </summary>
public partial class MainWindow : Window
{
    private bool _isShuttingDown;
    private Models.ITool? _savedSelectedTool;

    public MainWindow()
    {
        InitializeComponent();

        // 窗口加载后，通过 P/Invoke 启用 Win11 圆角和 Mica 材质
        Loaded += (_, _) =>
        {
            // 2026-08-03（审查 P1-7）：DWM/Win32 链独立 try-catch——任一步失败
            // （DWM 未运行/远程桌面/低版本）不崩应用，且**不再连带吞掉**后续的事件
            // 订阅与悬浮窗自启（原实现：前半段失败 = 后半段功能静默丢失）。
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                Win32Helper.EnableRoundedCorners(hwnd);         // 1. 圆角
                EnableAcrylicBackdrop();                        // 2. Acrylic 毛玻璃（替代 Mica）
                Win32Helper.EnableDarkMode(hwnd);               // 3. 沉浸式深色模式
                Win32Helper.ExtendFrameIntoClientArea(hwnd);    // 4. 扩展帧到标题栏

                // 6. 拦截 WM_NCCALCSIZE，抹掉 WPF 1px GDI NC 边界
                var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                source?.AddHook(Win32Helper.WndProc);

                // 7. 强制 WPF DirectX 交换链背景透明，彻底消除白色底漆
                if (source?.CompositionTarget is System.Windows.Interop.HwndTarget hwndTarget)
                {
                    hwndTarget.BackgroundColor = Colors.Transparent;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] DWM/Win32 初始化失败: {ex.Message}");
            }

            // ── 非 DWM 初始化（原在同一个 try 内，现独立执行，任一步失败不互拖）──

            // 更新四角落遮盖（等窗口实际尺寸确定后）
            Dispatcher.BeginInvoke(new Action(() => UpdateCornerMask()),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // 初始化分组高度（展开的为 Auto，折叠的为 0）
            Dispatcher.BeginInvoke(new Action(InitGroupHeights),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // 初始化导航高亮位置（等布局完成后）
            Dispatcher.BeginInvoke(new Action(InitHighlight),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // 设置页返回事件
            SettingsViewControl.BackRequested += (_, _) => ExitSettingsView();

            // 插件经 Core 中转的导航请求（如首页仪表盘卡片点击跳转工具）
            Models.ToolNavigation.NavigateRequested += OnToolNavigateRequested;

            // 启动自检（审查 P1-4）：工具发现失败时不再静默空白
            if (DataContext is ViewModels.MainViewModel vm && vm.Tools.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "工具加载失败：未发现任何可用工具，请检查安装完整性后重启。",
                    $"{App.WindowTitle} 警告",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            // 启动时自动打开悬浮窗
            if (AppSettings.Instance.AutoOpenFloatWindow)
            {
                var savedMode = AppSettings.Instance.MusicFloatSizeMode;
                var mode = savedMode == "Compact"
                    ? FloatSizeMode.Compact
                    : FloatSizeMode.Large;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 加载悬浮窗独立配置
                    AudioflowSettings.Instance.Load();
                    var mgr = MusicFloatControllerHost.Current;
                    if (mgr == null) return; // 控制器未注册（插件加载失败）时静默跳过
                    mgr.Show(mode, AudioflowSettings.Instance.FloatWindowBlurEnabled);
                    mgr.SetWindowLocked(AudioflowSettings.Instance.LockFloatWindow);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        };

        // 窗口状态变更时更新最大化/还原图标
        StateChanged += (_, _) => UpdateMaximizeIcon();

        // 鼠标跟随呼吸光晕
        InitHalo();

        // 搜索过滤会重建导航列表（VisibleGroups 变更），缓存的 工具→Border 映射随之失效
        if (DataContext is ViewModels.MainViewModel navVm)
            navVm.VisibleGroups.CollectionChanged += (_, _) => _toolBorders.Clear();
    }

    /// <summary>更新四角遮盖形状（全矩形 减 内圆角矩形 = 四个角落区域）</summary>
    private void UpdateCornerMask()
    {
        double r = 8; // 圆角半径，与 WindowChrome.CornerRadius 和 Border.CornerRadius 保持一致

        // 外矩形（尖角）和内矩形（圆角，填充整个窗口）的差集 = 仅四角，不含四边
        var outerRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var innerRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), r, r);

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

            // 如果当前在设置页，自动退出返回工具箱
            if (SettingsLayer.Visibility == Visibility.Visible)
                ExitSettingsView();
        }
    }

    /// <summary>处理插件经 ToolNavigation 中转的导航请求（按工具名切换 + 高亮跟随）</summary>
    private void OnToolNavigateRequested(string toolName)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;

        var tool = vm.AllGroups.SelectMany(g => g.Tools)
            .FirstOrDefault(t => t.Name == toolName);
        if (tool == null) return;

        vm.SelectedTool = tool;
        var target = FindToolBorderByTool(tool);
        if (target != null) PositionHighlight(target);

        if (SettingsLayer.Visibility == Visibility.Visible)
            ExitSettingsView();
    }

    /// <summary>将高亮指示器动画移动到指定元素的位置（跨分组定位）</summary>
    private void PositionHighlight(FrameworkElement itemElement)
    {
        // 计算 item 相对于 NavContainer 的位置
        Point position;
        try
        {
            var transform = itemElement.TransformToAncestor(NavContainer);
            position = transform.Transform(new Point(0, 0));
        }
        catch (Exception ex)
        {
            // 元素尚未连接/已脱离视觉树时静默放弃本次定位
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 高亮定位失败: {ex.Message}");
            return;
        }
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

    /// <summary>工具→导航 Border 缓存（O(1) 查找；VisibleGroups 变更时清空重建）</summary>
    private readonly Dictionary<Models.ITool, Border> _toolBorders = new();

    /// <summary>通过 ITool 引用查找对应的导航 Border 元素（缓存优先，未命中全量扫描重建）</summary>
    private Border? FindToolBorderByTool(Models.ITool tool)
    {
        if (_toolBorders.TryGetValue(tool, out var cached) && cached.DataContext == tool)
            return cached;

        // 缓存未命中：全量扫描视觉树重建映射
        _toolBorders.Clear();
        foreach (var border in FindVisualChildren<Border>(NavContainer))
        {
            if (border.DataContext is Models.ITool t)
                _toolBorders[t] = border;
        }
        return _toolBorders.TryGetValue(tool, out var found) ? found : null;
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

    // --- 设置页 ---

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        EnterSettingsView();
    }

    private void EnterSettingsView()
    {
        // 保存当前选中工具引用以便返回时恢复高亮
        _savedSelectedTool = (DataContext as ViewModels.MainViewModel)?.SelectedTool;

        // 隐藏内容区，显示设置层
        ContentScrollViewer.Visibility = Visibility.Collapsed;
        SettingsLayer.Visibility = Visibility.Visible;
        HighlightBar.Visibility = Visibility.Collapsed;
    }

    private void ExitSettingsView()
    {
        SettingsLayer.Visibility = Visibility.Collapsed;
        ContentScrollViewer.Visibility = Visibility.Visible;

        // 恢复高亮（如果有选中工具）
        if (_savedSelectedTool != null
            && DataContext is ViewModels.MainViewModel vm
            && vm.VisibleGroups.Any(g => g.IsExpanded && g.Tools.Contains(_savedSelectedTool)))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var target = FindToolBorderByTool(_savedSelectedTool);
                if (target != null)
                    PositionHighlight(target);
                else
                    ScheduleHighlightReposition();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    // --- 窗口关闭/退出 ---

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isShuttingDown)
        {
            base.OnClosing(e);
            return;
        }

        if (AppSettings.Instance.MinimizeOnClose)
        {
            e.Cancel = true;
            bool trayOk = SystemTrayHelper.Instance.Show(
                tooltip: $"{App.WindowTitle} - \u70B9\u51FB\u6062\u590D",
                onDoubleClick: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowInTaskbar = true;
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                        SystemTrayHelper.Instance.Hide();
                    });
                },
                onExitClick: () =>
                {
                    Dispatcher.Invoke(() => { Shutdown(); });
                });
            if (trayOk)
            {
                // \u6258\u76D8\u53EF\u7528\u624D\u9690\u85CF\u7A97\u53E3\uFF1B\u5426\u5219\u4FDD\u6301\u4EFB\u52A1\u680F\u53EF\u89C1\uFF08\u907F\u514D\u5E94\u7528\u4E0D\u53EF\u8FBE\uFF0C
                // 2026-08-03 \u5BA1\u67E5\u53D1\u73B0\uFF1ANIM_ADD \u5931\u8D25 + \u9690\u85CF\u7A97\u53E3 + \u5355\u5B9E\u4F8B\u4E92\u65A5\u9501 = \u5E94\u7528\u84B8\u53D1\uFF09
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[MainWindow] \u6258\u76D8\u56FE\u6807\u521B\u5EFA\u5931\u8D25\uFF0C\u4FDD\u6301\u7A97\u53E3\u53EF\u89C1");
            }
            return;
        }

        base.OnClosing(e);
    }

    public void Shutdown()
    {
        _isShuttingDown = true;
        AppSettings.Instance.Save();

        // 2026-08-03（审查 P1-7）：退出路径绝不允许被业务异常阻断——插件 Close()
        // 抛异常曾导致 Application.Shutdown 永不执行（点退出无反应）
        try
        {
            if (Helpers.SystemTrayHelper.Instance.IsVisible)
                Helpers.SystemTrayHelper.Instance.Hide();

            // 关闭悬浮窗释放 SMTC 监听
            MusicFloatControllerHost.Current?.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 退出清理失败（继续退出）: {ex.Message}");
        }

        Application.Current.Shutdown();
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

    // ═══════════════════════════════════════════════════════════
    // 鼠标跟随呼吸光晕
    // ═══════════════════════════════════════════════════════════

    private Point _haloTarget;          // 鼠标目标位置
    private Point _haloPos;             // 光晕当前位置（插值滞后跟随）
    private double _haloOpacity;        // 当前淡入淡出系数
    private bool _haloInitialized;      // 首次移动时直接吸附，避免从角落滑入
    private bool _glowTargetsDirty = true;   // 边缘发光目标清单待刷新
    private DateTime _glowTargetsLastRebuild = DateTime.MinValue;

    /// <summary>初始化鼠标光晕：位置插值跟随 + 淡入淡出（呼吸缩放动画在 XAML 中）</summary>
    private void InitHalo()
    {
        // 布局变化（窗口缩放/工具切换/设置层显隐/分组展开折叠）时标记发光目标待刷新
        GlowLayer.LayoutUpdated += (_, _) => _glowTargetsDirty = true;

        // 工具切换：立即销毁原界面全部发光（0ms 残留），并在下一帧重建目标清单
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ViewModels.MainViewModel.SelectedTool))
                    RequestGlowRebuild();
            };
        }

        // 设置层显隐切换：同上，立即销毁 + 下一帧重建
        SettingsLayer.IsVisibleChanged += (_, _) => RequestGlowRebuild();

        // 每帧用 Win32 GetCursorPos 轮询光标（原始屏幕坐标，与消息投递和命中测试
        // 完全无关——WPF 的 Mouse.GetPosition 由输入系统维护，鼠标悬停在
        // WindowChrome CaptionHeight 划出的 HTCAPTION 非客户区（顶栏空白处）时
        // 不再更新，会导致光晕误判为"鼠标已离开"而淡出）。
        // 插值滞后跟随（0.12 ≈ 轻微延迟拖尾），透明度平滑淡入淡出
        CompositionTarget.Rendering += (_, _) =>
        {
            // 窗口尚未完成初始化（视觉未连接到 PresentationSource）时跳过本帧
            if (PresentationSource.FromVisual(HaloLayer) == null) return;
            if (!GetCursorPos(out var cursor)) return;

            var pt = HaloLayer.PointFromScreen(new Point(cursor.X, cursor.Y));
            bool inside = IsActive
                && pt.X >= 0 && pt.Y >= 0
                && pt.X <= HaloLayer.ActualWidth && pt.Y <= HaloLayer.ActualHeight;

            if (inside)
            {
                _haloTarget = pt;
                if (!_haloInitialized)
                {
                    _haloPos = pt;
                    _haloInitialized = true;
                }
            }

            _haloPos.X += (_haloTarget.X - _haloPos.X) * 0.12;
            _haloPos.Y += (_haloTarget.Y - _haloPos.Y) * 0.12;
            // 鼠标光晕开关：关闭时按"鼠标不在窗口"处理，平滑淡出后保持熄灭
            bool haloOn = AppSettings.Instance.MouseHaloEnabled;
            _haloOpacity += (((inside && haloOn) ? 1.0 : 0.0) - _haloOpacity) * 0.08;

            HaloTranslate.X = _haloPos.X - HaloEllipse.Width / 2;
            HaloTranslate.Y = _haloPos.Y - HaloEllipse.Height / 2;
            HaloEllipse.Opacity = _haloOpacity;

            // 控件边缘发光开关：关闭时跳过目标重建，并按"鼠标不在窗口"瞬时熄灭
            bool glowOn = AppSettings.Instance.ControlGlowEnabled;

            // 边缘发光目标清单：节流重建（250ms），避免布局动画期间反复遍历视觉树
            if (glowOn && _glowTargetsDirty &&
                (DateTime.UtcNow - _glowTargetsLastRebuild).TotalMilliseconds >= 250)
            {
                GlowLayer.RebuildTargets(this);
                _glowTargetsLastRebuild = DateTime.UtcNow;
                _glowTargetsDirty = false;
            }
            // 传原始光标位置（非插值滞后的 _haloPos），保证移出控件瞬时熄灭（0ms）
            GlowLayer.UpdateCursor(pt, inside && glowOn);
        };
    }

    /// <summary>界面/工具切换时立即销毁全部发光（0ms 残留），下一帧重建目标清单</summary>
    private void RequestGlowRebuild()
    {
        GlowLayer.ClearTargets();
        _glowTargetsDirty = true;
        _glowTargetsLastRebuild = DateTime.MinValue;
    }

    // ═══════════════════════════════════════════════════════════
    // DWM Acrylic 毛玻璃效果（复用 Toolbox.Core 的 DwmHelper 封装）
    // ═══════════════════════════════════════════════════════════

    /// <summary>启用 Acrylic 毛玻璃背景（保持原版本门槛：Win11 Build≥22000 优先尝试官方背景效果）</summary>
    private void EnableAcrylicBackdrop()
    {
        // Win11 (Build 22000+)：尝试官方 DWMWA_SYSTEMBACKDROP_TYPE（需 22H2+，失败自动回落）
        if (Environment.OSVersion.Version.Build >= 22000
            && DwmHelper.SetBackdrop(this, BackdropType.Acrylic))
            return;

        // Win10 / 低版本 Win11 / SetBackdrop 失败：回落 SetWindowCompositionAttribute 方案
        DwmHelper.EnableAcrylicBlur(this, 0x661A1A1A);
    }
}