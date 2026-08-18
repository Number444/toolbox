using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Toolbox.Core.Models;
using Toolbox.Plugins.Controls;
using Toolbox.Plugins.Services;
using Toolbox.Tools.Helpers;
using Toolbox.Tools.Models;
using Toolbox.Tools.Services;
using Windows.Media.Control;

namespace Toolbox.Tools.Views;

/// <summary>
/// 音乐悬浮窗管理器（单例）。
/// 共享 SMTC 监听器，管理透明/毛玻璃两种窗口的创建与切换。
/// </summary>
public class MusicFloatWindowManager : IMusicFloatController
{
    private static readonly Lazy<MusicFloatWindowManager> _instance = new(() => new MusicFloatWindowManager());
    public static MusicFloatWindowManager Instance => _instance.Value;

    private readonly SMTCListener _listener = new();
    private readonly EdgeDockService _dockService = new();
    // SMTC 事件（后台线程）写入，UI 线程与 PeekNowPlaying 读取。
    // volatile 保证跨线程可见性；引用赋值天然原子，允许短暂读到"新曲名+旧艺术家"
    // 的混合快照（下一轮刷新自愈），因此不加锁。
    private volatile NowPlayingInfo _cachedInfo = new();

    /// <summary>贴边服务，供外部（NeteaseMusicTool）访问。</summary>
    public EdgeDockService DockService => _dockService;

    private Window? _activeWindow;
    private bool _isVisible;
    private bool _blurEnabled = true;
    private FloatSizeMode _sizeMode = FloatSizeMode.Large;
    private bool _isLocked;

    // ── 任务栏嵌入模式（与桌面悬浮窗独立，可同时存在）──
    private TaskbarMusicWindow? _taskbarWindow;
    private bool _taskbarVisible;
    private TaskbarMediaPopupWindow? _mediaPopup;
    // 卡片关闭时刻：用于抑制"点迷你控件想关卡片，但卡片已因失焦关闭，同一击又把卡片打开"的竞态
    private DateTime _lastPopupClosedUtc = DateTime.MinValue;

    /// <summary>当前活跃窗口是否可见。</summary>
    public bool IsVisible => _isVisible && _activeWindow != null;

    /// <summary>
    /// 被动窥探当前播放信息（供首页仪表盘读取）：单例未创建过（悬浮窗从未开启）
    /// 时返回 null，绝不触发单例实例化——仪表盘不得有"读一下就把监听拉起来"的副作用。
    /// 允许读到短暂的混合快照（新曲名+旧艺术家），下一轮刷新自愈，属预期行为。
    /// </summary>
    public static NowPlayingInfo? PeekNowPlaying() =>
        _instance.IsValueCreated ? Instance._cachedInfo : null;

    /// <summary>当前大小模式。</summary>
    public FloatSizeMode CurrentSizeMode => _sizeMode;

    /// <summary>
    /// 实时读取 SMTC 会话的播放状态（供悬浮窗图标延迟重同步）。
    /// 缓存快照可能携带过渡态旧值，延迟重同步必须读实时状态，拿不到再回退缓存。
    /// </summary>
    public GlobalSystemMediaTransportControlsSessionPlaybackStatus? GetLivePlaybackStatus()
    {
        try
        {
            return _listener.CurrentSession?.GetPlaybackInfo()?.PlaybackStatus
                ?? _cachedInfo.PlaybackStatus;
        }
        catch
        {
            return _cachedInfo.PlaybackStatus;
        }
    }

    /// <summary>可见性变化事件，供工具面板同步胶囊开关状态。</summary>
    public event EventHandler<bool>? VisibilityChanged;

    private MusicFloatWindowManager()
    {
        _listener.NowPlayingChanged += OnNowPlayingChanged;
        AudioflowSettings.Instance.PropertyChanged += OnFloatSettingChanged;
    }

    // ── 公开操作 ──────────────────────────────────────────

