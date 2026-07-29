namespace Toolbox.Models;

/// <summary>
/// 插件 → 主窗口的导航请求通道。
/// 插件程序集只引用 Core、无法访问主程序的 MainViewModel，
/// 仪表盘等工具需要"点击卡片跳转到另一工具"时经此中转，
/// 由主窗口订阅并完成实际切换。
/// </summary>
public static class ToolNavigation
{
    /// <summary>请求切换到指定名称的工具（参数为 ITool.Name）</summary>
    public static event Action<string>? NavigateRequested;

    public static void Request(string toolName) => NavigateRequested?.Invoke(toolName);
}
