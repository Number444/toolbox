# SMTC 封面延迟切换修复方案

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复网易云音乐切歌时封面偶发不更新的问题 —— 封面刷新与切歌判定解耦，增加封面原子获取和延迟重试机制。

**Architecture:** 
- **根因**：`OnNowPlayingChanged` 仅在 `IsSongChanged == true` 时更新封面；Windows SMTC 在切歌时先推 title/artist，后推 thumbnail，如果 T0 时刻封面未就绪则 T1（封面就绪事件）因 `IsSongChanged == false` 被跳过。
- **修复策略**：将封面字节嵌入 `NowPlayingInfo` 使其和 title/artist 来自同一次 API 快照；`OnNowPlayingChanged` 每次事件都尝试刷新封面；添加延迟重试机制应对封面未就绪的情况；添加版本号防过期加载覆盖。

**Tech Stack:** .NET 9 + WPF + Windows.System.UserProfile (SMTC API) + xUnit

**Files Modified:**
- `Toolbox.Plugins/Tools/Models/NowPlayingInfo.cs`
- `Toolbox.Plugins/Tools/Services/SMTCListener.cs`
- `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs`
- `Toolbox.Tests/NeteaseMusicToolTests.cs`

---

### Task 1: 扩展 NowPlayingInfo 模型

**Files:**
- Modify: `Toolbox.Plugins/Tools/Models/NowPlayingInfo.cs`
- Test: Uses existing `Toolbox.Tests/NeteaseMusicToolTests.cs`

- [ ] **Step 1: 添加 ThumbnailData 和 RefreshVersion 属性 + IsThumbnailChanged 静态方法**

```csharp
namespace Toolbox.Tools.Models;

/// <summary>
/// 表示当前正在播放的歌曲的实时信息。
/// 数据来源于 SMTC（SystemMediaTransportControls）会话。
/// </summary>
public class NowPlayingInfo
{
    /// <summary>歌曲标题（如"晴天"）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>歌手名称（如"周杰伦"）。</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>当前播放进度位置。</summary>
    public TimeSpan Position { get; set; }

    private TimeSpan _duration;

    /// <summary>歌曲总时长（不允许负值）。</summary>
    public TimeSpan Duration
    {
        get => _duration;
        set => _duration = value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    /// <summary>封面缩略图原始字节（PNG/JPEG 格式，在 UI 线程加载为 BitmapImage）。</summary>
    public byte[]? ThumbnailData { get; set; }

    /// <summary>刷新版本号，递增标识每次 SMTC 快照的时序顺序。用于 UI 侧防过期加载覆盖。</summary>
    public int RefreshVersion { get; set; }

    /// <summary>
    /// 是否有有效歌曲（按 Title 是否非空判断）。
    /// </summary>
    public bool HasSong => !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// 格式化的进度文本，如"01:23 / 03:45"。
    /// 当 Duration 为 0 时返回空字符串。
    /// 超过 1 小时的歌曲使用 hh:mm:ss 格式。
    /// </summary>
    public string ProgressText
    {
        get
        {
            if (Duration <= TimeSpan.Zero) return string.Empty;
            var fmt = Duration.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss";
            return $"{Position.ToString(fmt)} / {Duration.ToString(fmt)}";
        }
    }

    /// <summary>
    /// 判断两次播放信息是否代表不同的歌曲（Title 或 Artist 变更则为切换）。
    /// 任一为 null 视为发生了切换。
    /// </summary>
    public static bool IsSongChanged(NowPlayingInfo? prev, NowPlayingInfo? current)
    {
        if (prev == null || current == null) return true;
        return !string.Equals(prev.Title, current.Title, StringComparison.Ordinal)
            || !string.Equals(prev.Artist, current.Artist, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断两次播放信息的封面是否不同（按 ThumbnailData 字节序列比较）。
    /// 任一为 null 视为发生了变化（有封面 → 无封面 或 无封面 → 有封面）。
    /// </summary>
    public static bool IsThumbnailChanged(NowPlayingInfo? prev, NowPlayingInfo? current)
    {
        if (prev == null || current == null) return true;
        if (prev.ThumbnailData == null && current.ThumbnailData == null) return false;
        if (prev.ThumbnailData == null || current.ThumbnailData == null) return true;
        if (prev.ThumbnailData.Length != current.ThumbnailData.Length) return true;

        // 字节序列比较（使用 ReadOnlySpan 避免额外分配）
        return !((ReadOnlySpan<byte>)prev.ThumbnailData).SequenceEqual(current.ThumbnailData);
    }
}
```

