using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Toolbox.Controls;
using Toolbox.Tools.Helpers;

namespace Toolbox.Tools.Services;

/// <summary>贴边方向。</summary>
public enum DockDirection { Left, Right }

/// <summary>贴边状态机。</summary>
public enum DockState { Free, Docking, Docked, Expanding, Expanded }

/// <summary>
/// 悬浮窗贴边自动缩入服务（单例级，由 Manager 持有）。
/// 独立于具体窗口实例，在窗口替换（毛玻璃/大小模式切换）时通过 Attach/Detach 切换绑定。
/// </summary>
public class EdgeDockService
{
    // ── 可配置常量 ──

    /// <summary>触发条露出宽度（px）。</summary>
    public const double TriggerBarWidth = 14;

    /// <summary>判定贴边的边缘距离阈值（px），可由 Manager 按窗口类型设置不同值。</summary>
    public double EdgeThreshold { get; set; } = 10;

    /// <summary>缩入/展开动画时长（ms）。</summary>
    private const int AnimationDurationMs = 250;

    /// <summary>动画帧间隔（ms，约 60fps）。</summary>
    private const int FrameIntervalMs = 16;

    /// <summary>缩回防抖延迟（ms）。</summary>
    private const int CollapseDelayMs = 300;

    // ── 持久化状态（窗口替换后保留）──

    /// <summary>贴边功能全局开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>当前状态。</summary>
    public DockState State { get; private set; } = DockState.Free;

    /// <summary>当前贴边方向。</summary>
    public DockDirection Direction { get; private set; }

    /// <summary>展开状态时窗口的 Left（用于缩回后复原）。</summary>
    public double SavedLeft { get; private set; }

    /// <summary>展开状态时窗口的 Top（缩回后保持纵坐标不变）。</summary>
    public double SavedTop { get; private set; }

    // ── 临时引用（窗口替换时更新）──

    private Window? _window;
    private MusicContentControl? _content;
    private DockTriggerBar? _triggerBar;

    // ── 动画 ──
    private bool _isAnimating;
    private DispatcherTimer? _animationTimer;

    // ── 缩回防抖 ──
    private DispatcherTimer? _collapseTimer;

    // ── 事件处理程序（用于解绑）──
    private EventHandler? _dragCompletedHandler;

    // ── 公开接口 ─────────────────────────────────────

    /// <summary>
    /// 绑定到窗口实例。窗口创建后调用，自动恢复上次的状态。
    /// Manager 需传入拖拽完成的回调（在其中调用 OnDragCompleted）。
    /// </summary>
    public void Attach(Window window, MusicContentControl content, DockTriggerBar triggerBar,
        EventHandler? dragCompletedHandler)
    {
        Detach();

        _window = window;
        _content = content;
        _triggerBar = triggerBar;

        // 保存引用供 Detach 解绑
        _dragCompletedHandler = dragCompletedHandler;

        // 订阅拖拽完成事件（两个 Window 子类都暴露 DragMoveCompleted）
        if (dragCompletedHandler != null)
        {
            if (window is Views.AcrylicMusicWindow aw)
                aw.DragMoveCompleted += dragCompletedHandler;
            else if (window is Views.TransparentMusicWindow tw)
                tw.DragMoveCompleted += dragCompletedHandler;
        }

        // 注入 EdgeDockService 引用到 Window（用于 MouseLeave 缩回）
        if (window is Views.AcrylicMusicWindow aw2)
            aw2.SetEdgeDockService(this);
        else if (window is Views.TransparentMusicWindow tw2)
            tw2.SetEdgeDockService(this);

        // 触发条悬停 → 展开
        triggerBar.MouseEnter += OnTriggerBarMouseEnter;
        triggerBar.MouseLeave += OnTriggerBarMouseLeave;

        // 触发条拖拽 → 脱离贴边 + 启动窗口拖拽
        triggerBar.DragRequested += OnTriggerBarDragRequested;

        // 窗口鼠标进入 → 取消缩回定时器；离开 → 安排缩回
        window.MouseEnter += OnWindowMouseEnter;
        window.MouseLeave += OnWindowMouseLeave;

        // 应用当前状态（Docked 时恢复位置）
        ApplySavedState();
    }