    /// <summary>创建并显示悬浮窗（根据当前设置选择透明/毛玻璃）。</summary>
    public void Show(FloatSizeMode sizeMode, bool blurEnabled)
    {
        _sizeMode = sizeMode;
        _blurEnabled = blurEnabled;

        if (!_listener.IsListening)
            _ = StartListenerSafeAsync();

        // 始终创建新窗口（确保正确的窗口类型）
        var newWindow = CreateWindow();
        PrePositionWindow(newWindow);
        try
        {
            newWindow.Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] 悬浮窗显示失败: {ex.Message}");
            // 回滚：CreateWindow 已把 DockService 挂到新窗口，重新挂回旧窗口（无旧窗口则断开）
            if (_activeWindow != null)
                _dockService.Attach(_activeWindow, GetContentControl(_activeWindow),
                    GetTriggerBar(_activeWindow), OnDragMoveCompleted);
            else
                _dockService.Detach();
            return;
        }

        // 注入缓存的歌曲信息
        if (_cachedInfo.Title != null || _cachedInfo.Artist != null)
            GetContentControl(newWindow).UpdateSongInfo(_cachedInfo);

        // 关闭上一次遗留的窗口（Hide 只隐藏不关闭，重复 Show 会泄漏带活 HWND 的窗口实例）。
        // 先显示新窗口再关闭旧窗口，避免闪烁（与 ToggleBlur/SetSizeMode 替换路径一致）
        if (_activeWindow != null)
        {
            _activeWindow.LocationChanged -= OnWindowMoved;
            _activeWindow.Close();
        }

        _activeWindow = newWindow;
        _activeWindow.LocationChanged += OnWindowMoved;
        _isVisible = true;
        VisibilityChanged?.Invoke(this, true);

        // 启动时检测是否满足贴边条件，自动收起
        newWindow.Dispatcher.BeginInvoke(
            new Action(() => _dockService.OnDragCompleted()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>隐藏当前窗口。</summary>
    public void Hide()
    {
        SaveWindowPosition();
        if (_activeWindow != null)
            _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();
        _activeWindow?.Hide();
        _isVisible = false;
        VisibilityChanged?.Invoke(this, false);
    }

    /// <summary>关闭并清理。</summary>
    public void Close()
    {
        SaveWindowPosition();
        // 关闭任务栏控件（不动持久化设置：退出后重启仍按用户设置恢复）
        CloseTaskbarWindow();
        if (_activeWindow != null)
            _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        _activeWindow?.Close();
        _activeWindow = null;
        _isVisible = false;
        VisibilityChanged?.Invoke(this, false);
    }

    /// <summary>切换毛玻璃效果（透明 ↔ 毛玻璃窗口）。</summary>
    public void ToggleBlur(bool enabled)
    {
        if (_blurEnabled == enabled) return;
        _blurEnabled = enabled;

        if (_activeWindow == null || !_isVisible) return;

        // 保存当前状态
        var savedRight = _activeWindow.Left + _activeWindow.Width;
        var savedTop = _activeWindow.Top;
        var savedLocked = _isLocked;
        var wa = MonitorHelper.GetMonitorWorkAreaDips(_activeWindow);
        var isRightSide = _activeWindow.Left > wa.Left + wa.Width / 2;

        _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();

        // 创建新窗口
        var newWindow = CreateWindow();
        newWindow.Left = _activeWindow.Left;
        newWindow.Top = savedTop;
        SetLocked(newWindow, savedLocked);

        // 先显示新窗口再关闭旧窗口，避免闪烁
        try
        {
            newWindow.Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] 切换毛玻璃窗口显示失败: {ex.Message}");
            // 回滚：旧窗口保持完整功能（恢复位置保存订阅与贴边挂载，Attach 内部自带 Detach）
            _activeWindow.LocationChanged += OnWindowMoved;
            _dockService.Attach(_activeWindow, GetContentControl(_activeWindow),
                GetTriggerBar(_activeWindow), OnDragMoveCompleted);
            return;
        }
        InjectSongInfo(newWindow);

        // 右侧锚定右边缘，宽度可能因 size mode 不同而变
        if (isRightSide)
            newWindow.Left = savedRight - newWindow.Width;

        _activeWindow.Close();
        _activeWindow = newWindow;
        _activeWindow.LocationChanged += OnWindowMoved;
    }

    /// <summary>切换大小模式（通过窗口替换，避免在同一窗口内 resize 导致 DWM 渲染问题）。</summary>
    public void SetSizeMode(FloatSizeMode mode)
    {
        if (_sizeMode == mode) return;
        _sizeMode = mode;

        if (_activeWindow == null || !_isVisible) return;

        // 保存当前状态
        var savedRight = _activeWindow.Left + _activeWindow.Width;
        var savedTop = _activeWindow.Top;
        var savedLocked = _isLocked;
        var wa = MonitorHelper.GetMonitorWorkAreaDips(_activeWindow);
        var isRightSide = _activeWindow.Left > wa.Left + wa.Width / 2;

        _activeWindow.LocationChanged -= OnWindowMoved;
        _dockService.Detach();

        // 创建同类型新窗口（保持 blur/transparent 不变，只改 SizeMode）
        var newWindow = CreateWindow();
        newWindow.Left = _activeWindow.Left;
        newWindow.Top = savedTop;
        SetLocked(newWindow, savedLocked);

        // 先显示新窗口再关闭旧窗口，避免闪烁
        try
        {
            newWindow.Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] 切换大小模式窗口显示失败: {ex.Message}");
            // 回滚：旧窗口保持完整功能（恢复位置保存订阅与贴边挂载，Attach 内部自带 Detach）
            _activeWindow.LocationChanged += OnWindowMoved;
            _dockService.Attach(_activeWindow, GetContentControl(_activeWindow),
                GetTriggerBar(_activeWindow), OnDragMoveCompleted);
            return;
        }
        InjectSongInfo(newWindow);

        // 右侧锚定右边缘，宽窄模式切换时宽度会变（242↔190）
        if (isRightSide)
            newWindow.Left = savedRight - newWindow.Width;

        _activeWindow.Close();
        _activeWindow = newWindow;
        _activeWindow.LocationChanged += OnWindowMoved;
    }

