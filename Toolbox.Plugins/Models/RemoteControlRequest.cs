using System.Text.Json;

namespace Toolbox.Plugins.Models;

/// <summary>
/// 远程控制指令请求模型 —— POST /api/command 的 Body 结构。
/// </summary>
public sealed class RemoteControlRequest
{
    /// <summary>指令名（shutdown/restart/lock/sleep/monitor_off/explorer_restart/cancel_shutdown/status/network）</summary>
    public string Command { get; set; } = "";

    /// <summary>指令参数（如 shutdown 的 delaySeconds/confirm），按需解析</summary>
    public Dictionary<string, JsonElement>? Args { get; set; }
}
