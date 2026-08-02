using System;

namespace Toolbox.Helpers;

/// <summary>
/// 崩溃弹窗节流（2026-08-03 审查中危：DispatcherUnhandledException 无节流，
/// 同一异常反复出现会连环弹窗刷爆日志）。
/// 规则：按「异常类型|消息」分组——冷却期内不弹（只记日志）；同组连续达到上限
/// 判定为损坏状态空转，调用方应退出进程。
/// </summary>
internal sealed class CrashThrottle
{
    private readonly TimeSpan _cooldown;
    private readonly int _maxRepeat;
    private string? _lastKey;
    private DateTime _lastShown;
    private int _repeatCount;

    public CrashThrottle(TimeSpan cooldown, int maxRepeat)
    {
        _cooldown = cooldown;
        _maxRepeat = maxRepeat;
    }

    /// <summary>本次是否应弹窗；false = 冷却期内（调用方只记日志）</summary>
    public bool ShouldShow(string exceptionType, string message)
    {
        var now = DateTime.UtcNow;
        string key = exceptionType + "|" + message;
        if (key == _lastKey)
        {
            _repeatCount++;
            if (now - _lastShown < _cooldown) return false;
            _lastShown = now;
            return true;
        }
        _lastKey = key;
        _repeatCount = 1;
        _lastShown = now;
        return true;
    }

    /// <summary>同组异常连续达到上限（损坏状态空转）→ 调用方应退出进程而非继续弹窗</summary>
    public bool IsExcessive => _repeatCount >= _maxRepeat;
}
