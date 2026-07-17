using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
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
    /// </summary>
    public async Task StartAsync()
    {
        if (_manager != null) return;

        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        SubscribeToSession(_manager.GetCurrentSession());

        // 订阅会话列表变化，当网易云音乐启动/关闭时重新匹配
        _manager.SessionsChanged += OnSessionsChanged;
    }

    /// <summary>
    /// 停止监听并释放资源。
    /// </summary>
    public void Stop()
    {
        _isClosing = true;
        UnsubscribeFromSession(_session);
        if (_manager != null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
        }
        _manager = null;
        _session = null;
    }

    public void Dispose()
    {
        Stop();
        _refreshLock.Dispose();
    }

    // ── 会话管理 ──────────────────────────────────────────

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        if (_isClosing) return;

        // 尝试在当前所有会话中匹配网易云音乐
        foreach (var s in sender.GetSessions())
        {
            if (IsTargetSession(s))
            {
                SubscribeToSession(s);
                return;
            }
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
        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
    }

    // ── 事件处理（按职责分开，修复 4）──────────────────────

    /// <summary>
    /// 媒体属性变更（标题/歌手/封面）→ 全量刷新。
    /// </summary>
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = RefreshNowPlayingAsync(RefreshScope.Full);
    }

    /// <summary>
    /// 进度变更 → 仅刷新 Position/Duration，不重读封面（修复 4）。
    /// </summary>
    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _ = RefreshNowPlayingAsync(RefreshScope.TimelineOnly);
    }

    /// <summary>
    /// 播放状态变更 → 仅刷新 Position/Duration，不重读封面（修复 4）。
    /// </summary>
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
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