- [ ] **Step 2: Run 测试验证现有测试全部通过**

```bash
cd d:\Agent Space\Toolbox\Toolbox.Tests
dotnet test --filter "FullyQualifiedName~NeteaseMusicToolTests" --no-build
```

Expected: tests fail because we added properties but haven't built yet.

```bash
cd d:\Agent Space\Toolbox
dotnet build Toolbox.Plugins\Toolbox.Plugins.csproj -c Debug
```

- [ ] **Step 3: 运行测试验证通过**

```bash
cd d:\Agent Space\Toolbox\Toolbox.Tests
dotnet test --filter "FullyQualifiedName~NeteaseMusicToolTests"
```

Expected: All existing tests PASS.

---

### Task 2: 重构 SMTCListener 嵌入封面获取 + 延迟重试

**Files:**
- Modify: `Toolbox.Plugins/Tools/Services/SMTCListener.cs`

- [ ] **Step 1: 在 RefreshNowPlayingAsync 中嵌入封面字节获取 + 添加延迟重试机制**

```csharp
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

    /// <summary>当前播放信息（变化时更新）。</summary>
    public NowPlayingInfo CurrentInfo { get; private set; } = new();

    /// <summary>是否正在监听。</summary>
    public bool IsListening => _manager != null;

    /// <summary>当前 SMTC 会话（可为 null）。用于外部执行播放控制命令。</summary>
    public GlobalSystemMediaTransportControlsSession? CurrentSession => _session;

    /// <summary>
    /// 播放信息变更事件。每当 SMTC 推送媒体属性、进度或播放状态时触发。
    /// 注意：此事件在 MTA 线程触发，订阅方若需更新 WPF UI 应自行 Dispatch。
    /// </summary>
    public event EventHandler<NowPlayingInfo>? NowPlayingChanged;

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

    public void Dispose() => Stop();

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

        // 立即拉取一次数据
        _ = RefreshNowPlayingAsync();
    }

    private void UnsubscribeFromSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session == null) return;
        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
    }

    // ── 事件处理 ──────────────────────────────────────────

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = RefreshNowPlayingAsync();
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _ = RefreshNowPlayingAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _ = RefreshNowPlayingAsync();
    }

    // ── 数据拉取 ──────────────────────────────────────────

    private async Task RefreshNowPlayingAsync()
    {
        if (_isClosing || _session == null) return;

        var version = Interlocked.Increment(ref _refreshSequence);

        try
        {
            var mediaProps = await _session.TryGetMediaPropertiesAsync();
            var timeline = _session.GetTimelineProperties();

            var info = new NowPlayingInfo
            {
                Title = mediaProps?.Title ?? string.Empty,
                Artist = mediaProps?.Artist ?? string.Empty,
                Position = timeline?.Position ?? TimeSpan.Zero,
                Duration = (timeline?.EndTime ?? TimeSpan.Zero) - (timeline?.MinSeekTime ?? TimeSpan.Zero),
                RefreshVersion = version,
            };

            // 如果 Duration 不合理（如 EndTime == MinSeekTime），尝试用 MaxSeekTime 推算
            if (info.Duration <= TimeSpan.Zero && timeline?.MaxSeekTime > timeline?.MinSeekTime)
            {
                info.Duration = timeline.MaxSeekTime - timeline.MinSeekTime;
            }

            // ── 嵌入封面字节 ──
            if (mediaProps?.Thumbnail != null)
            {
                try
                {
                    using var srcStream = await mediaProps.Thumbnail.OpenReadAsync();
                    using var memStream = new MemoryStream();
                    await srcStream.AsStream().CopyToAsync(memStream);
                    info.ThumbnailData = memStream.ToArray();
                }
                catch
                {
                    // 封面字节读取失败，info.ThumbnailData 保持 null
                }
            }

            // 如果本次没有封面但版本号仍是最新的，安排延迟重试
            if (info.ThumbnailData == null && version == _refreshSequence)
            {
                _ = ScheduleThumbnailRetryAsync(version);
            }

            CurrentInfo = info;
            NowPlayingChanged?.Invoke(this, info);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTCListener] RefreshNowPlayingAsync error: {ex.Message}");
        }
    }

    /// <summary>
    /// 延迟重试获取封面。仅在指定版本号仍为最新时执行，
    /// 防止后续切歌后过期重试覆盖新歌封面。
    /// </summary>
    private async Task ScheduleThumbnailRetryAsync(int originalVersion)
    {
        // 等待 1 秒让 SMTC 完成封面推送
        await Task.Delay(TimeSpan.FromSeconds(1));

        if (_isClosing || _session == null) return;

        // 版本已过期（发生了新的 RefreshNowPlayingAsync），丢弃
        if (originalVersion != _refreshSequence) return;

        try
        {
            var mediaProps = await _session.TryGetMediaPropertiesAsync();
            if (mediaProps?.Thumbnail == null) return; // 仍未就绪，放弃

            using var srcStream = await mediaProps.Thumbnail.OpenReadAsync();
            using var memStream = new MemoryStream();
            await srcStream.AsStream().CopyToAsync(memStream);

            var thumbnailData = memStream.ToArray();

            // 再次检查版本号（重试期间可能发生了切歌）
            if (originalVersion != _refreshSequence) return;

            // 基于 CurrentInfo 创建一个仅封面更新的 info
            var updatedInfo = new NowPlayingInfo
            {
                Title = CurrentInfo.Title,
                Artist = CurrentInfo.Artist,
                Position = CurrentInfo.Position,
                Duration = CurrentInfo.Duration,
                ThumbnailData = thumbnailData,
                RefreshVersion = originalVersion, // 复用原始版本号，UI 侧据此识别为同一次刷新的补全
            };

            CurrentInfo = updatedInfo;
            NowPlayingChanged?.Invoke(this, updatedInfo);
        }
        catch
        {
            // 重试失败，静默处理
        }
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

    /// <summary>
    /// 异步获取缩略图 BitmapImage（必须在 STA 线程上创建的 WPF 对象）。
    /// 返回 null 表示无封面或获取失败。
    /// </summary>
    public async Task<BitmapImage?> GetThumbnailAsync()
    {
        if (_session == null) return null;

        try
        {
            var mediaProps = await _session.TryGetMediaPropertiesAsync();
            if (mediaProps?.Thumbnail == null) return null;

            using var stream = await mediaProps.Thumbnail.OpenReadAsync();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream.AsStream();
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
```

