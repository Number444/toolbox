using Windows.Media.Control;

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

    /// <summary>播放状态（Playing / Paused / Stopped 等），来自 SMTC PlaybackInfo。</summary>
    public GlobalSystemMediaTransportControlsSessionPlaybackStatus? PlaybackStatus { get; set; }

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