    /// <summary>设置窗口锁定状态。</summary>
    public void SetWindowLocked(bool locked)
    {
        _isLocked = locked;
        if (_activeWindow != null)
            SetLocked(_activeWindow, locked);
    }

    // ── 播放控制（转发到 SMTC 会话）─────────────────────

    /// <summary>播放/暂停切换（悬浮窗悬停按钮、右键菜单用）。</summary>
    public async void TogglePlayPause()
    {
        try
        {
            var session = _listener.CurrentSession;
            if (session != null) await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] 播放/暂停失败: {ex.Message}");
        }
    }

    /// <summary>下一首。</summary>
    public async void SkipNext()
    {
        try
        {
            var session = _listener.CurrentSession;
            if (session != null) await session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] 下一首失败: {ex.Message}");
        }
    }

    /// <summary>上一首。</summary>
    public async void SkipPrevious()
    {
        try
        {
            var session = _listener.CurrentSession;
            if (session != null) await session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] 上一首失败: {ex.Message}");
        }
    }

    /// <summary>设置窗口位置（预留扩展方法）。</summary>
    public void SetWindowPosition(double left, double top)
    {
        if (_activeWindow != null)
        {
            _activeWindow.Left = left;
            _activeWindow.Top = top;
        }
    }

    // ── 任务栏嵌入模式 ──

    /// <summary>任务栏嵌入控件当前是否可见。</summary>
    public bool IsTaskbarWidgetVisible => _taskbarVisible;

    /// <summary>
    /// 显示任务栏嵌入播放器控件。与桌面悬浮窗独立，可同时存在。
    /// 由设置面板绑定 / 启动恢复经 TaskbarWidgetEnabled 属性驱动，入口处幂等。
    /// </summary>
    public void ShowTaskbarWidget()
    {
        TaskbarLog($"ShowTaskbarWidget 被调用 (listener.IsListening={_listener.IsListening}, 窗口已存在={_taskbarWindow != null})");

        if (!_listener.IsListening)
            _ = StartListenerSafeAsync();

        if (_taskbarWindow == null)
        {
            _taskbarWindow = new TaskbarMusicWindow();
            WireTaskbarPlayback(_taskbarWindow);
        }

        if (!_taskbarVisible)
        {
            _taskbarWindow.Show();
            _taskbarVisible = true;
        }
        else
        {
            // 已显示（如设置反复触发）：仅重新定位
            _taskbarWindow.Reposition();
        }

        // 注入当前歌曲信息（必须在 Show 之后：控件 IsLoaded 前 UpdateSongInfo 会被丢弃）。
        // 与悬浮窗同款条件：空快照也注入，显示"未在播放"
        if (_cachedInfo.Title != null || _cachedInfo.Artist != null)
            _taskbarWindow.UpdateSongInfo(_cachedInfo);

        // 初始即应用"无播放自动隐藏"（等 SMTC 事件前不闪烁"未在播放"）
        ApplyIdleVisibility(_cachedInfo);

        // 与设置保持一致（setter 短路，不会递归）
        AudioflowSettings.Instance.TaskbarWidgetEnabled = true;
    }

    /// <summary>按当前播放信息应用"无播放自动隐藏"（迷你控件 + 卡片）。</summary>
    private void ApplyIdleVisibility(NowPlayingInfo info)
    {
        if (_taskbarWindow == null || !_taskbarVisible) return;

        bool idle = !info.HasSong
            || info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped;

        if (AudioflowSettings.Instance.TaskbarWidgetHideWhenIdle)
        {
            _taskbarWindow.SetWidgetVisible(!idle);
            if (idle) CloseMediaPopup();
        }
        else
        {
            _taskbarWindow.SetWidgetVisible(true);
        }
    }

    /// <summary>
    /// 隐藏任务栏嵌入播放器控件。
    /// </summary>
    public void HideTaskbarWidget()
    {
        CloseTaskbarWindow();

        // 与设置保持一致（setter 短路，不会递归）
        AudioflowSettings.Instance.TaskbarWidgetEnabled = false;
    }

    /// <summary>仅销毁任务栏窗口实例，不动持久化设置（退出清理路径用，避免清掉用户设置）。</summary>
    private void CloseTaskbarWindow()
    {
        CloseMediaPopup();
        if (_taskbarWindow != null)
        {
            _taskbarWindow.DetachFromTaskbar();
            _taskbarWindow.Close();
            _taskbarWindow = null;
            TaskbarLog("任务栏窗口已销毁");
        }
        _taskbarVisible = false;
    }

    /// <summary>关闭媒体卡片（幂等）。</summary>
    private void CloseMediaPopup()
    {
        if (_mediaPopup is { IsVisible: true })
        {
            _mediaPopup.AnimatedClose();
        }
    }

    /// <summary>任务栏控件诊断日志（与 TaskbarMusicWindow 同文件）。</summary>
    private static void TaskbarLog(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Toolbox.Core.Services.AppPaths.DataDir, "taskbar_widget.log");
            System.IO.File.AppendAllText(logPath,
                $"{DateTime.Now:HH:mm:ss.fff} [MusicFloatWindowManager] {message}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    /// <summary>切换任务栏嵌入播放器控件的显示/隐藏。</summary>
    public void ToggleTaskbarWidget()
    {
        if (_taskbarVisible)
            HideTaskbarWidget();
        else
            ShowTaskbarWidget();
    }

    /// <summary>绑定任务栏控件交互（单击弹卡片、拖拽换位后重锚卡片）。</summary>
    private void WireTaskbarPlayback(TaskbarMusicWindow window)
    {
        // 单击 → 弹出/收起媒体卡片
        window.WidgetClicked += ToggleMediaPopup;

        // 拖拽换位完成 → 重新锚定卡片
        window.WidgetMoved += () =>
        {
            if (_mediaPopup is { IsVisible: true })
            {
                _mediaPopup.Dispatcher.BeginInvoke(new Action(() =>
                    _mediaPopup.AnchorTo(window.GetWidgetScreenBounds())));
            }
        };
    }

    // ── 媒体卡片（弹出窗口）──

    /// <summary>切换媒体卡片的弹出/收起。</summary>
    private void ToggleMediaPopup()
    {
        if (_mediaPopup is { IsVisible: true })
        {
            _mediaPopup.AnimatedClose();
            return;
        }

        if (_taskbarWindow == null || !_taskbarVisible) return;

        // 卡片刚因"点击外部（含迷你控件）"失焦关闭时，同一击的 MouseUp 会冒泡成 WidgetClicked，
        // 此时若立即重开就违背用户"点控件收起"的意图 → 400ms 内忽略重开
        if ((DateTime.UtcNow - _lastPopupClosedUtc).TotalMilliseconds < 400) return;

        if (_mediaPopup == null)
        {
            _mediaPopup = new TaskbarMediaPopupWindow();
            _mediaPopup.PopupClosed += () => _lastPopupClosedUtc = DateTime.UtcNow;
            _mediaPopup.OnSkipPrevious += async () =>
            {
                try
                {
                    var session = _listener.CurrentSession;
                    if (session != null) await session.TrySkipPreviousAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MusicFloatWindowManager] 卡片上一首失败: {ex.Message}");
                }
            };
            _mediaPopup.OnTogglePlayPause += async () =>
            {
                try
                {
                    var session = _listener.CurrentSession;
                    if (session != null) await session.TryTogglePlayPauseAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MusicFloatWindowManager] 卡片播放/暂停失败: {ex.Message}");
                }
            };
            _mediaPopup.OnSkipNext += async () =>
            {
                try
                {
                    var session = _listener.CurrentSession;
                    if (session != null) await session.TrySkipNextAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MusicFloatWindowManager] 卡片下一首失败: {ex.Message}");
                }
            };
        }

        _mediaPopup.UpdateSongInfo(_cachedInfo);
        // DPI 从任务栏控件窗口取（它已显示、有句柄）：弹窗 Show 前即可完成预锚定，首帧就在动画起点
        _mediaPopup.Open(_taskbarWindow.GetWidgetScreenBounds(),
            System.Windows.Media.VisualTreeHelper.GetDpi(_taskbarWindow).PixelsPerDip);
    }

    /// <summary>
    /// 同步更新任务栏控件与媒体卡片上的歌曲信息（SMTC 事件后台线程调用，内部 Dispatch 到 UI 线程）。
    /// 同时处理"无播放自动隐藏"：无歌或 Stopped 时隐藏迷你控件并收起卡片。
    /// </summary>
    private void UpdateTaskbarWidget(NowPlayingInfo info)
    {
        if (_taskbarWindow == null || !_taskbarVisible) return;
        try
        {
            _taskbarWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_taskbarWindow == null) return;

                _taskbarWindow.UpdateSongInfo(info);

                // 媒体卡片同步
                if (_mediaPopup is { IsVisible: true })
                {
                    _mediaPopup.UpdateSongInfo(info);
                }

                // 无播放自动隐藏
                ApplyIdleVisibility(info);
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] 任务栏控件更新异常: {ex.Message}");
        }
    }

    /// <summary>设置窗口尺寸（预留扩展方法）。</summary>
    public void SetWindowSize(double width, double height)
    {
        if (_activeWindow != null)
        {
            _activeWindow.Width = width;
            _activeWindow.Height = height;
        }
    }

    // ── 内部 ──────────────────────────────────────────────

    private Window CreateWindow()
    {
        Window window = _blurEnabled
            ? new AcrylicMusicWindow()
            : new TransparentMusicWindow();

        var content = GetContentControl(window);
        content.SizeMode = _sizeMode;

        // 透明窗口：需越界 5px 才触发贴边；毛玻璃窗口：距边缘 10px 即触发
        _dockService.EdgeThreshold = _blurEnabled ? 10 : -5;

        // 挂载 EdgeDockService
        _dockService.Attach(window, content, GetTriggerBar(window), OnDragMoveCompleted);

        // 应用当前点击穿透状态（游戏模式）
        SetClickThrough(window, AudioflowSettings.Instance.ClickThroughEnabled);

        return window;
    }

    private void OnDragMoveCompleted(object? sender, EventArgs e)
    {
        _dockService.OnDragCompleted();
    }

    private static MusicContentControl GetContentControl(Window window) =>
        window switch
        {
            TransparentMusicWindow tw => tw.MusicContent,
            AcrylicMusicWindow aw => aw.MusicContent,
            _ => throw new InvalidOperationException("Unknown window type")
        };

    private static DockTriggerBar GetTriggerBar(Window window) =>
        window switch
        {
            TransparentMusicWindow tw => tw.TriggerBar,
            AcrylicMusicWindow aw => aw.TriggerBar,
            _ => throw new InvalidOperationException("Unknown window type")
        };

    private static void SetLocked(Window window, bool locked)
    {
        switch (window)
        {
            case TransparentMusicWindow tw: tw.SetWindowLocked(locked); break;
            case AcrylicMusicWindow aw: aw.SetWindowLocked(locked); break;
        }
    }

    private void InjectSongInfo(Window window)
    {
        if (_cachedInfo.Title != null || _cachedInfo.Artist != null)
            GetContentControl(window).UpdateSongInfo(_cachedInfo);
    }

    private void PrePositionWindow(Window window)
    {
        var settings = AudioflowSettings.Instance;

        if (!double.IsNaN(settings.FloatWindowLeft) && !double.IsNaN(settings.FloatWindowTop))
        {
            // 防丢窗口：持久化位置完全落在虚拟屏幕外时（拔副屏/分辨率变小），
            // 放弃恢复，改用默认位置
            double w = double.IsNaN(window.Width) ? 242 : window.Width;
            double h = double.IsNaN(window.Height) ? 252 : window.Height;
            var saved = new Rect(settings.FloatWindowLeft, settings.FloatWindowTop, w, h);
            var virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

            if (!Rect.Intersect(saved, virtualScreen).IsEmpty)
            {
                window.Left = settings.FloatWindowLeft;
                window.Top = settings.FloatWindowTop;
                return;
            }
        }

        // 使用已知最终尺寸预设位置，避免 Show() 后在 (0,0) 闪现。
        // 基于主屏工作区定位（避开任务栏），多显示器下也安全。
        double defaultH = _sizeMode == FloatSizeMode.Large ? 252 : 96;
        var workArea = SystemParameters.WorkArea;
        window.Left = workArea.Left + 20;
        window.Top = workArea.Top + (workArea.Height - defaultH) / 2;
    }

    /// <summary>安全启动 SMTC 监听，记录启动失败异常。</summary>
    private async Task StartListenerSafeAsync()
    {
        try
        {
            await _listener.StartAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] SMTC 监听启动失败: {ex.Message}");
        }
    }

    private void SaveWindowPosition()
    {
        // 贴边/动画状态下窗口坐标位于屏幕边缘外，不应作为用户位置持久化
        if (_activeWindow == null || _dockService.State != DockState.Free) return;
        var settings = AudioflowSettings.Instance;
        settings.FloatWindowLeft = _activeWindow.Left;
        settings.FloatWindowTop = _activeWindow.Top;
        settings.Save();
    }

    /// <summary>将悬浮窗复位到默认位置（所在显示器工作区垂直居中，距左 20 像素）。</summary>
    public void ResetPosition()
    {
        if (_activeWindow == null || !_isVisible) return;

        // 贴边缩入状态下内容不可见（Opacity=0、IsHitTestVisible=false），直接移动窗口会留下
        // "内容不可见 + 触发条悬空"的死态。先解除贴边：恢复内容可见、隐藏触发条、回到 Free 态
        if (_dockService.State != DockState.Free)
            _dockService.ForceRestore();

        var wa = MonitorHelper.GetMonitorWorkAreaDips(_activeWindow);
        _activeWindow.Left = wa.Left + 20;
        _activeWindow.Top = wa.Top + (wa.Height - _activeWindow.Height) / 2;

        SaveWindowPosition();
    }

    /// <summary>监听窗口位置变化，实时保存位置到 audioflow.json。</summary>
    private void OnWindowMoved(object? sender, EventArgs e)
    {
        if (_activeWindow == null) return;
        // 贴边缩入/展开动画会瞬间把窗口推到屏幕外，这些坐标不是用户意图，跳过
        if (_dockService.State != DockState.Free) return;
        var settings = AudioflowSettings.Instance;
        settings.FloatWindowLeft = _activeWindow.Left;
        settings.FloatWindowTop = _activeWindow.Top;
    }

    /// <summary>悬浮窗设置项变更回调。</summary>
    private void OnFloatSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 这些设置也可从悬浮窗右键菜单修改，Manager 自行响应，
        // 不依赖 NeteaseMusicTool 面板是否已创建（面板侧的处理为幂等重复，无害）
        if (e.PropertyName == nameof(AudioflowSettings.FloatWindowBlurEnabled))
        {
            ToggleBlur(AudioflowSettings.Instance.FloatWindowBlurEnabled);
        }
        else if (e.PropertyName == nameof(AudioflowSettings.LockFloatWindow))
        {
            SetWindowLocked(AudioflowSettings.Instance.LockFloatWindow);
        }
        else if (e.PropertyName == nameof(AudioflowSettings.EdgeDockEnabled))
        {
            _dockService.Enabled = AudioflowSettings.Instance.EdgeDockEnabled;
            if (!_dockService.Enabled)
                _dockService.ForceRestore();
        }
        else if (e.PropertyName == nameof(AudioflowSettings.ClickThroughEnabled))
        {
            var enabled = AudioflowSettings.Instance.ClickThroughEnabled;
            SetClickThrough(_activeWindow, enabled);
            if (_activeWindow == null || !_isVisible) return;

            if (enabled)
            {
                // 若正处于贴边缩入状态（内容隐藏只露触发条），先还原窗口，
                // 否则穿透下无法悬停展开，窗口会永远保持"一条缝"的外观
                _dockService.ForceRestore();
                _dockService.Detach(); // 穿透下贴边无效，主动断开
            }
            else if (AudioflowSettings.Instance.EdgeDockEnabled)
            {
                // 关闭穿透时恢复贴边挂载（Attach 内部自带 Detach，可安全重挂）
                _dockService.Attach(_activeWindow, GetContentControl(_activeWindow),
                    GetTriggerBar(_activeWindow), OnDragMoveCompleted);
            }
        }
        else if (e.PropertyName == nameof(AudioflowSettings.TaskbarWidgetEnabled))
        {
            // 设置面板 / 启动恢复统一入口（属性 setter 短路，无递归）
            if (AudioflowSettings.Instance.TaskbarWidgetEnabled)
                ShowTaskbarWidget();
            else
                HideTaskbarWidget();
        }
        else if (e.PropertyName == nameof(AudioflowSettings.TaskbarWidgetPosition))
        {
            // 位置切换：重新定位（父窗口恒为 Shell_TrayWnd，无需重新嵌入）+ 卡片重锚定
            if (_taskbarWindow != null && _taskbarVisible)
            {
                _taskbarWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _taskbarWindow.Reposition();
                    if (_mediaPopup is { IsVisible: true })
                    {
                        _mediaPopup.AnchorTo(_taskbarWindow.GetWidgetScreenBounds());
                    }
                }));
            }
        }
    }

    private static void SetClickThrough(Window? window, bool enabled)
    {
        if (window == null) return;
        switch (window)
        {
            case AcrylicMusicWindow aw: aw.SetClickThrough(enabled); break;
            case TransparentMusicWindow tw: tw.SetClickThrough(enabled); break;
        }
    }

    private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
    {
        _cachedInfo = info;

        // 同步任务栏控件（必须在提前 return 之前；内部自判窗口存在性）
        UpdateTaskbarWidget(info);

        if (_activeWindow == null || !_isVisible) return;
        try
        {
            // SMTCListener 事件在后台线程触发，必须 Dispatch 到 UI 线程
            _activeWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeWindow == null) return;
                GetContentControl(_activeWindow).UpdateSongInfo(info);
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicFloatWindowManager] SMTC 回调处理异常: {ex.Message}");
        }
    }
}