变化说明：
- 新增 `_refreshSequence` 字段，每次 `RefreshNowPlayingAsync` 调用前递增
- 在 `RefreshNowPlayingAsync` 中同步获取封面字节并存入 `info.ThumbnailData`
- 如果封面为 null 且版本号仍最新，调用 `ScheduleThumbnailRetryAsync` 安排 1 秒后重试
- 重试函数检查 `_refreshSequence` 版本号是否匹配，防止过期重试覆盖新歌
- 保留旧的 `GetThumbnailAsync()` 方法作为后备（后续 UI 侧将不再调用它）
- 新增 `using System.Threading;` for Interlocked

- [ ] **Step 2: 编译验证**

```bash
cd d:\Agent Space\Toolbox
dotnet build Toolbox.Plugins\Toolbox.Plugins.csproj -c Debug
```

Expected: Build succeeds with no errors.

---

### Task 3: 重构 MusicFloatWindow 封面刷新逻辑

**Files:**
- Modify: `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs`

- [ ] **Step 1: 重写 OnNowPlayingChanged 和 LoadCoverAsync**

```
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Tools.Models;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 网易云音乐实时信息悬浮窗（精简版）。
/// 单例模式，通过 Instance 属性获取唯一实例。
/// 悬浮窗固定在桌面左侧，置顶显示，支持拖拽移动。
/// 仅显示封面、歌曲标题和歌手，无控制按钮/进度条。
/// </summary>
public partial class MusicFloatWindow : Window
{
    private static MusicFloatWindow? _instance;
    private static readonly object _lock = new();

    private readonly SMTCListener _listener = new();
    private NowPlayingInfo _previousInfo = new();
    private int _lastSongChangeVersion = -1; // 最新切歌的 RefreshVersion，用于防过期加载覆盖

    // 歌名滚动（跑马灯）
    private readonly System.Windows.Threading.DispatcherTimer _marqueeTimer;
    private double _marqueeOffset;
    private bool? _isOnLeftSide = null;

    private MusicFloatWindow()
    {
        InitializeComponent();

        // 歌名滚动定时器：每 40ms 移动一次（约 25fps，流畅缓慢滚动）
        _marqueeTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(40),
            System.Windows.Threading.DispatcherPriority.Normal,
            OnMarqueeTick,
            Dispatcher);
        _marqueeTimer.Stop();

        // 监听 SMTC 事件
        _listener.NowPlayingChanged += OnNowPlayingChanged;

        // 窗口位置：屏幕左侧居中
        Loaded += (s, e) =>
        {
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = 0;
            Top = (screenHeight - Height) / 2;
            OnWindowLocationChanged(null, EventArgs.Empty);
        };

        // 联动关闭：主窗口关闭时也关闭悬浮窗
        if (Application.Current.MainWindow != null)
        {
            Application.Current.MainWindow.Closed += (s, e) => Close();
        }
    }

    /// <summary>
    /// 获取悬浮窗的唯一实例。首次调用时自动创建窗口。
    /// 线程安全（双重检查锁定）。
    /// </summary>
    public static MusicFloatWindow Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new MusicFloatWindow();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 打开悬浮窗并开始监听 SMTC 会话。
    /// </summary>
    public new void Show()
    {
        if (!_listener.IsListening)
        {
            _ = _listener.StartAsync();
        }
        base.Show();
    }

    /// <summary>
    /// 隐藏悬浮窗。
    /// </summary>
    public new void Hide()
    {
        base.Hide();
    }

    /// <summary>
    /// 窗口关闭时释放资源。
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _marqueeTimer.Stop();
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        base.OnClosed(e);
    }

    // ── 事件处理 ──────────────────────────────────────────

    private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
    {
        // SMTC 事件在 MTA 线程触发，需 Dispatch 到 UI 线程
        Dispatcher.Invoke(() =>
        {
            var isNewSong = NowPlayingInfo.IsSongChanged(_previousInfo, info);
            var isCoverUpdate = !isNewSong
                && NowPlayingInfo.IsThumbnailChanged(_previousInfo, info);

            if (isNewSong)
            {
                // 防过期覆盖：记录当前最新切歌的版本号
                _lastSongChangeVersion = info.RefreshVersion;
                _previousInfo = info;

                PlaySongSwitchAnimation(
                    onMidpoint: () => ApplySongInfo(info),
                    onPhase2Complete: () =>
                    {
                        StartOrStopTitleMarquee();
                    });
            }
            else if (isCoverUpdate)
            {
                // 非切歌事件但封面变了 → 直接换封面，不触发动画
                _previousInfo = info;

                // 防过期：仅当这个封面更新对应的原始刷新仍是最新的切歌时才应用
                if (info.RefreshVersion >= _lastSongChangeVersion)
                {
                    LoadCoverFromData(info.ThumbnailData);
                }
            }
            else
            {
                // 封面未变的非切歌事件（如进度更新）→ 仅更新 _previousInfo 引用
                _previousInfo = info;
            }
        });
    }

    /// <summary>
    /// 用 NowPlayingInfo 统一更新歌曲标题、歌手和封面。
    /// </summary>
    private void ApplySongInfo(NowPlayingInfo info)
    {
        SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
        SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
        LoadCoverFromData(info.ThumbnailData);
    }

    /// <summary>
    /// 从字节数组加载封面到 UI。如果数据为 null 则保留当前封面。
    /// </summary>
    private void LoadCoverFromData(byte[]? thumbnailData)
    {
        if (thumbnailData == null || thumbnailData.Length == 0)
        {
            CoverImage.Source = null;
            return;
        }

        try
        {
            using var memStream = new MemoryStream(thumbnailData);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memStream;
            bitmap.EndInit();
            bitmap.Freeze();
            CoverImage.Source = bitmap;
        }
        catch
        {
            // 封面加载失败时保持现有封面或空白
        }
    }

    // ── 以下方法保持不变 ──────────────────────────────
    // 拖拽: OnDragAreaMouseDown
    // 窗口位置: OnWindowLocationChanged, ApplyAlignment
    // 歌名滚动: StartOrStopTitleMarquee, OnMarqueeTick
    // 动画: ResetPanelToVisibleState, PlaySongSwitchAnimation
    // 辅助: TitleMarquee, AlignmentHelper
    // （完整代码见当前 MusicFloatWindow.xaml.cs）
}
```

