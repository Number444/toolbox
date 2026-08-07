namespace Toolbox.Plugins.Models;

/// <summary>
/// 统一响应模型 —— 所有 API 一律返回 { success, data, error } 结构（见设计文档 5.2）。
/// </summary>
public sealed class RemoteControlResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }

    public static RemoteControlResponse Ok(object? data = null) =>
        new() { Success = true, Data = data, Error = null };

    public static RemoteControlResponse Fail(string error) =>
        new() { Success = false, Data = null, Error = error };
}
