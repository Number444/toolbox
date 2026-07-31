namespace Toolbox.Core.Models;

/// <summary>
/// 悬浮窗控制器宿主 —— 主程序与插件之间的唯一接线点。
/// 插件加载成功后由 ToolRegistry 注册 IMusicFloatController 实现，
/// 主程序通过 Current 获取控制器，不直接引用插件类型。
/// </summary>
public static class MusicFloatControllerHost
{
    private static IMusicFloatController? _current;

    /// <summary>当前已注册的悬浮窗控制器；未注册（插件加载失败）时为 null</summary>
    public static IMusicFloatController? Current => _current;

    /// <summary>注册控制器（幂等，重复注册以最新为准）</summary>
    public static void Register(IMusicFloatController controller)
    {
        _current = controller;
    }
}