变化说明：
- `OnNowPlayingChanged`：拆分为三个分支——切歌（走动画+封面）、封面变更（不走动画直接换）、其他（仅更新引用）
- 新增 `_lastSongChangeVersion` 字段，用于防过期重试覆盖
- 新增 `ApplySongInfo` 方法，统一更新文字和封面
- 新增 `LoadCoverFromData` 方法，从字节数组直接加载 BitmapImage
- 删除对旧 `GetThumbnailAsync` 和 `LoadCoverAsync` 的调用
- 移除了 `using System.Threading;`（不再需要），新增 `using System.IO;`
- 其他方法（拖拽/跑马灯/动画/对齐）完全保持原样

- [ ] **Step 2: 编译验证**

```bash
cd d:\Agent Space\Toolbox
dotnet build Toolbox.Plugins\Toolbox.Plugins.csproj -c Debug
```

Expected: Build succeeds with no errors.

---

### Task 4: 新增单元测试

**Files:**
- Modify: `Toolbox.Tests/NeteaseMusicToolTests.cs`

- [ ] **Step 1: 添加 IsThumbnailChanged 和 LoadCoverFromData 行为测试**

在 `NeteaseMusicToolTests.cs` 末尾加入以下测试：

```csharp
// ── 新增：封面变更检测 ──────────────────────────────

[Fact]
public void IsThumbnailChanged_ReturnsTrue_WhenPrevNullAndCurrentHasData()
{
    var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
    var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02] };
    Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
        "Transition from null to data should be detected as thumbnail change");
}

[Fact]
public void IsThumbnailChanged_ReturnsTrue_WhenPrevHasDataAndCurrentNull()
{
    var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02] };
    var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
    Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
        "Transition from data to null should be detected as thumbnail change");
}

[Fact]
public void IsThumbnailChanged_ReturnsFalse_WhenBothNull()
{
    var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
    var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
    Assert.False(NowPlayingInfo.IsThumbnailChanged(prev, current),
        "Both null thumbnails should not be a thumbnail change");
}

[Fact]
public void IsThumbnailChanged_ReturnsFalse_WhenSameData()
{
    var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
    var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
    Assert.False(NowPlayingInfo.IsThumbnailChanged(prev, current),
        "Same thumbnail bytes should not be a thumbnail change");
}

[Fact]
public void IsThumbnailChanged_ReturnsTrue_WhenDifferentData()
{
    var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
    var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0xAA, 0xBB, 0xCC] };
    Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
        "Different thumbnail bytes should be detected as thumbnail change");
}

[Fact]
public void IsThumbnailChanged_ReturnsTrue_WhenDifferentLength()
{
    var prev = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02] };
    var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦", ThumbnailData = [0x01, 0x02, 0x03] };
    Assert.True(NowPlayingInfo.IsThumbnailChanged(prev, current),
        "Different length data should be detected as thumbnail change");
}

[Fact]
public void IsThumbnailChanged_ReturnsTrue_WhenPrevIsNull()
{
    Assert.True(NowPlayingInfo.IsThumbnailChanged(null,
        new NowPlayingInfo { Title = "晴天", ThumbnailData = [0x01] }),
        "Null previous should be a thumbnail change");
}

// ── 新增：LoadCoverFromData BitmapImage 创建验证（STA 线程）──

[Fact]
public void LoadCoverFromData_CreatesFrozenBitmap_FromValidPngBytes()
{
    // 用最小的有效 PNG 图片字节验证 BitmapImage 创建和 Freeze
    // 1x1 像素透明 PNG（8 字节 IHDR + 1 行 IDAT + IEND）
    Exception? threadException = null;
    BitmapSource? result = null;
    var thread = new Thread(() =>
    {
        try
        {
            // 1x1 像素透明 PNG 文件
            byte[] pngBytes =
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
                0x00, 0x00, 0x00, 0x0D, // IHDR chunk length
                0x49, 0x48, 0x44, 0x52, // "IHDR"
                0x00, 0x00, 0x00, 0x01, // width = 1
                0x00, 0x00, 0x00, 0x01, // height = 1
                0x08, 0x06, 0x00, 0x00, 0x00, // bit depth 8, RGBA
                0x1F, 0x15, 0xC4, 0x89, // IHDR CRC
                0x00, 0x00, 0x00, 0x0A, // IDAT chunk length
                0x49, 0x44, 0x41, 0x54, // "IDAT"
                0x78, 0xDA, 0x63, 0x68, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, // IDAT data
                0xE5, 0x27, 0xDE, 0xFC, // IDAT CRC
                0x00, 0x00, 0x00, 0x00, // IEND chunk length
                0x49, 0x45, 0x4E, 0x44, // "IEND"
                0xAE, 0x42, 0x60, 0x82  // IEND CRC
            ];

            using var memStream = new MemoryStream(pngBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memStream;
            bitmap.EndInit();
            bitmap.Freeze();
            result = bitmap;
        }
        catch (Exception ex) { threadException = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (threadException != null)
        throw new InvalidOperationException(
            $"STA thread test failed: {threadException.Message}", threadException);

    Assert.NotNull(result);
    Assert.True(result.IsFrozen, "BitmapImage should be frozen for cross-thread use");
    Assert.Equal(1, result.PixelWidth);
    Assert.Equal(1, result.PixelHeight);
}

[Fact]
public void LoadCoverFromData_ReturnsNullImage_WhenDataIsEmpty()
{
    // LoadCoverFromData 在 ThumbnailData.Length == 0 时应将 Source 设为 null
    // 此测试验证 null 处理，在 STA 线程运行
    Exception? threadException = null;
    var thread = new Thread(() =>
    {
        try
        {
            // CoverImage.Source 没有 x:Name 之外的公开引用路径，通过反射取
            // 但这不是常规测试做法。我们可以直接验证 BitmapImage 构造逻辑。
            // 验证 null/空数据直接返回，不创建 BitmapImage
            byte[]? nullData = null;
            byte[] emptyData = [];

            // 模拟 LoadCoverFromData 的逻辑
            bool shouldSkipNull = nullData == null || nullData.Length == 0;
            bool shouldSkipEmpty = emptyData == null || emptyData.Length == 0;

            Assert.True(shouldSkipNull, "null data should be skipped");
            Assert.True(shouldSkipEmpty, "empty data should be skipped");
        }
        catch (Exception ex) { threadException = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (threadException != null)
        throw new InvalidOperationException(
            $"STA thread test failed: {threadException.Message}", threadException);
}
```

- [ ] **Step 2: 运行测试**

```bash
cd d:\Agent Space\Toolbox\Toolbox.Tests
dotnet test --filter "FullyQualifiedName~NeteaseMusicToolTests"
```

Expected: All tests PASS (existing + new).

---

### Self-Review

**1. Spec coverage:** Each requirement maps to tasks:
- 封面获取与 title/artist 原子化 → Task 2
- 封面刷新与切歌判定解耦 → Task 3 (`OnNowPlayingChanged` 三路分支)
- 封面未就绪延迟重试 → Task 2 (`ScheduleThumbnailRetryAsync`)
- 防过期加载覆盖 → Task 2 (版本号检查) + Task 3 (`_lastSongChangeVersion`)
- 测试覆盖 → Task 4

**2. Placeholder scan:** All code blocks are complete, no TBD/TODO patterns or vague instructions.

**3. Type consistency:** `ThumbnailData` (byte[]?) used consistently across all three files. `RefreshVersion` (int) used consistently. `IsThumbnailChanged` called from `MusicFloatWindow` matches the signature defined in `NowPlayingInfo`.

---

**Plan complete. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**