using System.Text.Json;
using Toolbox.Plugins.Helpers;
using Toolbox.Plugins.Models;
using Toolbox.Tools.Helpers;

namespace Toolbox.Plugins.Handlers;

/// <summary>
/// 电源/系统指令执行器：关机/重启/取消关机/重启资源管理器（本工具私有 <see cref="PowerActions"/>）
/// + 锁屏/睡眠/关显示器（公共 SystemPowerHelper）。
/// 进程执行经 PowerActions 注入委托：测试注入记录型假执行器，绝不触发真实关机（设计文档 9 章）。
/// </summary>
public sealed class PowerCommandHandler : IRemoteCommandHandler
{
    /// <summary>延迟秒数上限（24h）。限值防 int 溢出/误操作（吸取 ShutdownTool P1-6 教训）</summary>
    public const int MaxDelaySeconds = 86400;

    private readonly PowerActions _power;

    /// <summary>系统动作执行委托（lock/sleep/monitor_off 经此调用，测试注入假实现避免真实锁屏/睡眠）</summary>
    private readonly Func<string, bool> _systemAction;

    public PowerCommandHandler() : this(new PowerActions(), SystemPowerExecutor) { }

    /// <param name="power">进程命令执行器（测试传入注入假委托的 PowerActions）</param>
    /// <param name="systemAction">系统动作执行器（lock/sleep/monitor_off；默认调 SystemPowerHelper，测试注入假实现）</param>
    public PowerCommandHandler(PowerActions power, Func<string, bool>? systemAction = null)
    {
        _power = power;
        _systemAction = systemAction ?? SystemPowerExecutor;
    }

    public bool CanHandle(string command) => command is "shutdown" or "restart" or "cancel_shutdown"
        or "lock" or "sleep" or "monitor_off" or "explorer_restart";

    public RemoteControlResponse Execute(string command, Dictionary<string, JsonElement>? args) => command switch
    {
        "shutdown" => HandleShutdown(args),
        "restart" => HandleRestart(args),
        "cancel_shutdown" => HandleCancelShutdown(),
        "lock" => RunPower(() => _systemAction("lock"), "锁屏"),
        "sleep" => RunPower(() => _systemAction("sleep"), "睡眠"),
        "monitor_off" => RunPower(() => _systemAction("monitor_off"), "关闭显示器"),
        "explorer_restart" => HandleExplorerRestart(),
        _ => RemoteControlResponse.Fail($"未知指令: {command}")
    };

    // ==================== 危险指令（强制 confirm） ====================

    private RemoteControlResponse HandleShutdown(Dictionary<string, JsonElement>? args)
    {
        var confirmError = RequireConfirmError(args);
        if (confirmError != null) return RemoteControlResponse.Fail(confirmError);

        var delaySeconds = 0;
        if (args != null && args.TryGetValue("delaySeconds", out var delay))
        {
            if (delay.ValueKind != JsonValueKind.Number || !delay.TryGetInt32(out delaySeconds))
                return RemoteControlResponse.Fail("delaySeconds 必须为数字");
            if (delaySeconds < 0 || delaySeconds > MaxDelaySeconds)
                return RemoteControlResponse.Fail($"delaySeconds 须在 0~{MaxDelaySeconds} 之间");
        }

        return RunCommand(() => _power.Shutdown(delaySeconds), "定时关机");
    }

    private RemoteControlResponse HandleRestart(Dictionary<string, JsonElement>? args)
    {
        var confirmError = RequireConfirmError(args);
        if (confirmError != null) return RemoteControlResponse.Fail(confirmError);

        return RunCommand(_power.Restart, "重启电脑");
    }

    private RemoteControlResponse HandleCancelShutdown()
        => RunCommand(_power.CancelShutdown, "取消关机");

    // ==================== 资源管理器 ====================

    private RemoteControlResponse HandleExplorerRestart()
    {
        // taskkill 失败（explorer 未运行）也继续启动 explorer（吸取 QuickSystemTool P1-2 教训）
        _power.KillExplorer();
        var code = _power.StartExplorer();
        return code == 0
            ? RemoteControlResponse.Ok()
            : RemoteControlResponse.Fail($"启动 explorer 失败（退出码 {code}）");
    }

    // ==================== 工具方法 ====================

    private static string? RequireConfirmError(Dictionary<string, JsonElement>? args)
    {
        if (args == null || !args.TryGetValue("confirm", out var confirm) ||
            confirm.ValueKind != JsonValueKind.True)
            return "危险指令必须携带 args.confirm=true";
        return null;
    }

    private static RemoteControlResponse RunCommand(Func<int> run, string label)
    {
        var code = run();
        return code == 0
            ? RemoteControlResponse.Ok()
            : RemoteControlResponse.Fail($"{label}失败（退出码 {code}）");
    }

    private static RemoteControlResponse RunPower(Func<bool> action, string label)
        => action()
            ? RemoteControlResponse.Ok()
            : RemoteControlResponse.Fail($"{label}失败");

    /// <summary>默认系统动作实现：转发公共 SystemPowerHelper（P/Invoke 自含，见开发规范 3.8.1）</summary>
    private static bool SystemPowerExecutor(string action) => action switch
    {
        "lock" => SystemPowerHelper.Lock(),
        "sleep" => SystemPowerHelper.Sleep(),
        "monitor_off" => RunAndTrue(SystemPowerHelper.TurnOffMonitor),
        _ => false
    };

    private static bool RunAndTrue(Action action)
    {
        action();
        return true;
    }
}