    /// <summary>解绑当前窗口，在窗口关闭/替换前调用。</summary>
    public void Detach()
    {
        StopAnimation();
        _collapseTimer?.Stop();
        _collapseTimer = null;

        if (_window != null)
        {
            _window.MouseEnter -= OnWindowMouseEnter;
            _window.MouseLeave -= OnWindowMouseLeave;

            if (_dragCompletedHandler != null)
            {
                if (_window is Views.AcrylicMusicWindow aw)
                    aw.DragMoveCompleted -= _dragCompletedHandler;
                else if (_window is Views.TransparentMusicWindow tw)
                    tw.DragMoveCompleted -= _dragCompletedHandler;
            }
        }

        if (_triggerBar != null)
        {
            _triggerBar.MouseEnter -= OnTriggerBarMouseEnter;
            _triggerBar.MouseLeave -= OnTriggerBarMouseLeave;
            _triggerBar.DragRequested -= OnTriggerBarDragRequested;
        }

        _window = null;
        _content = null;
        _triggerBar = null;
        _dragCompletedHandler = null;
    }

    /// <summary>DragMove 结束后调用，检测贴边。</summary>
    public void OnDragCompleted()
    {
        if (!Enabled || _window == null) return;

        // 取消可能正在跑的缩回定时器（防止 DragMove 期间到期触发 StartDock）
        _collapseTimer?.Stop();

        // 保存用户拖拽的最终位置（可能已被定时器触发的动画移动）
        double dragLeft = _window.Left;
        double dragTop = _window.Top;

        // 如果定时器在 DragMove 期间已触发贴边，回退
        if (State == DockState.Docked || State == DockState.Docking)
        {
            StopAnimation();
            SetContentVisible(true);
        }

        // 恢复到拖拽实际位置
        _window.Left = dragLeft;
        _window.Top = dragTop;

        var (dpiX, _) = GetDpiScale();
        var wa = GetMonitorWorkAreaDips(dpiX);

        double windowWidth = _window.Width;
        double windowRight = dragLeft + windowWidth;

        if (dragLeft <= wa.Left + EdgeThreshold)
        {
            StartDock(DockDirection.Left, wa.Left);
        }
        else if (windowRight >= wa.Right - EdgeThreshold)
        {
            StartDock(DockDirection.Right, wa.Right);
        }
        else
        {
            SavedLeft = dragLeft;
            SavedTop = dragTop;
            State = DockState.Free;
        }
    }

    /// <summary>展开窗口（鼠标悬停触发条时调用）。</summary>
    public void Expand()
    {
        if (!Enabled) return;
        if (_isAnimating || _window == null) return;
        if (State != DockState.Docked) return;

        _collapseTimer?.Stop();

        // 展开只到全程的 2/3，不完全滑出
        double dockedLeft = _window.Left;
        double targetLeft = dockedLeft + (SavedLeft - dockedLeft) * (2.0 / 3.0);

        // 触发条瞬间消失，窗口内容出现 → 然后窗口滑出 2/3
        SetContentVisible(true);

        State = DockState.Expanding;
        DoAnimation(dockedLeft, targetLeft, SavedTop, () =>
        {
            State = DockState.Expanded;
            _isAnimating = false;

            // 动画期间 MouseLeave 被拦截，完成后根据鼠标实际位置决定是否缩回
            if (_window?.IsMouseOver != true)
                StartCollapseTimer();
        });
    }

    /// <summary>强制脱离贴边，无动画瞬移回保存位置（开关关闭时调用）。</summary>
    public void ForceRestore()
    {
        _collapseTimer?.Stop();
        StopAnimation();
        _isAnimating = false;

        if (_window != null && (State == DockState.Docked || State == DockState.Docking
            || State == DockState.Expanding || State == DockState.Expanded))
        {
            double restoreLeft = (Math.Abs(SavedLeft) > 0.001) ? SavedLeft : _window.Left;
            double restoreTop = (Math.Abs(SavedTop) > 0.001) ? SavedTop : _window.Top;

            _window.Left = restoreLeft;
            _window.Top = restoreTop;

            SetContentVisible(true);
            State = DockState.Free;
        }
        else
        {
            State = DockState.Free;
        }
    }

