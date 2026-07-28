using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.Media.Control;
using Toolbox.Tools.Models;

namespace Toolbox.Tools.Services;

/// <summary>
/// SMTC（SystemMediaTransportControls）监听器。
/// 通过 Windows 原生 API 监听网易云音乐等应用的媒体播放状态，
/// 实时获取歌曲标题、歌手、封面缩略图和播放进度等信息。
/// </summary>
public sealed class SMTCListener : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _isClosing;
    private int _refreshSequence; // 递增的快照版本号

    // 修复 1：SemaphoreSlim 串行化所有刷新，消除并发竞态
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // 修复 3(b)：记录当前歌曲身份，用于重试取消判断（而非全局版本号）
    private string _currentSongId = string.Empty;

    // 陈旧封面候选字节：切歌时被判定为上一首封面的字节快照，供重试验证
    private byte[]? _staleThumbnailCandidate;

    // ── 生命周期防护（启动重试 / 看门狗 / 休眠唤醒恢复）──
    private bool _disposed;
    private int _startInProgress;      // Interlocked 守卫，防止并发 Start 造成多重订阅
    private int _powerModeSubscribed;  // Interlocked 守卫，防止重复订阅电源事件
    private System.Threading.Timer? _watchdogTimer;
    private CancellationTokenSource? _lifecycleCts; // Stop/Dispose 时取消进行中的启动重试
    private DateTime _lastEventUtc = DateTime.MinValue; // 最近一次收到任何 SMTC 会话事件的时间

    /// <summary>当前播放信息（变化时更新）。</summary>
    public NowPlayingInfo CurrentInfo { get; private set; } = new();

    /// <summary>是否正在监听。</summary>
    public bool IsListening => _manager != null;

    /// <summary>当前 SMTC 会话（可为 null）。用于外部执行播放控制。</summary>
    public GlobalSystemMediaTransportControlsSession? CurrentSession => _session;

    /// <summary>
    /// 播放信息变更事件。仅在 SemaphoreSlim 保护下触发，无并发冲突。
    /// 注意：此事件在 MTA 线程触发，订阅方若需更新 WPF UI 应自行 Dispatch。
    /// </summary>
    public event EventHandler<NowPlayingInfo>? NowPlayingChanged;

    // 刷新范围枚举：分离事件处理职责（修复 4）
    private enum RefreshScope { Full, TimelineOnly }

    /// <summary>
    /// 异步启动监听：请求 SMTC SessionManager 并订阅当前会话的事件。
    /// 幂等：已在监听或已有启动流程进行中时直接返回。
    /// WinRT 调用失败时按 5s → 15s → 30s（封顶）退避自动重试，
    /// 直到成功、Stop 或 Dispose。
    /// </summary>
    public async Task StartAsync()
    {
        if (_disposed || _manager != null) return;
        if (Interlocked.CompareExchange(ref _startInProgress, 1, 0) != 0) return;

        try
        {
            _isClosing = false; // 允许 Stop 之后重新启动（看门狗/唤醒重建依赖此语义）
            _lifecycleCts?.Dispose();
            _lifecycleCts = new CancellationTokenSource();
            var token = _lifecycleCts.Token;
            SubscribePowerModeChanged();

            int attempt = 0;
            while (!_disposed && !_isClosing && _manager == null)
            {
                try
                {
                    _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    // 必须过滤目标应用（网易云），不能直接用 GetCurrentSession()——
                    // 它返回的是"当前会话"，可能是浏览器等任意媒体源
                    SubscribeToSession(FindTargetSession());

                    // 订阅会话列表变化，当网易云音乐启动/关闭时重新匹配
                    _manager.SessionsChanged += OnSessionsChanged;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SMTCListener] 启动失败（第 {attempt + 1} 次）: {ex.Message}");
                    // 部分订阅可能已完成，回滚到干净状态再重试
                    UnsubscribeFromSession(_session);
                    _session = null;
                    if (_manager != null)
                    {
                        try { _manager.SessionsChanged -= OnSessionsChanged; } catch { /* 忽略回滚异常 */ }
                        _manager = null;
                    }

                    // 退避重试：5s → 15s → 30s 封顶
                    int delayMs = attempt == 0 ? 5000 : attempt == 1 ? 15000 : 30000;
                    attempt++;
                    try
                    {
                        await Task.Delay(delayMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // Stop/Dispose 取消了重试
                    }
                }
            }

            EnsureWatchdog();
        }
        finally
        {
            Interlocked.Exchange(ref _startInProgress, 0);
        }
    }

    /// <summary>在所有会话中查找目标应用（网易云音乐）的会话，未找到返回 null。</summary>
    private GlobalSystemMediaTransportControlsSession? FindTargetSession()
    {
        if (_manager == null) return null;
        foreach (var s in _manager.GetSessions())
        {
            if (IsTargetSession(s)) return s;
        }
        return null;
    }

    /// <summary>
    /// 停止监听并释放资源（含看门狗、电源事件订阅和进行中的启动重试）。
    /// 幂等，可重复调用；之后可再次 StartAsync 重建监听。
    /// </summary>
    public void Stop()
    {
        _isClosing = true;

        // 取消进行中的启动重试
        try { _lifecycleCts?.Cancel(); } catch (ObjectDisposedException) { /* 已释放 */ }

        // 停止看门狗
        _watchdogTimer?.Dispose();
        _watchdogTimer = null;

        // 退订电源事件（防止 Stop/Start 循环造成重复订阅）
        if (Interlocked.Exchange(ref _powerModeSubscribed, 0) == 1)
        {
            try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; }
            catch (Exception ex) { Debug.WriteLine($"[SMTCListener] 退订电源事件失败: {ex.Message}"); }
        }

        UnsubscribeFromSession(_session);
        if (_manager != null)
        {
            try { _manager.SessionsChanged -= OnSessionsChanged; }
            catch (Exception ex) { Debug.WriteLine($"[SMTCListener] 退订 SessionsChanged 失败: {ex.Message}"); }
        }
        _manager = null;
        _session = null;
    }

    public void Dispose()
    {
        if (_disposed) return; // 幂等
        _disposed = true;
        Stop();
        _lifecycleCts?.Dispose();
        _refreshLock.Dispose();
    }

    // ── 生命周期防护：看门狗 + 休眠唤醒 ────────────────────

    /// <summary>启动低频看门狗（30s 周期），幂等。</summary>
    private void EnsureWatchdog()
    {
        if (_disposed || _watchdogTimer != null) return;
        _watchdogTimer = new System.Threading.Timer(
            WatchdogTick, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 看门狗：发现管理器失效/长时间无会话事件/订阅断链迹象时自动重建监听。
    /// 启发式保持简单：能枚举会话即视为管理器存活；无目标会话时顺带重新匹配一次，
    /// 兜底遗漏的 SessionsChanged 事件。
    /// </summary>
    private void WatchdogTick(object? state)
    {
        if (_disposed || _isClosing) return;
        try
        {
            if (_manager == null)
            {
                // 管理器缺失（启动失败或被置空）→ 触发重建（已在重试中时幂等返回）
                _ = StartAsync();
                return;
            }

            // 探测管理器活性：枚举会话抛异常视为失效
            try
            {
                _ = _manager.GetSessions();
            }
            catch
            {
                RebuildListening("SessionManager 已失效");
                return;
            }

            if (_session == null)
            {
                // 未持有目标会话 → 重新匹配，兜底网易云已启动但事件未触发的情况
                var target = FindTargetSession();
                if (target != null) SubscribeToSession(target);
                return;
            }

            // 持有会话但超过 5 分钟无任何事件 → 探测会话活性，失效则重建
            if (DateTime.UtcNow - _lastEventUtc > TimeSpan.FromMinutes(5))
            {
                try
                {
                    _ = _session.GetPlaybackInfo();
                }
                catch
                {
                    RebuildListening("会话已失效");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTCListener] 看门狗检查异常: {ex.Message}");
        }
    }

    /// <summary>销毁当前订阅并异步重建整个监听。</summary>
    private void RebuildListening(string reason)
    {
        Debug.WriteLine($"[SMTCListener] 重建监听（{reason}）");
        Stop();
        _ = StartAsync();
    }

    /// <summary>订阅系统电源事件（去重），Resume 时重建监听。</summary>
    private void SubscribePowerModeChanged()
    {
        if (Interlocked.CompareExchange(ref _powerModeSubscribed, 1, 0) != 0) return;
        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            // 无消息泵的线程（如测试环境）无法订阅系统事件，回退标记并放弃该能力
            Interlocked.Exchange(ref _powerModeSubscribed, 0);
            Debug.WriteLine($"[SMTCListener] 订阅电源事件失败: {ex.Message}");
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        if (_disposed || _isClosing) return;
        // 唤醒后媒体服务可能尚未就绪，重建失败时由启动重试/看门狗兜底
        RebuildListening("系统休眠唤醒");
    }

    // ── 会话管理 ──────────────────────────────────────────

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        _lastEventUtc = DateTime.UtcNow;
        if (_isClosing) return;

        var target = FindTargetSession();
        if (target != null)
        {
            SubscribeToSession(target);
            return;
        }

        // 没有找到目标会话 → 清除信息
        if (_session != null)
        {
            UnsubscribeFromSession(_session);
            _session = null;
            CurrentInfo = new NowPlayingInfo();
            NowPlayingChanged?.Invoke(this, CurrentInfo);
        }
    }

    private void SubscribeToSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session == null || session == _session) return;

        UnsubscribeFromSession(_session);
        _session = session;

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;

        // 立即拉取一次全量数据
        _ = RefreshNowPlayingAsync(RefreshScope.Full);
    }

    private void UnsubscribeFromSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session == null) return;
        try
        {
            session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        catch (Exception ex)
        {
            // 会话已失效（网易云重启/媒体服务重启）时退订可能抛异常，忽略即可
            Debug.WriteLine($"[SMTCListener] 退订会话事件失败: {ex.Message}");
        }
    }

    // ── 事件处理（按职责分开，修复 4）──────────────────────

    /// <summary>
    /// 媒体属性变更（标题/歌手/封面）→ 全量刷新。
    /// </summary>
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _lastEventUtc = DateTime.UtcNow;
        _ = RefreshNowPlayingAsync(RefreshScope.Full);
    }

    /// <summary>
    /// 进度变更 → 仅刷新 Position/Duration，不重读封面（修复 4）。
    /// </summary>
    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _lastEventUtc = DateTime.UtcNow;
        _ = RefreshNowPlayingAsync(RefreshScope.TimelineOnly);
    }

    /// <summary>
    /// 播放状态变更 → 仅刷新 Position/Duration，不重读封面（修复 4）。
    /// </summary>
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _lastEventUtc = DateTime.UtcNow;
        _ = RefreshNowPlayingAsync(RefreshScope.TimelineOnly);
    }

    // ── 数据拉取（SemaphoreSlim 串行化，修复 1）────────────

    private async Task RefreshNowPlayingAsync(RefreshScope scope)
    {
        if (_isClosing || _session == null) return;

        await _refreshLock.WaitAsync();
        try
        {
            var version = Interlocked.Increment(ref _refreshSequence);

            if (scope == RefreshScope.Full)
            {
                await RefreshFullAsync(version);
            }
            else
            {
                RefreshTimelineOnly(version);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTCListener] RefreshNowPlayingAsync error: {ex.Message}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// 全量刷新：读取 MediaProperties（标题/歌手/封面）+ TimelineProperties（进度）。
    /// SemaphoreSlim 保证串行执行，无需额外版本守卫。
    /// </summary>
    private async Task RefreshFullAsync(int version)
    {
        var mediaProps = await _session!.TryGetMediaPropertiesAsync();
        var timeline = _session.GetTimelineProperties();
        var playbackInfo = _session.GetPlaybackInfo();

        var info = new NowPlayingInfo
        {
            Title = mediaProps?.Title ?? string.Empty,
            Artist = mediaProps?.Artist ?? string.Empty,
            Position = timeline?.Position ?? TimeSpan.Zero,
            Duration = CalcDuration(timeline),
            PlaybackStatus = playbackInfo?.PlaybackStatus,
            RefreshVersion = version,
        };

        // 记录当前歌曲身份（Artist:Title），用于重试取消判断
        _currentSongId = $"{mediaProps?.Artist ?? ""}:{mediaProps?.Title ?? ""}";

        // ── 读取封面字节 ──
        if (mediaProps?.Thumbnail != null)
        {
            try
            {
                using var srcStream = await mediaProps.Thumbnail.OpenReadAsync();
                using var memStream = new MemoryStream();
                await srcStream.AsStream().CopyToAsync(memStream);
                var thumbnailData = memStream.ToArray();

                // 修复 5：陈旧字节检测——切歌后 SMTC 可能仍返回上一首封面
                if (CurrentInfo.ThumbnailData != null
                    && CurrentInfo.ThumbnailData.Length == thumbnailData.Length
                    && CurrentInfo.Title != info.Title   // 确认发生了切歌
                    && ((ReadOnlySpan<byte>)CurrentInfo.ThumbnailData).SequenceEqual(thumbnailData))
                {
                    // 字节与上一首相同 → 新封面未就绪，保存候选供重试验证，按 null 走重试
                    _staleThumbnailCandidate = thumbnailData;
                    thumbnailData = null;
                }
                else
                {
                    // 当前读取的字节非陈旧（或者是第一次读无封面数据），清除候选标记
                    _staleThumbnailCandidate = null;
                }

                info.ThumbnailData = thumbnailData;
            }
            catch
            {
                // 封面读取失败，info.ThumbnailData 保持 null
            }
        }

        // 修复 3(b)：封面未就绪 → 调度歌曲身份绑定的多重退避重试
        if (info.ThumbnailData == null)
        {
            _ = ScheduleThumbnailRetryAsync(_currentSongId);
        }

        CurrentInfo = info;
        NowPlayingChanged?.Invoke(this, info);
    }

    /// <summary>
    /// 仅刷新 Timeline（Position/Duration），不重读封面（修复 4）。
    /// 保留已有 ThumbnailData，避免 seek/暂停时误清封面。
    /// </summary>
    private void RefreshTimelineOnly(int version)
    {
        var timeline = _session!.GetTimelineProperties();
        var playbackInfo = _session.GetPlaybackInfo();

        var info = new NowPlayingInfo
        {
            Title = CurrentInfo.Title,
            Artist = CurrentInfo.Artist,
            Position = timeline?.Position ?? TimeSpan.Zero,
            Duration = CalcDuration(timeline),
            ThumbnailData = CurrentInfo.ThumbnailData, // 保留已有封面，不重读
            PlaybackStatus = playbackInfo?.PlaybackStatus,
            RefreshVersion = version,
        };

        CurrentInfo = info;
        NowPlayingChanged?.Invoke(this, info);
    }

    /// <summary>
    /// 计算 Duration，处理 Edge 等应用可能返回不合理值的情况。
    /// </summary>
    private static TimeSpan CalcDuration(GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline)
    {
        if (timeline == null) return TimeSpan.Zero;
        var d = timeline.EndTime - timeline.MinSeekTime;
        if (d <= TimeSpan.Zero && timeline.MaxSeekTime > timeline.MinSeekTime)
        {
            d = timeline.MaxSeekTime - timeline.MinSeekTime;
        }
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }

    // ── 封面重试 ───────────────────────────────────────────

    /// <summary>
    /// 歌曲身份绑定的多重退避重试（修复 3(b)）。
    /// 取消条件从"全局 sequence 变化"改为"歌曲身份变化"，
    /// 单纯进度/状态事件不会打断封面重试。
    /// 六次退避（增量阶梯）：200ms → 200ms → 400ms → 800ms → 1500ms → 3000ms
    /// 前两次 200ms 覆盖低延迟场景，后逐步拉大兜底慢速场景。
    /// 重试中同样具备陈旧字节检测，避免 SMTC 重复返回上一首封面。
    /// </summary>
    private async Task ScheduleThumbnailRetryAsync(string songId)
    {
        int[] delays = [200, 200, 400, 800, 1500, 3000];

        for (int i = 0; i < delays.Length; i++)
        {
            await Task.Delay(delays[i]);
            if (_isClosing || _session == null) return;
            if (_currentSongId != songId) return; // 歌曲已变，取消重试

            await _refreshLock.WaitAsync();
            try
            {
                // 获得锁后再次确认歌曲身份
                if (_currentSongId != songId) return;

                var mediaProps = await _session.TryGetMediaPropertiesAsync();
                if (mediaProps?.Thumbnail == null) continue; // 仍无封面，尝试下一次

                using var srcStream = await mediaProps.Thumbnail.OpenReadAsync();
                using var memStream = new MemoryStream();
                await srcStream.AsStream().CopyToAsync(memStream);
                var thumbnailData = memStream.ToArray();

                // I/O 完成后再次确认歌曲未变
                if (_currentSongId != songId) return;

                // 陈旧字节检测：如果读取到的字节与刚切歌时的陈旧候选相同，则仍未就绪
                if (_staleThumbnailCandidate != null
                    && _staleThumbnailCandidate.Length == thumbnailData.Length
                    && ((ReadOnlySpan<byte>)_staleThumbnailCandidate).SequenceEqual(thumbnailData))
                {
                    // 仍然是陈旧封面，更新候选并继续重试
                    _staleThumbnailCandidate = thumbnailData;
                    continue;
                }

                // 基于 CurrentInfo 创建仅封面更新的版本
                var updatedInfo = new NowPlayingInfo
                {
                    Title = CurrentInfo.Title,
                    Artist = CurrentInfo.Artist,
                    Position = CurrentInfo.Position,
                    Duration = CurrentInfo.Duration,
                    ThumbnailData = thumbnailData,
                    RefreshVersion = _refreshSequence, // 使用最新版本号
                };

                CurrentInfo = updatedInfo;
                NowPlayingChanged?.Invoke(this, updatedInfo);
                _staleThumbnailCandidate = null; // 重试成功，清除候选
                return; // 重试成功
            }
            catch
            {
                // 本次重试失败，尝试下一次
            }
            finally
            {
                _refreshLock.Release();
            }
        }
        // 所有重试均失败，静默放弃
    }

    // ── 辅助方法 ──────────────────────────────────────────

    /// <summary>
    /// 判断指定会话是否来自网易云音乐。
    /// 通过 SourceAppUserModelId 模糊匹配 "netease" 或 "cloudmusic"。
    /// </summary>
    private static bool IsTargetSession(GlobalSystemMediaTransportControlsSession session)
    {
        var srcId = session.SourceAppUserModelId ?? string.Empty;
        return srcId.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
               srcId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase);
    }
}