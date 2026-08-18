using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Toolbox.Core.Controls;
using Toolbox.Core.Services;
using Toolbox.Plugins.Services;
using Toolbox.Tools.Models;
using static Toolbox.Core.Helpers.Win32Native;

namespace Toolbox.Tools.Views;

/// <summary>
/// 任务栏嵌入迷你媒体窗口。
/// 核心机制：通过 SetParent 将 WPF 窗口设为 Shell_TrayWnd 的子窗口，从而"嵌入"任务栏。
/// 父窗口统一使用 Shell_TrayWnd；SetWindowPos 坐标一律使用【相对任务栏客户区】坐标
/// （WS_CHILD 子窗口坐标语义，实测 2026-08-18：传屏幕坐标会导致窗口移出屏幕）。
/// 交互：单击 → 弹出媒体卡片（WidgetClicked）；按住拖动换位（吸附左/右半区）；右键菜单。
/// Explorer 重启自愈：TaskbarCreated 广播只发给顶层窗口，嵌入后的 WS_CHILD 窗口收不到，
/// 因此以 2 秒看门狗比对任务栏句柄为兜底机制。
/// </summary>
public partial class TaskbarMusicWindow : Window
{
    // Explorer 重启消息（尽力而为路径；广播只达顶层窗口，主要靠看门狗兜底）
    private int _wmTaskbarCreated;

    // 任务栏句柄缓存
    private IntPtr _taskbarHwnd = IntPtr.Zero;
    private IntPtr _trayNotifyHwnd = IntPtr.Zero;

    // 原始窗口样式（用于恢复）
    private int _originalStyle;
    private int _originalExStyle;

    // 是否已成功嵌入 / 是否对用户可见（idle 自动隐藏用）
    private bool _isEmbedded;
    private bool _widgetVisible = true;

    // 当前歌曲信息
    private NowPlayingInfo? _currentInfo;

    // 看门狗：Explorer 重启自愈 + 嵌入失败重试
    private readonly DispatcherTimer _watchdogTimer;
    private int _embedRetryCount;
    private const int MaxEmbedRetries = 30; // 约 30 秒内未就绪则放弃（保持隐藏，等待设置重开）

    // ── 交互事件（由 Manager 订阅）──
    /// <summary>单击控件（非拖拽）→ 弹出/收起媒体卡片。</summary>
    public event Action? WidgetClicked;
    /// <summary>右键菜单打开（供 Manager 同步菜单状态）。</summary>
    public event Action? WidgetRightClicked;
    /// <summary>拖拽换位完成（吸附左/右）→ 重新锚定媒体卡片。</summary>
    public event Action? WidgetMoved;

    // ── 拖拽状态 ──
    private bool _mouseDown;
    private bool _dragging;
    private POINT _downScreen;
    private int _downWidgetX; // 按下时窗口相对任务栏的 x（物理像素）