    /// <summary>从 Docked 状态脱离（触发条拖拽时用到），无动画。</summary>
    public void DetachFromDock()
    {
        if (_window == null) return;

        if (State == DockState.Docked)
        {
            // 先移动窗口到可见位置，再显示内容，避免内容在屏幕外闪现
            _window.Left = SavedLeft;
            _window.Top = SavedTop;
            SetContentVisible(true);
            State = DockState.Free;
        }
    }

    /// <summary>取消缩回定时器（鼠标重新进入窗口时由外部调用）。</summary>
    public void CancelScheduledCollapse()
    {
        _collapseTimer?.Stop();
    }

    // ── 事件处理 ──────────────────────────────────

    private void OnTriggerBarMouseEnter(object? sender, EventArgs e)
    {
        CancelScheduledCollapse();
        if (Enabled && State == DockState.Docked)
            Expand();
    }

    private void OnTriggerBarMouseLeave(object? sender, EventArgs e)
    {
        // 正在展开中，触发条刚被 SetContentVisible 隐藏，本次离开是虚假的，忽略
        if (State == DockState.Expanding) return;
        ScheduleCollapse();
    }

    private void OnTriggerBarDragRequested(object? sender, EventArgs e)
    {
        if (_window == null || !Enabled) return;

        // 从贴边状态脱离，瞬移到保存位置
        if (State == DockState.Docked)
            DetachFromDock();

        // 启动窗口拖拽（后台状态下 HWND 可能失效，需保护）
        try
        {
            _window.DragMove();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EdgeDockService] 触发条 DragMove 失败: {ex.Message}");
        }

