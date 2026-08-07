using System.Text.Json;
using Toolbox.Plugins.Models;

namespace Toolbox.Plugins.Handlers;

/// <summary>
/// 指令处理器接口 —— 与 UI/传输层解耦，便于单元测试注入。
/// </summary>
public interface IRemoteCommandHandler
{
    /// <summary>是否可处理该指令名</summary>
    bool CanHandle(string command);

    /// <summary>执行指令并返回统一响应</summary>
    RemoteControlResponse Execute(string command, Dictionary<string, JsonElement>? args);
}