    public TaskbarMusicWindow()
    {
        InitializeComponent();

        // 注册 Explorer 重启消息（需在窗口接收广播前注册；嵌入后收不到，看门狗兜底）
        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        SourceInitialized += OnSourceInitialized;
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;

        // 拖拽 / 单击 / 右键（子控件未 Handled 的鼠标事件冒泡到窗口统一处理）
        MouseLeftButtonDown += OnDragMouseLeftButtonDown;
        MouseMove += OnDragMouseMove;
        MouseLeftButtonUp += OnDragMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;

        _watchdogTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            OnWatchdogTick,
            Dispatcher);
    }

    public bool IsEmbedded => _isEmbedded;

    /// <summary>当前是否对用户可见（idle 隐藏时 false，嵌入状态不受影响）。</summary>
    public bool IsWidgetVisible => _widgetVisible;

    // ── 诊断日志（%LocalAppData%/Toolbox(-Debug)/taskbar_widget.log）──

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(AppPaths.DataDir, "taskbar_widget.log");
            File.AppendAllText(logPath,
                $"{DateTime.Now:HH:mm:ss.fff} [TaskbarMusicWindow] {message}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    // ── 窗口生命周期 ──

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // 注册 WndProc 钩子
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Log("窗口 Loaded，尝试嵌入");
        if (TryEmbedIntoTaskbar())
        {
            _watchdogTimer.Interval = TimeSpan.FromSeconds(2);
            _watchdogTimer.Start();
        }
        else
        {
            // 任务栏未就绪：先隐藏避免独立窗口闪现，由看门狗重试
            Log("任务栏未就绪，隐藏等待重试");
            Hide();
            _watchdogTimer.Interval = TimeSpan.FromSeconds(1);
            _watchdogTimer.Start();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _watchdogTimer.Stop();
        DetachFromTaskbar();
    }

    /// <summary>
    /// 核心：将窗口嵌入任务栏。
    /// 步骤：1) 找到 Shell_TrayWnd  2) 修改 WS_CHILD 样式  3) SetParent  4) 定位
    /// </summary>
    public bool TryEmbedIntoTaskbar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Log("hwnd 为 0，无法嵌入");
                return false;
            }

            // 1. 找到主任务栏窗口
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd == IntPtr.Zero)
            {
                Log("未找到 Shell_TrayWnd，任务栏可能未就绪");
                return false;
            }

            // 2. 找到系统托盘（用于右侧定位参考）
            _trayNotifyHwnd = FindWindowEx(taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);

            // 3. 样式转换（仅首次需要；重复嵌入时样式已就位，避免样式漂移）
            if (!_isEmbedded)
            {
                _originalStyle = GetWindowLong(hwnd, GWL_STYLE);
                _originalExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                int newStyle = (_originalStyle | WS_CHILD) & ~WS_POPUP;
                SetWindowLong(hwnd, GWL_STYLE, newStyle);
                SetWindowLong(hwnd, GWL_EXSTYLE, _originalExStyle | WS_EX_NOACTIVATE);
            }

            _taskbarHwnd = taskbarHwnd;

            // 4. 设置父窗口为任务栏（核心！）
            SetParent(hwnd, _taskbarHwnd);

            // 5. 定位到任务栏上（按当前可见性决定是否显示）
            Reposition();

            _isEmbedded = true;

            // Explorer 重启重嵌后恢复歌曲显示（首次嵌入时 _currentInfo 尚为 null，无副作用）
            if (_currentInfo != null)
                UpdateSongInfo(_currentInfo);

            Log($"嵌入成功（任务栏 {_taskbarHwnd}）");
            return true;
        }
        catch (Exception ex)
        {
            Log($"嵌入任务栏失败（异常）: {ex}");
            _isEmbedded = false;
            return false;
        }
    }

    /// <summary>
    /// 计算并设置窗口在任务栏上的位置。
    /// ⚠️ 坐标语义：WS_CHILD 子窗口的 SetWindowPos 坐标是【相对父窗口客户区】的
    /// （任务栏左上角为原点），不是屏幕坐标。
    /// 同时不设置 WPF 的 Left/Top/Width/Height（屏幕坐标语义，会与子窗口坐标冲突）。
    /// </summary>
    public void Reposition()
    {
        if (_taskbarHwnd == IntPtr.Zero) return;

        var hwnd = new WindowInteropHelper(this).Handle;

        // 获取任务栏矩形（物理像素）
        if (!GetWindowRect(_taskbarHwnd, out var taskbarRect))
        {
            Log("Reposition: GetWindowRect(任务栏) 失败");
            return;
        }

        // 固定尺寸（DIP 与物理 1:1 换算）
        double dpiScale = 1.0;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            dpiScale = source.CompositionTarget.TransformToDevice.M11;
        }
        int width = (int)Math.Round(Toolbox.Plugins.Controls.TaskbarMusicWidget.FixedWidth * dpiScale);
        int height = (int)Math.Round(Toolbox.Plugins.Controls.TaskbarMusicWidget.FixedHeight * dpiScale);

        int x, y;

        if (AudioflowSettings.Instance.TaskbarWidgetPosition == 1)
        {
            // 右侧：紧邻系统托盘左侧（坐标相对任务栏客户区）
            if (_trayNotifyHwnd != IntPtr.Zero && GetWindowRect(_trayNotifyHwnd, out var trayRect))
            {
                x = (trayRect.Left - taskbarRect.Left) - width;
            }
            else
            {
                x = taskbarRect.Width - width - 100; // 预留系统托盘空间
            }
        }
        else
        {
            // 左侧：紧贴任务栏左边缘，留 8 DIP 间距保持视觉平衡（坐标为物理像素，需乘 DPI）
            x = (int)Math.Round(8.0 * dpiScale);
        }

        // 垂直居中于任务栏（相对任务栏客户区）
        y = (taskbarRect.Height - height) / 2;

        // 防越界（任务栏客户区内）
        if (x < 0) x = 0;
        if (x + width > taskbarRect.Width) x = taskbarRect.Width - width;
        if (x < 0) x = 0;

        // HWND_TOP（0）：把子窗口提到父窗口（任务栏）z-order 顶部，防止被 ReBar/托盘盖住。
        // SWP_SHOWWINDOW 仅在用户可见时携带（idle 隐藏时保持隐藏，看门狗重嵌也不显示）
        uint flags = SWP_NOACTIVATE;
        if (_widgetVisible) flags |= SWP_SHOWWINDOW;

        SetWindowPos(hwnd, (IntPtr)HWND_TOP, x, y, width, height, flags);
    }

    /// <summary>设置窗口对用户可见/隐藏（idle 自动隐藏用；不改变嵌入状态，看门狗不会重新显示）。</summary>
    public void SetWidgetVisible(bool visible)
    {
        if (_widgetVisible == visible) return;
        _widgetVisible = visible;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        ShowWindow(hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
        Log($"SetWidgetVisible({visible})");
    }

    /// <summary>获取迷你控件当前屏幕位置（物理像素，供媒体卡片锚定）。</summary>
    public Rect GetWidgetScreenBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rc))
        {
            return new Rect(rc.Left, rc.Top, rc.Width, rc.Height);
        }
        return Rect.Empty;
    }

    // ── 拖拽 / 单击 / 右键 ──

    private void OnDragMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isEmbedded) return;
        if (AudioflowSettings.Instance.TaskbarWidgetLocked) return;

        _mouseDown = true;
        _dragging = false;
        GetCursorPos(out _downScreen);
        _downWidgetX = GetCurrentWidgetX();
        Mouse.Capture(this);
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || !_isEmbedded) return;
        if (AudioflowSettings.Instance.TaskbarWidgetLocked) return;

        GetCursorPos(out var cur);
        double dx = cur.X - _downScreen.X;
        double dy = cur.Y - _downScreen.Y;

        // 8px 阈值内视为单击（点击），超过进入拖拽
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 8) return;
        if (!_dragging)
        {
            _dragging = true;
            _downScreen = cur; // 重新以拖拽起点计，防按下瞬间跳变
            _downWidgetX = GetCurrentWidgetX();
            Log("开始拖拽");
        }

        // 移动窗口（相对任务栏 x，y 保持垂直居中）
        var hwnd = new WindowInteropHelper(this).Handle;
        if (GetWindowRect(_taskbarHwnd, out var taskbarRect))
        {
            int width = (int)Math.Round(Toolbox.Plugins.Controls.TaskbarMusicWidget.FixedWidth
                * (PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0));
            int height = (int)Math.Round(Toolbox.Plugins.Controls.TaskbarMusicWidget.FixedHeight
                * (PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0));
            int newX = Math.Clamp(_downWidgetX + (int)Math.Round((double)(cur.X - _downScreen.X)), 0, taskbarRect.Width - width);
            int y = (taskbarRect.Height - height) / 2;
            SetWindowPos(hwnd, (IntPtr)HWND_TOP, newX, y, width, height, SWP_NOACTIVATE);
        }
    }

    private void OnDragMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        Mouse.Capture(null);

        var hwnd = new WindowInteropHelper(this).Handle;

        if (_dragging)
        {
            _dragging = false;

            // 吸附左/右半区：窗口中心在任务栏左半 → 左，否则右
            if (GetWindowRect(_taskbarHwnd, out var taskbarRect) && GetWindowRect(hwnd, out var wr))
            {
                int center = (wr.Left - taskbarRect.Left) + wr.Width / 2;
                int side = center < taskbarRect.Width / 2 ? 0 : 1;
                if (AudioflowSettings.Instance.TaskbarWidgetPosition != side)
                {
                    AudioflowSettings.Instance.TaskbarWidgetPosition = side;
                }
                Reposition();
                Log($"拖拽结束，吸附到{(side == 0 ? "左" : "右")}");
                WidgetMoved?.Invoke();
            }
        }
        else
        {
            // 单击 → 弹出/收起媒体卡片
            WidgetClicked?.Invoke();
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var settings = AudioflowSettings.Instance;

        var items = new System.Collections.Generic.List<ThemedMenuWindow.Item>
        {
            new() { Text = "锁定位置", IsChecked = settings.TaskbarWidgetLocked,
                Action = () => settings.TaskbarWidgetLocked = !settings.TaskbarWidgetLocked },
            new() { Text = "无播放时自动隐藏", IsChecked = settings.TaskbarWidgetHideWhenIdle,
                Action = () => settings.TaskbarWidgetHideWhenIdle = !settings.TaskbarWidgetHideWhenIdle },
            new() { Text = "位置：左侧", IsChecked = settings.TaskbarWidgetPosition == 0,
                Action = () => settings.TaskbarWidgetPosition = 0 },
            new() { Text = "位置：右侧", IsChecked = settings.TaskbarWidgetPosition == 1,
                Action = () => settings.TaskbarWidgetPosition = 1 },
            ThemedMenuWindow.Item.Separator(),
            new() { Text = "隐藏任务栏控件", Action = () => settings.TaskbarWidgetEnabled = false },
        };

        // PointToScreen 返回物理像素，TransformFromDevice 转回 DIP（菜单位置用）
        var pt = PointToScreen(Mouse.GetPosition(this));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            pt = target.TransformFromDevice.Transform(pt);

        WidgetRightClicked?.Invoke();
        ThemedMenuWindow.ShowAt(pt, items);
    }

    /// <summary>当前窗口相对任务栏的 x（物理像素）。</summary>
    private int GetCurrentWidgetX()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return 0;
        if (GetWindowRect(_taskbarHwnd, out var taskbarRect) && GetWindowRect(hwnd, out var wr))
        {
            return wr.Left - taskbarRect.Left;
        }
        return 0;
    }

    // ── WndProc ──

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Explorer 重启后重新嵌入（尽力而为；嵌入为 WS_CHILD 后收不到广播，看门狗兜底）
        if (msg == _wmTaskbarCreated)
        {
            Log("收到 TaskbarCreated，尝试重新嵌入");
            Dispatcher.BeginInvoke(new Action(() => TryEmbedIntoTaskbar()));
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    // ── 看门狗：Explorer 重启自愈 + 嵌入失败重试 ──

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (_isEmbedded)
        {
            // 已嵌入：任务栏句柄变化（Explorer 重启）则重新嵌入
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero && taskbarHwnd != _taskbarHwnd)
            {
                Log($"检测到任务栏句柄变化 {_taskbarHwnd} → {taskbarHwnd}，重新嵌入");
                TryEmbedIntoTaskbar();
            }
            return;
        }

        // 未嵌入（首次失败）：重试，成功后 SetWindowPos 已带 SWP_SHOWWINDOW 恢复显示
        if (++_embedRetryCount > MaxEmbedRetries)
        {
            _watchdogTimer.Stop();
            Log("多次重试仍无法嵌入，放弃（保持隐藏）");
            return;
        }

        if (TryEmbedIntoTaskbar())
        {
            Log($"重试成功（第 {_embedRetryCount} 次）");
        }
    }

    // ── 公共方法 ──

    public void UpdateSongInfo(NowPlayingInfo info)
    {
        _currentInfo = info;
        MusicWidget.UpdateSongInfo(info);

        // 歌曲信息变化后重新定位（固定宽度下仅首帧需要；Render 优先级等布局完成）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isEmbedded && _widgetVisible)
            {
                Reposition();
            }
        }), DispatcherPriority.Render);
    }

    /// <summary>
    /// 从任务栏分离（恢复为普通窗口或关闭）。
    /// 幂等：未嵌入时直接返回。
    /// </summary>
    public void DetachFromTaskbar()
    {
        if (!_isEmbedded) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 先解除父子关系，再恢复样式（顺序不可反：样式恢复为顶层后再解除父，窗口会闪现在桌面）
        SetParent(hwnd, IntPtr.Zero);
        SetWindowLong(hwnd, GWL_STYLE, _originalStyle);
        SetWindowLong(hwnd, GWL_EXSTYLE, _originalExStyle);
        Log("已从任务栏分离");

        _taskbarHwnd = IntPtr.Zero;
        _isEmbedded = false;
    }
}
