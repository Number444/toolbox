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
    private bool _selfCheckShown;
    private bool _backgroundServicesInitialized;

    /// <summary>
    /// 静默启动（命令行含 --autostart 且 AutoStartSilent 开启）：不显示主界面，
    /// 后台驻留托盘 + 悬浮窗照常。由 App.OnStartup 在构造后设置。
    /// </summary>
    public bool StartSilent { get; set; }

    /// <summary>窗口曾进入后台（最小化/托盘隐藏），下次激活时播放切回动画</summary>
    private bool _wasBackground;

    public MainWindow()
    {
        InitializeComponent();

        // 事件订阅（不依赖窗口显示——静默启动不触发 Loaded，也必须生效）
        SettingsViewControl.BackRequested += (_, _) => ExitSettingsView();
        Models.ToolNavigation.NavigateRequested += OnToolNavigateRequested;

        // 窗口状态变更时更新最大化/还原图标
        StateChanged += (_, _) => UpdateMaximizeIcon();

        // 切回前台动画：最小化时置位后台标志；激活时播放（仅"不可见→可见"触发，
        // 普通失焦再聚焦不触发——Deactivated 不监听，避免点悬浮窗/弹窗后误动画）
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) _wasBackground = true;
        };
        Activated += (_, _) =>
        {
            if (_wasBackground)
            {
                _wasBackground = false;
                PlayReturnAnimations();
            }
        };

        // 鼠标跟随呼吸光晕
        InitHalo();

        // 方案二（试效果）：选中高亮条边缘绿色细线呼吸闪烁
        StartHighlightBorderBreath();

        // 搜索过滤会重建导航列表（VisibleGroups 变更）：清缓存 + 调度导航状态刷新
        // （重建后新模板实例：展开组 Height 归 0、箭头角度归零、选中 Tag 丢失、高亮位置漂移，
        //   由 RunNavigationRefresh 一并恢复，见下方方法定义）
        if (DataContext is ViewModels.MainViewModel navVm)
        {
            navVm.VisibleGroups.CollectionChanged += (_, _) =>
            {
                _toolBorders.Clear();
                _groupHeights.Clear();
                _navRefreshPending = true;
                Dispatcher.BeginInvoke(new Action(RunNavigationRefresh),
                    System.Windows.Threading.DispatcherPriority.Background);
            };

            // 工具切换（含搜索选中）时内容区滚动回顶：
            // 否则从长工具底部切到另一长工具，新内容从旧滚动位置（中间）开始显示
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.MainViewModel.SelectedTool))
                    ContentScrollViewer.ScrollToVerticalOffset(0);
            };
        }

        // 窗口加载后：Win11 外观 + 首次显示才需要的 UI 初始化
        // （静默启动不 Show 不触发 Loaded，首次从托盘恢复显示时自然执行）
        Loaded += (_, _) => OnWindowLoaded();
    }

    /// <summary>
    /// 窗口首次加载（= 首次显示）：Win11 外观与 UI 布局初始化。
    /// 2026-08-03（审查 P1-7）：DWM/Win32 链独立 try-catch——任一步失败
    /// （DWM 未运行/远程桌面/低版本）不崩应用，且**不再连带吞掉**后续初始化
    /// （原实现：前半段失败 = 后半段功能静默丢失）。
    /// </summary>
    private void OnWindowLoaded()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32Helper.EnableRoundedCorners(hwnd);         // 1. 圆角
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

        // 2. Acrylic 毛玻璃：延迟到 WPF 首帧提交后启用——若在 Loaded 同步启用，DWM 生效会早于
        // 首帧内容提交（渲染线程正忙初始化），窗口短暂显示无内容的纯毛玻璃（毫秒级闪过）。
        // Rendering 首次触发后取消订阅，再以 Background 优先级执行（当前帧提交之后）
        bool firstRenderHandled = false;
        void EnableAcrylicAfterFirstFrame(object? _, EventArgs __)
        {
            if (firstRenderHandled) return;
            firstRenderHandled = true;
            CompositionTarget.Rendering -= EnableAcrylicAfterFirstFrame;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { EnableAcrylicBackdrop(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Acrylic 初始化失败: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        CompositionTarget.Rendering += EnableAcrylicAfterFirstFrame;

        // ── 非 DWM 初始化（原在同一个 try 内，现独立执行，任一步失败不互拖）──

        // 更新四角落遮盖（等窗口实际尺寸确定后）
        Dispatcher.BeginInvoke(new Action(() => UpdateCornerMask()),
            System.Windows.Threading.DispatcherPriority.Loaded);

        // 初始化分组高度（展开的为 Auto，折叠的为 0）
        Dispatcher.BeginInvoke(new Action(InitGroupHeights),
            System.Windows.Threading.DispatcherPriority.Loaded);

        // 初始化分组箭头角度（展开 = 90° ▾ / 折叠 = 0° ▸；点击旋转动画只在切换时触发，初始态需同步）
        Dispatcher.BeginInvoke(new Action(InitGroupArrows),
            System.Windows.Threading.DispatcherPriority.Loaded);

        // 初始化导航高亮位置（等布局完成后）
        Dispatcher.BeginInvoke(new Action(InitHighlight),
            System.Windows.Threading.DispatcherPriority.Loaded);

        // 启动自检（审查 P1-4）：工具发现失败时不再静默空白。
        // 静默启动从托盘恢复显示也会触发 Loaded——防重复弹窗
        if (!_selfCheckShown)
        {
            _selfCheckShown = true;
            if (DataContext is ViewModels.MainViewModel vm && vm.Tools.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "工具加载失败：未发现任何可用工具，请检查安装完整性后重启。",
                    $"{App.WindowTitle} 警告",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        // 后台服务（悬浮窗自启；静默启动由 App.OnStartup 直接调用，此处幂等防重入）
        InitializeBackgroundServices();

        // 遮罩内容（图标 + 文字容器）：套用工具切换的淡入上滑动画（2s CubicEase EaseOut）
        var ease = EaseOut();
        MaskContent.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(2000)) { EasingFunction = ease });
        MaskContentTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(2000)) { EasingFunction = ease });

        // 启动遮罩（测试）：纯黑停留 2s，再 500ms 淡出。黑色与 DWM 首帧空窗帧同色（无缝），
        // Acrylic 已在幕后就绪（延迟方案），淡出时毛玻璃透出
        var maskFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            BeginTime = TimeSpan.FromMilliseconds(2000)   // 先停留 2s 测试
        };
        maskFade.Completed += (_, _) => StartupMask.Visibility = Visibility.Collapsed;
        StartupMask.BeginAnimation(UIElement.OpacityProperty, maskFade);
    }

    /// <summary>
    /// 后台服务初始化（不依赖窗口显示）：悬浮窗自启 + 静默模式的托盘驻留。
    /// 正常启动由 OnWindowLoaded 调用；静默启动由 App.OnStartup 直接调用。
    /// </summary>
    public void InitializeBackgroundServices()
    {
        if (_backgroundServicesInitialized) return;
        _backgroundServicesInitialized = true;

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

        // 静默模式：托盘是唯一入口，必须创建成功；失败回退显示主窗口（避免应用不可达）
        if (StartSilent)
        {
            bool trayOk = SystemTrayHelper.Instance.Show(
                tooltip: $"{App.WindowTitle} - 单击恢复主窗口",
                onDoubleClick: () => Dispatcher.Invoke(RestoreFromTray),
                onExitClick: () => Dispatcher.Invoke(Shutdown));
            if (!trayOk)
            {
                System.Diagnostics.Debug.WriteLine("[MainWindow] 静默启动托盘创建失败，回退显示主窗口");
                StartSilent = false;
                Show();
            }
            else
            {
                _wasBackground = true;   // 静默驻留：恢复显示时播放切回动画
            }
        }
    }

    /// <summary>从托盘恢复显示主窗口（静默驻留唤起与最小化到托盘恢复共用）</summary>
    public void RestoreFromTray()
    {
        // 退出过程中收到唤起信号（托盘 Exit 后立刻双击 exe）→ 忽略，避免 Show 关闭中的窗口
        if (_isShuttingDown) return;

        // SystemTrayHelper 回调可能在非 UI 线程（命名事件等待线程）——切回 UI 线程
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RestoreFromTray);
            return;
        }
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        SystemTrayHelper.Instance.Hide();   // 窗口已回任务栏，隐藏托盘图标
    }

    /// <summary>
    /// 从后台切回前台动画：左侧工具栏从左向右滑入淡入，右侧工具页随后加载淡入。
    /// 错峰设计（修复实测三问题）：左侧 0-400ms 先落位，右侧延迟 200ms 跟上（200-620ms）——
    /// 不再"左侧还空着、右侧已过半"；位移 QuinticEase EaseOut（开头快、结尾极缓）去僵硬感；
    /// 总时长 620ms 比原 400ms 硬切舒缓。
    /// 显式 From——常态值是 1/0，无 From 则动画原地不动（ComboBox 弹层同款教训）。
    /// </summary>
    private void PlayReturnAnimations()
    {
        var slideEase = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var fadeEase = EaseOut();

        NavPane.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = fadeEase });
        NavPaneTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-220, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = slideEase });

        var delay = TimeSpan.FromMilliseconds(200);
        ContentScrollViewer.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = delay, EasingFunction = fadeEase });
        ContentPaneTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = delay, EasingFunction = slideEase });
    }

    /// <summary>列表重建（搜索过滤等）后恢复导航状态。去抖：Clear+Add 触发多次事件，
    /// 首次执行置 false 后其余排队调用直接返回——每个输入动作只刷新一次。
    /// 执行时机为 Background 优先级：新容器已完成布局并连接视觉树。</summary>
    private bool _navRefreshPending;
    private void RunNavigationRefresh()
    {
        if (!_navRefreshPending) return;
        _navRefreshPending = false;

        InitGroupHeights();   // 恢复展开组 Height=Auto（搜索克隆组 IsExpanded=true → 列表可见）
        InitGroupArrows();    // 恢复箭头角度（重建后新箭头 Angle=0 ▸ → 按 IsExpanded 校正）
        if (DataContext is ViewModels.MainViewModel vm && vm.SelectedTool != null)
        {
            var target = FindToolBorderByTool(vm.SelectedTool);
            if (target != null) PositionHighlight(target);  // 内部含 MarkSelectedNavItem（选中着色）
            else HighlightBar.Visibility = Visibility.Collapsed;  // 选中工具不在可见列表 → 隐藏高亮
        }
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

    // 统一动效参数（时长/缓动集中管理，避免各处漂移）
    private const int HighlightAnimMs = 200;
    private const int GroupAnimMs = 200;
    private const int HighlightFadeMs = 120;
    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };
    private static CubicEase EaseIn() => new() { EasingMode = EasingMode.EaseIn };

    /// <summary>当前选中态着色的导航项 Border（Tag="Selected" 驱动 XAML 触发器）</summary>
    private Border? _selectedNavBorder;

    /// <summary>初始化导航高亮到 SelectedTool 对应项（无则回退第一个可见项）</summary>
    private void InitHighlight()
    {
        if (DataContext is ViewModels.MainViewModel vm && vm.SelectedTool != null)
        {
            var target = FindToolBorderByTool(vm.SelectedTool);
            if (target != null && target.IsVisible)
            {
                PositionHighlight(target);
                return;
            }
        }
        // 回退：遍历 VisibleGroups 中展开组的子 ItemsControl，找首个可见的 Tool Border
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
                // 重复点击已选中项：仅退出设置页，不重放位移动画
                if (vm.SelectedTool == tool)
                {
                    if (SettingsLayer.Visibility == Visibility.Visible)
                        ExitSettingsView();
                    return;
                }
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

    /// <summary>将高亮指示器动画移动到指定元素的位置（跨分组定位；位移走 RenderTransform，纯渲染层不触发每帧布局）</summary>
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

        // 选中项本体着色（与高亮条解耦：组折叠高亮隐藏时选中线索仍在）
        MarkSelectedNavItem(itemElement);

        if (HighlightBar.Visibility != Visibility.Visible)
        {
            // 首次/重新出现：清除残留动画，直接定位（无位移动画）+ 淡入
            HighlightTransform.BeginAnimation(TranslateTransform.YProperty, null);
            HighlightBar.BeginAnimation(UIElement.OpacityProperty, null);
            HighlightTransform.Y = top;
            HighlightBar.Opacity = 0;
            HighlightBar.Visibility = Visibility.Visible;
            HighlightBar.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(HighlightFadeMs)));
        }
        else
        {
            // 已有位置：Y 位移动画平滑过渡（CubicEase EaseOut）
            HighlightTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
                top, TimeSpan.FromMilliseconds(HighlightAnimMs)) { EasingFunction = EaseOut() });
        }
    }

    /// <summary>
    /// 方案二（试效果）：选中高亮条边缘 toolbox 绿细线的呼吸闪烁——
    /// 淡出 1200ms → 停顿 300ms → 淡入 1200ms → 停顿 300ms（周期 3000ms）。
    /// 独立 SolidColorBrush（非共享资源），不影响其他使用 AccentBrush 的元素。
    /// 效果不佳时移除本方法与其 XAML BorderBrush。
    /// </summary>
    private void StartHighlightBorderBreath()
    {
        if (HighlightBar.BorderBrush is not SolidColorBrush brush) return;
        var full = Color.FromRgb(0x76, 0xB5, 0x80);
        var dim = Color.FromArgb(0x55, 0x76, 0xB5, 0x80);
        var breathe = new ColorAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(3000),
            RepeatBehavior = RepeatBehavior.Forever
        };
        breathe.KeyFrames.Add(new LinearColorKeyFrame(full, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        breathe.KeyFrames.Add(new LinearColorKeyFrame(dim, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
        breathe.KeyFrames.Add(new LinearColorKeyFrame(dim, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));  // 淡出后停顿 300ms
        breathe.KeyFrames.Add(new LinearColorKeyFrame(full, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2700))));
        breathe.KeyFrames.Add(new LinearColorKeyFrame(full, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3000))));  // 淡入后停顿 300ms
        brush.BeginAnimation(SolidColorBrush.ColorProperty, breathe);
    }

    /// <summary>选中态着色：Tag="Selected" 驱动 XAML 触发器，旧选中项清除标记</summary>
    private void MarkSelectedNavItem(FrameworkElement itemElement)
    {
        if (itemElement is not Border b || _selectedNavBorder == b) return;
        if (_selectedNavBorder != null) _selectedNavBorder.Tag = null;
        b.Tag = "Selected";
        _selectedNavBorder = b;
    }

    /// <summary>高亮条淡出并隐藏（折叠选中项所在组时调用；选中工具与右侧内容保留，不切走）</summary>
    private void FadeOutHighlight()
    {
        if (HighlightBar.Visibility != Visibility.Visible) return;
        HighlightBar.BeginAnimation(UIElement.OpacityProperty, null);
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(HighlightFadeMs));
        fade.Completed += (_, _) => HighlightBar.Visibility = Visibility.Collapsed;
        HighlightBar.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>高亮条在当前位置基础上同步位移 delta（分组展开/折叠动画期间与列表同步流动，同时长同缓动）</summary>
    private void MoveHighlightBy(double delta)
    {
        if (Math.Abs(delta) < 0.5 || HighlightBar.Visibility != Visibility.Visible) return;
        HighlightTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
            HighlightTransform.Y + delta, TimeSpan.FromMilliseconds(GroupAnimMs))
        { EasingFunction = delta > 0 ? EaseOut() : EaseIn() }); // 展开 EaseOut / 折叠 EaseIn
    }

    // --- 悬停高亮条（HoverBar：与 HighlightBar 同几何的第二条浮层） ---

    /// <summary>悬停条位移动画时长 ms（测试开关）：0 = 瞬移到位；>0 = 带 CubicEase EaseOut 位移动画</summary>
    private static int HoverMoveAnimMs = 120;
    private const int HoverFadeInMs = 100;
    private const int HoverFadeOutMs = 80;

    /// <summary>鼠标进入导航项/分组头：悬停条定位到目标（几何与 HighlightBar 一致，高度随目标；
    /// 已选中项不显示，避免与高亮条叠色加深）</summary>
    private void ShowHover(FrameworkElement target)
    {
        _hoverHidePending = false; // 取消延迟中的淡出（跨项移动不闪）
        if (ReferenceEquals(target, _selectedNavBorder)) { HideHover(); return; }

        Point position;
        try
        {
            position = target.TransformToAncestor(NavContainer).Transform(new Point(0, 0));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 悬停定位失败: {ex.Message}");
            return;
        }
        double top = position.Y;

        HoverBar.Height = target.ActualHeight;

        if (HoverBar.Visibility != Visibility.Visible)
        {
            // 瞬移到位后显示：仅会话内第一次淡入；之后再次出现直接全显
            // （从间隙/淡出恢复时若重新从 0 淡入，会看到"闪一下消失再浮现"）
            HoverTransform.BeginAnimation(TranslateTransform.YProperty, null);
            HoverTransform.Y = top;
            HoverBar.Visibility = Visibility.Visible;
            HoverBar.BeginAnimation(UIElement.OpacityProperty, null);
            if (_hoverShownOnce)
            {
                HoverBar.Opacity = 1;
            }
            else
            {
                _hoverShownOnce = true;
                HoverBar.Opacity = 0;
                HoverBar.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(HoverFadeInMs)));
            }
        }
        else
        {
            // 取消进行中的淡出，保持显示
            HoverBar.BeginAnimation(UIElement.OpacityProperty, null);
            HoverBar.Opacity = 1;

            if (HoverMoveAnimMs > 0)
            {
                // 测试模式：位移动画过渡（EaseOut）
                HoverTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
                    top, TimeSpan.FromMilliseconds(HoverMoveAnimMs)) { EasingFunction = EaseOut() });
            }
            else
            {
                // 默认：瞬移到位，避免"追着鼠标跑"的拖尾感
                HoverTransform.BeginAnimation(TranslateTransform.YProperty, null);
                HoverTransform.Y = top;
            }
        }
    }

    /// <summary>鼠标离开：宽限期后淡出隐藏。
    /// 相邻两项间有 2px 间隙（上下 Margin 各 1px），慢速移动时光标会在间隙停留：
    /// 若离开即淡出，间隙期间淡出真实播放（甚至播完隐藏），进入下一项再从 0 淡入——
    /// 即"移动到下一个会闪一下消失"。80ms 宽限定时器由 ShowHover 取消，
    /// 只有真正离开列表（间隙停留超宽限）才淡出</summary>
    private bool _hoverHidePending;
    private bool _hoverShownOnce; // 会话内是否已显示过（再次出现不再淡入）
    private System.Windows.Threading.DispatcherTimer? _hoverHideTimer;

    private void HideHover()
    {
        if (HoverBar.Visibility != Visibility.Visible) return;
        _hoverHidePending = true;
        if (_hoverHideTimer == null)
        {
            _hoverHideTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(80),
                System.Windows.Threading.DispatcherPriority.Normal,
                (_, _) =>
                {
                    _hoverHideTimer?.Stop();
                    if (!_hoverHidePending || HoverBar.Visibility != Visibility.Visible) return;
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(HoverFadeOutMs));
                    fade.Completed += (_, _) => HoverBar.Visibility = Visibility.Collapsed;
                    HoverBar.BeginAnimation(UIElement.OpacityProperty, fade);
                },
                Dispatcher);
        }
        _hoverHideTimer.Stop();
        _hoverHideTimer.Start();
    }

    /// <summary>导航项鼠标进入——悬停条跟随</summary>
    private void NavItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement el) ShowHover(el);
    }

    /// <summary>导航项鼠标离开——悬停条淡出</summary>
    private void NavItem_MouseLeave(object sender, MouseEventArgs e) => HideHover();

    /// <summary>分类标题头点击——切换展开/折叠（渲染式动画；动画期间高亮与列表同步位移；选中工具不被强制切走）</summary>
    private void GroupHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not Models.ToolGroup group) return;

        // 找到动画容器 Border（StackPanel → Children[1]）
        Border? animContainer = null;
        StackPanel? groupPanel = null;
        if (VisualTreeHelper.GetParent(element) is StackPanel sp
            && sp.Children.Count > 1
            && sp.Children[1] is Border b
            && b.Tag is string tag && tag == "GroupAnimContainer")
        {
            groupPanel = sp;
            animContainer = b;
        }

        // 渲染式动画进行中（≤200ms）忽略本次切换：中间态（兄弟元素平移补偿）无法平滑反转
        if (animContainer != null && _animatingContainers.Contains(animContainer)) return;

        bool nowExpanded = !group.IsExpanded;
        group.IsExpanded = nowExpanded;
        AnimateGroupArrow(element, nowExpanded); // 方案 D：箭头旋转动画（0° 折叠 ▸ ↔ 90° 展开 ▾）

        var vm = DataContext as ViewModels.MainViewModel;
        bool selectedInGroup = vm?.SelectedTool != null && group.Tools.Contains(vm.SelectedTool);

        // 高度差（绝对值）：展开优先取缓存（免点击时同步 UpdateLayout 测量），折叠取当前实测
        double delta = 0;
        if (animContainer != null)
        {
            if (nowExpanded)
            {
                if (!_groupHeights.TryGetValue(group, out delta))
                {
                    delta = MeasureAutoHeight(animContainer);
                    _groupHeights[group] = delta;
                }
            }
            else
            {
                delta = animContainer.ActualHeight;
                if (delta <= 0) _groupHeights.TryGetValue(group, out delta);
            }
        }

        // 动画完成回调：布局稳定后重定位高亮（兜底校正同步位移的累计误差）
        Action onAnimCompleted = () => Dispatcher.BeginInvoke(
            new Action(ScheduleHighlightReposition),
            System.Windows.Threading.DispatcherPriority.Background);

        // 高亮同步：与分组动画同时启动
        if (vm?.SelectedTool != null)
        {
            if (selectedInGroup)
            {
                // 选中项在本组：折叠 → 高亮淡出（SelectedTool 与右侧内容保留）；展开 → 完成回调里淡入归位
                if (!nowExpanded) FadeOutHighlight();
            }
            else if (delta != 0 && GroupIndexOf(vm, group) < GroupIndexOf(vm, vm.SelectedTool))
            {
                // 被切换组在选中项上方：高亮与列表同步流动（展开下移、折叠上移）
                MoveHighlightBy(nowExpanded ? delta : -delta);
            }
        }

        // 渲染式展开/折叠（零布局动画；完成后一次性提交布局并回调重定位高亮）
        if (animContainer != null && groupPanel != null && delta > 0)
            AnimateGroupRender(animContainer, groupPanel, nowExpanded, delta, onAnimCompleted);
        else
            onAnimCompleted();
    }

    /// <summary>
    /// 初始化分组箭头角度（展开 = 90° ▾ / 折叠 = 0° ▸）：
    /// 旋转动画仅在点击切换时触发，首次加载的初始状态需按 IsExpanded 同步。
    /// </summary>
    private void InitGroupArrows()
    {
        foreach (var textBlock in FindVisualChildren<TextBlock>(NavContainer))
        {
            if (textBlock.Tag as string == "GroupArrow" && textBlock.DataContext is Models.ToolGroup group)
            {
                var rotate = EnsureMutableRotate(textBlock);
                rotate.BeginAnimation(RotateTransform.AngleProperty, null); // 清残留动画再设值
                rotate.Angle = group.IsExpanded ? 90 : 0;
            }
        }
    }

    /// <summary>
    /// 保证箭头 RotateTransform 可动画：静态 XAML 创建的 Freezable 可能被冻结（DataTemplate 中
    /// po:Freeze="False" 不生效），冻结对象无法 BeginAnimation/设值——统一替换为可变实例。
    /// </summary>
    private static RotateTransform EnsureMutableRotate(TextBlock arrow)
    {
        if (arrow.RenderTransform is RotateTransform rt && !rt.IsFrozen) return rt;
        var fresh = new RotateTransform { Angle = 0 };
        arrow.RenderTransform = fresh;
        return fresh;
    }

    /// <summary>
    /// 方案 D：分组头箭头旋转动画——折叠 = 0°（▸），展开 = 90°（▾），200ms CubicEase EaseOut。
    /// 目标 TextBlock 由 Tag="GroupArrow" 在分组头视觉树内定位。
    /// </summary>
    private static void AnimateGroupArrow(FrameworkElement header, bool expanded)
    {
        foreach (var textBlock in FindVisualChildren<TextBlock>(header))
        {
            if (textBlock.Tag as string == "GroupArrow")
            {
                var rotate = EnsureMutableRotate(textBlock); // 冻结防御：替换为可变实例
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(
                    expanded ? 90 : 0, TimeSpan.FromMilliseconds(GroupAnimMs))
                {
                    EasingFunction = EaseOut()
                });
                break;
            }
        }
    }

    /// <summary>VisibleGroups 中某分组的索引（传入工具时返回其所在组索引）</summary>
    private static int GroupIndexOf(ViewModels.MainViewModel vm, object groupOrTool)
    {
        for (int i = 0; i < vm.VisibleGroups.Count; i++)
        {
            if (ReferenceEquals(vm.VisibleGroups[i], groupOrTool)) return i;
            if (groupOrTool is Models.ITool tool && vm.VisibleGroups[i].Tools.Contains(tool)) return i;
        }
        return -1;
    }

    /// <summary>测量容器内容完整高度（测量后恢复 0，供展开动画终点预判与高亮同步位移）</summary>
    private static double MeasureAutoHeight(Border container)
    {
        container.Height = double.NaN; // Auto
        container.UpdateLayout();
        double h = container.ActualHeight;
        container.Height = 0;
        return h;
    }

    /// <summary>渲染式分组展开/折叠：兄弟元素 RenderTransform 平移 + 内容 Clip 几何揭示，
    /// 全程零布局动画（旧实现动画 Height，每帧触发整个导航面板 Measure/Arrange）；
    /// 动画结束一次性提交布局（Height=Auto/0）并复位变换，视觉无缝衔接</summary>
    private void AnimateGroupRender(Border container, StackPanel groupPanel, bool expand, double delta, Action? onCompleted)
    {
        _animatingContainers.Add(container);
        IEasingFunction ease = expand ? EaseOut() : EaseIn();
        var duration = TimeSpan.FromMilliseconds(GroupAnimMs);

        // 收集下方兄弟元素（ItemsControl 生成的容器项），挂上平移变换
        var siblings = new List<(UIElement el, TranslateTransform tt)>();
        if (VisualTreeHelper.GetParent(groupPanel) is FrameworkElement groupHost
            && VisualTreeHelper.GetParent(groupHost) is Panel panel)
        {
            int index = panel.Children.IndexOf(groupHost);
            for (int i = index + 1; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not UIElement sib) continue;
                if (sib.RenderTransform is not TranslateTransform tt)
                {
                    tt = new TranslateTransform();
                    sib.RenderTransform = tt;
                }
                siblings.Add((sib, tt));
            }
        }

        // 内容按实测高度渲染（容器布局高度保持 0，不撑开兄弟），由 Clip 几何逐步揭示
        if (container.Child is FrameworkElement child)
            child.Height = delta;

        double w = Math.Max(container.ActualWidth, 1);
        var clip = new RectangleGeometry(new Rect(0, 0, w, expand ? 0 : delta));
        container.Clip = clip;

        if (!expand)
        {
            // 折叠：布局先塌缩（Height=0，兄弟被布局瞬移上移 delta），同一回合内给兄弟 +delta
            // 补偿平移保持视觉原位，再平移回 0 —— 两者在下一次渲染前同时生效，无跳变
            container.Height = 0;
            foreach (var (_, tt) in siblings) tt.Y = delta;
        }

        // Clip 揭示动画（几何属性动画：只走渲染，不触发布局）；完成回调里提交布局
        var clipAnim = new RectAnimation(
            new Rect(0, 0, w, expand ? delta : 0), duration)
        { EasingFunction = ease };
        clipAnim.Completed += (_, _) => CommitGroupAnimation(container, expand, siblings, onCompleted);
        clip.BeginAnimation(RectangleGeometry.RectProperty, clipAnim);

        // 兄弟平移动画（渲染线程）：展开 0→+delta，折叠 -delta→0
        foreach (var (_, tt) in siblings)
        {
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
                expand ? delta : 0, duration) { EasingFunction = ease });
        }
    }

    /// <summary>渲染式分组动画的提交：一次性落地布局并复位全部变换/裁剪（与动画末帧视觉一致，无跳变）</summary>
    private void CommitGroupAnimation(Border container, bool expand,
        List<(UIElement el, TranslateTransform tt)> siblings, Action? onCompleted)
    {
        _animatingContainers.Remove(container);
        container.Clip = null;
        if (container.Child is FrameworkElement child)
            child.Height = double.NaN;
        if (expand)
            container.Height = double.NaN; // 布局接管：兄弟被推到最终位，与平移末帧一致

        foreach (var (el, tt) in siblings)
        {
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            el.RenderTransform = null;
        }
        onCompleted?.Invoke();
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

    /// <summary>分类头鼠标进入——悬停条跟随（图标切换已由常驻箭头取代）</summary>
    private void GroupHeader_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element) ShowHover(element);
    }

    /// <summary>分类头鼠标离开——悬停条淡出</summary>
    private void GroupHeader_MouseLeave(object sender, MouseEventArgs e)
    {
        HideHover();
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

    /// <summary>分组内容实测高度缓存（避免展开时重复 UpdateLayout 同步测量；VisibleGroups 变更时清空）</summary>
    private readonly Dictionary<Models.ToolGroup, double> _groupHeights = new();

    /// <summary>渲染式展开/折叠动画进行中的容器（200ms 内忽略对同组的重复切换）</summary>
    private readonly HashSet<Border> _animatingContainers = new();

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

    // 进出动画令牌：Completed 回调只认最后一次动画，防止快速连点导致状态错乱
    private int _settingsAnimToken;

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        EnterSettingsView();
    }

    private void EnterSettingsView()
    {
        _settingsAnimToken++;

        // 隐藏内容区，显示设置层
        ContentScrollViewer.Visibility = Visibility.Collapsed;
        SettingsLayer.Visibility = Visibility.Visible;
        SettingsLayer.IsHitTestVisible = true;
        HighlightBar.Visibility = Visibility.Collapsed;

        // 淡入 + 8px 上滑（180ms EaseOut，与 TransitioningContentControl 同参数族）
        SettingsLayer.Opacity = 0;
        SettingsLayerTransform.Y = 8;
        SettingsLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(360)) { EasingFunction = EaseOut() });
        SettingsLayerTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(360)) { EasingFunction = EaseOut() });
    }

    private void ExitSettingsView()
    {
        int token = ++_settingsAnimToken;

        SettingsLayer.IsHitTestVisible = false;   // 淡出期间挡点击，避免误触层内控件

        // 内容切换退场进行中（设置页期间点击工具）：下层内容区保持折叠不显示——
        // 标题区/旧工具的退场动画在折叠容器内不可见（设置层 60% 半透明遮不住，
        // 实测会"标题栏闪一下消失"）；等退场完成（ExitCompleted）再显示内容区并快速退场，
        // 露出正在淡入的新内容
        if (ContentTransitionControl.IsExiting)
        {
            ContentTransitionControl.ExitCompleted += StartSettingsExit;
            return;
        }

        // 无内容切换（Back 返回等）：下层立即露出，设置层自身渐隐 + 下滑 150ms
        ContentScrollViewer.Visibility = Visibility.Visible;
        StartSettingsExit();

        void StartSettingsExit()
        {
            ContentTransitionControl.ExitCompleted -= StartSettingsExit;   // 一次性订阅
            if (token != _settingsAnimToken) return;   // 等待期间又进入了设置页，放弃本次收尾

            // 退场完成（仅等退场路径）：此刻新内容已切入、正在淡入起点，可以露出下层
            ContentScrollViewer.Visibility = Visibility.Visible;

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = EaseIn() };
            fade.Completed += (_, _) =>
            {
                if (token != _settingsAnimToken) return;
                SettingsLayer.Visibility = Visibility.Collapsed;
                SettingsLayer.Opacity = 1;                 // 复位供下次进入
                SettingsLayerTransform.Y = 0;
                SettingsLayer.IsHitTestVisible = true;
            };
            SettingsLayer.BeginAnimation(OpacityProperty, fade);
            SettingsLayerTransform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, TimeSpan.FromMilliseconds(150)) { EasingFunction = EaseIn() });
        }

        // 恢复高亮到当前选中工具（设置页期间可能已通过导航点击/插件跳转切换了选中项，
        // 必须用 vm.SelectedTool 而非进入时保存的旧引用，否则高亮会被拽回旧工具）
        if (DataContext is ViewModels.MainViewModel vm
            && vm.SelectedTool != null
            && vm.VisibleGroups.Any(g => g.IsExpanded && g.Tools.Contains(vm.SelectedTool)))
        {
            var selected = vm.SelectedTool;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var target = FindToolBorderByTool(selected);
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
                onDoubleClick: () => Dispatcher.Invoke(RestoreFromTray),
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
                _wasBackground = true;   // \u6258\u76D8\u9690\u85CF\uFF1A\u6062\u590D\u663E\u793A\u65F6\u64AD\u653E\u5207\u56DE\u52A8\u753B
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
    private double _breathPhase;        // 呼吸缩放相位（代码驱动，仅可见时推进）
    private bool _haloInitialized;      // 首次移动时直接吸附，避免从角落滑入
    private bool _glowTargetsDirty = true;   // 边缘发光目标清单待刷新
    private DateTime _glowTargetsLastRebuild = DateTime.MinValue;

    /// <summary>初始化鼠标光晕：帧驱动轮询光标，插值跟随 + 淡入淡出 + 代码驱动呼吸。
    /// 帧率策略：活跃期（光标移动/淡入淡出/呼吸）由 CompositionTarget.Rendering 帧驱动保证顺滑；
    /// 静止收敛后零写入 → 无脏区 → 渲染帧自然停止，合成器完全空闲；
    /// 下一次光标移动经 MouseMove 强制一帧唤醒循环。
    /// （曾用 DispatcherTimer 轮询：Normal 优先级下 tick 被调度延迟/合并，实际远低于 60Hz，
    /// 按 60fps 设计的插值系数导致光晕又慢又掉帧，已回退）</summary>
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

        // 导航/内容区/设置层滚动时强制发光重绘：滚动只平移内容不触发布局，光标静止时去重逻辑会跳过重绘
        // （ContentScrollViewer 的 ScrollChanged 为冒泡路由事件：内部工具页面的滚动也会触发；
        //   设置层是独立覆盖层不在 ContentScrollViewer 子树内，需单独绑定）
        NavScrollViewer.ScrollChanged += (_, _) => GlowLayer.Refresh();
        ContentScrollViewer.ScrollChanged += (_, _) => GlowLayer.Refresh();
        SettingsScrollViewer.ScrollChanged += (_, _) => GlowLayer.Refresh();

        // 唤醒：循环休眠后（无脏区不产帧），光标移动/进出/窗口激活切换强制一帧，
        //  Rendering 恢复触发；活跃期写入产生的脏区使循环自维持
        MouseMove += (_, _) => HaloLayer.InvalidateVisual();
        MouseLeave += (_, _) => HaloLayer.InvalidateVisual();
        Activated += (_, _) => HaloLayer.InvalidateVisual();
        Deactivated += (_, _) => HaloLayer.InvalidateVisual();

        CompositionTarget.Rendering += HaloFrame;
    }

    private DateTime _lastHaloFrame = DateTime.MinValue;

    /// <summary>光晕/发光帧处理：插值按真实帧间隔（dt）换算，60/120/144Hz 及掉帧下速度一致；
    /// 静止收敛后零写入零重绘（循环随之休眠，由输入事件唤醒）</summary>
    private void HaloFrame(object? sender, EventArgs e)
    {
        // 窗口尚未完成初始化（视觉未连接到 PresentationSource）时跳过本帧
        if (PresentationSource.FromVisual(HaloLayer) == null) return;

        // 每帧用 Win32 GetCursorPos 轮询光标（原始屏幕坐标，与消息投递和命中测试
        // 完全无关——WPF 的 Mouse.GetPosition 由输入系统维护，鼠标悬停在
        // WindowChrome CaptionHeight 划出的 HTCAPTION 非客户区（顶栏空白处）时
        // 不再更新，会导致光晕误判为"鼠标已离开"而淡出）。
        if (!GetCursorPos(out var cursor)) return;

        // 帧间隔（上限 100ms 防窗口失焦后首帧跳变）
        var now = DateTime.UtcNow;
        double dt = _lastHaloFrame == DateTime.MinValue ? 16.0
            : Math.Min((now - _lastHaloFrame).TotalMilliseconds, 100);
        _lastHaloFrame = now;
        double step = dt / 16.0; // 以 60fps 为基准的步长倍率

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

        // 插值滞后跟随（0.12 @60fps ≈ 轻微延迟拖尾，按 dt 换算保证任意帧率同速）；
        // 收敛到 0.3px 内吸附终点，此后不再写变换
        double dx = _haloTarget.X - _haloPos.X, dy = _haloTarget.Y - _haloPos.Y;
        if (Math.Abs(dx) > 0.3 || Math.Abs(dy) > 0.3)
        {
            double f = 1 - Math.Pow(1 - 0.12, step);
            _haloPos.X += dx * f;
            _haloPos.Y += dy * f;
            HaloTranslate.X = _haloPos.X - HaloEllipse.Width / 2;
            HaloTranslate.Y = _haloPos.Y - HaloEllipse.Height / 2;
        }
        else if (_haloPos != _haloTarget)
        {
            _haloPos = _haloTarget;
            HaloTranslate.X = _haloPos.X - HaloEllipse.Width / 2;
            HaloTranslate.Y = _haloPos.Y - HaloEllipse.Height / 2;
        }

        // 鼠标光晕开关：关闭时按"鼠标不在窗口"处理，平滑淡出后保持熄灭
        bool haloOn = AppSettings.Instance.MouseHaloEnabled;
        double opacityTarget = (inside && haloOn) ? 1.0 : 0.0;
        if (Math.Abs(opacityTarget - _haloOpacity) > 0.005)
        {
            _haloOpacity += (opacityTarget - _haloOpacity) * (1 - Math.Pow(1 - 0.08, step));
            HaloEllipse.Opacity = _haloOpacity;
        }
        else if (_haloOpacity != opacityTarget)
        {
            _haloOpacity = opacityTarget;
            HaloEllipse.Opacity = _haloOpacity;
        }

        // 呼吸缩放（替代 XAML Forever Storyboard）：仅光晕可见时推进相位，隐形零开销
        if (_haloOpacity > 0.001)
        {
            _breathPhase += Math.PI * 2 * dt / 3000.0;   // 3s 周期（与原 1.5s AutoReverse 一致）
            var s = 1.0 + 0.1 * Math.Sin(_breathPhase);  // 0.9~1.1，等效原 ScaleX/Y 动画
            HaloScale.ScaleX = s;
            HaloScale.ScaleY = s;
        }

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
        // 传原始光标位置（非插值滞后的 _haloPos），保证移出控件瞬时熄灭（0ms）；
        // EdgeGlowLayer 内部去重：光标静止时零重绘
        GlowLayer.UpdateCursor(pt, inside && glowOn);
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