        // DragMove 从触发条事件发起，不会触发窗口的 DragMoveCompleted。
        // 手动调用 OnDragCompleted 检测贴边。
        OnDragCompleted();
    }

    private void OnWindowMouseEnter(object? sender, EventArgs e)
    {
        CancelScheduledCollapse();
    }

    private void OnWindowMouseLeave(object? sender, EventArgs e)
    {
        // 展开动画中改 Window.Left 会触发虚假 MouseLeave，等动画完成时再判断
        if (State == DockState.Expanding) return;
        ScheduleCollapse();
    }

    // ── 内部 ─────────────────────────────────────────

    private void ApplySavedState()
    {
        if (_window == null) return;

        if (State == DockState.Docked)
        {
            var (dpiX, _) = GetDpiScale();
            var wa = GetMonitorWorkAreaDips(dpiX);

            double edgeX = Direction == DockDirection.Left ? wa.Left : wa.Right;

            double targetLeft = Direction == DockDirection.Left
                ? edgeX - _window.Width + TriggerBarWidth
                : edgeX - TriggerBarWidth;

            _window.Left = targetLeft;
            _window.Top = SavedTop;

            SetContentVisible(false);
            if (_triggerBar != null)
            {
                _triggerBar.SetDirection(Direction);
                _triggerBar.Visibility = Visibility.Visible;
            }

            State = DockState.Docked;
        }
        else if (State == DockState.Expanded)
        {
            _window.Left = SavedLeft;
            _window.Top = SavedTop;
            SetContentVisible(true);
            State = DockState.Expanded;
        }
    }

    private void StartDock(DockDirection direction, double edgeX, bool savePosition = true)
    {
        if (_isAnimating || _window == null) return;

        // 仅在用户主动拖拽贴边时保存完全展开的参考位置
        if (savePosition)
        {
            // 如果窗口已在屏幕外（上次关闭时处于贴边状态），用默认可见位置作为展开参考
            bool isOffScreen = direction == DockDirection.Left
                ? _window.Left < edgeX
                : _window.Left + _window.Width > edgeX;

            if (isOffScreen)
            {
                SavedLeft = direction == DockDirection.Left
                    ? edgeX + 20
                    : edgeX - _window.Width - 20;
            }
            else
            {
                SavedLeft = _window.Left;
            }
            SavedTop = _window.Top;
        }

        Direction = direction;
        State = DockState.Docking;

        double targetLeft = direction == DockDirection.Left
            ? edgeX - _window.Width + TriggerBarWidth
            : edgeX - TriggerBarWidth;

        SetContentVisible(false);

        double fromLeft = _window.Left; // 动画起点的实际窗口位置
        DoAnimation(fromLeft, targetLeft, SavedTop, () =>
        {
            State = DockState.Docked;
            _isAnimating = false;

            if (_triggerBar != null)
            {
                _triggerBar.SetDirection(direction);
                _triggerBar.Visibility = Visibility.Visible;
            }
        });
    }

    private void ScheduleCollapse()
    {
        if (!Enabled) return;

        if (State == DockState.Expanded)
        {
            StartCollapseTimer();
        }
    }

    private void StartCollapseTimer()
    {
        if (_window == null) return;
        _collapseTimer?.Stop();
        _collapseTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(CollapseDelayMs),
            DispatcherPriority.Normal,
            (s, e) =>
            {
                _collapseTimer?.Stop();
                if (_window == null) return;
                if (State != DockState.Expanded) return;

                var (dpiX, _) = GetDpiScale();
                var wa = GetMonitorWorkAreaDips(dpiX);
                double edgeX = Direction == DockDirection.Left ? wa.Left : wa.Right;
                StartDock(Direction, edgeX, savePosition: false);
            },
            _window.Dispatcher);
        _collapseTimer.Start();
    }

    /// <summary>DispatcherTimer 驱动的 EaseOutCubic 窗口位移动画。</summary>
    private void DoAnimation(double fromLeft, double toLeft, double top, Action onCompleted)
    {
        if (_window == null) return;

        StopAnimation();
        _isAnimating = true;

        var startTime = DateTime.Now;
        var duration = TimeSpan.FromMilliseconds(AnimationDurationMs);

        _animationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(FrameIntervalMs),
            DispatcherPriority.Normal,
            (s, e) =>
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var progress = Math.Min(elapsed / AnimationDurationMs, 1.0);

                // EaseOutCubic: f(t) = 1 - (1-t)^3
                var eased = 1.0 - Math.Pow(1.0 - progress, 3);

                _window.Left = fromLeft + (toLeft - fromLeft) * eased;
                _window.Top = top;

                if (progress >= 1.0)
                {
                    StopAnimation();
                    _window.Left = toLeft;
                    onCompleted();
                }
            },
            _window.Dispatcher);
        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        if (_animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer = null;
        }
        _isAnimating = false;
    }

    private void SetContentVisible(bool visible)
    {
        if (_content != null)
        {
            // 用 Opacity + IsHitTestVisible 替代 Visibility.Collapsed，
            // 避免从视觉树移除导致的虚假 MouseLeave 事件和布局重算
            _content.Opacity = visible ? 1 : 0;
            _content.IsHitTestVisible = visible;
        }
        if (_triggerBar != null)
        {
            _triggerBar.Opacity = visible ? 0 : 1;
            _triggerBar.IsHitTestVisible = !visible;
        }
    }

    private (double ScaleX, double ScaleY) GetDpiScale()
    {
        if (_window == null) return (1.0, 1.0);
        var source = PresentationSource.FromVisual(_window);
        if (source?.CompositionTarget == null) return (1.0, 1.0);
        return (
            source.CompositionTarget.TransformToDevice.M11,
            source.CompositionTarget.TransformToDevice.M22
        );
    }

    /// <summary>
    /// 获取窗口所在屏幕的工作区（DIP 坐标）。
    /// MonitorHelper 返回物理像素，需除以 DPI 缩放。
    /// </summary>
    private MonitorHelper.Rect GetMonitorWorkAreaDips(double dpiX)
    {
        if (_window == null) return new MonitorHelper.Rect(0, 0, 0, 0);

        var phys = MonitorHelper.GetMonitorWorkArea(_window);
        return new MonitorHelper.Rect(
            phys.Left / dpiX,
            phys.Top / dpiX,
            phys.Width / dpiX,
            phys.Height / dpiX);
    }
}