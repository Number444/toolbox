using System.Diagnostics;
using System.Text.Json;
using Toolbox.Plugins.Handlers;
using Toolbox.Plugins.Helpers;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// PowerCommandHandler 指令参数校验与映射测试。
/// 通过注入记录型假执行器（绝不触发真实关机/重启），仅断言参数校验与指令映射（设计文档 9 章）。
/// </summary>
public class PowerCommandHandlerTests
{
    private readonly List<ProcessStartInfo> _started = new();
    private readonly List<string> _systemActions = new();

    private PowerCommandHandler CreateHandler(int exitCode = 0) =>
        new(
            new PowerActions(psi => { _started.Add(psi); return exitCode; }),
            action => { _systemActions.Add(action); return true; });

    private static Dictionary<string, JsonElement>? Args(params (string Key, JsonElement Value)[] pairs)
    {
        if (pairs.Length == 0) return null;
        return pairs.ToDictionary(p => p.Key, p => p.Value);
    }

    private static JsonElement Json(bool value) => JsonSerializer.Deserialize<JsonElement>(value ? "true" : "false");
    private static JsonElement Json(int value) => JsonSerializer.Deserialize<JsonElement>(value.ToString());

    // ==================== shutdown ====================

    [Fact]
    public void Shutdown_WithoutConfirm_Rejected()
    {
        var handler = CreateHandler();
        var result = handler.Execute("shutdown", Args(("delaySeconds", Json(60))));

        Assert.False(result.Success);
        Assert.Contains("confirm", result.Error);
        Assert.Empty(_started); // 校验失败不得下发命令
    }

    [Fact]
    public void Shutdown_WithConfirm_ExecutesWithDelay()
    {
        var handler = CreateHandler();
        var result = handler.Execute("shutdown", Args(("delaySeconds", Json(60)), ("confirm", Json(true))));

        Assert.True(result.Success);
        var psi = Assert.Single(_started);
        Assert.Equal("shutdown.exe", psi.FileName);
        Assert.Contains("/s /t 60", psi.Arguments);
    }

    [Fact]
    public void Shutdown_DefaultDelay_IsZero()
    {
        var handler = CreateHandler();
        var result = handler.Execute("shutdown", Args(("confirm", Json(true))));

        Assert.True(result.Success);
        Assert.Contains("/s /t 0", Assert.Single(_started).Arguments);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(86401)]
    public void Shutdown_DelayOutOfRange_Rejected(int delay)
    {
        var handler = CreateHandler();
        var result = handler.Execute("shutdown", Args(("delaySeconds", Json(delay)), ("confirm", Json(true))));

        Assert.False(result.Success);
        Assert.Contains("delaySeconds", result.Error);
        Assert.Empty(_started);
    }

    [Fact]
    public void Shutdown_DelayNotNumber_Rejected()
    {
        var handler = CreateHandler();
        var result = handler.Execute("shutdown", Args(("delaySeconds", JsonSerializer.Deserialize<JsonElement>("\"abc\"")), ("confirm", Json(true))));

        Assert.False(result.Success);
        Assert.Contains("delaySeconds", result.Error);
    }

    [Fact]
    public void Shutdown_ExecutorFailure_ReturnsError()
    {
        var handler = CreateHandler(exitCode: 1);
        var result = handler.Execute("shutdown", Args(("confirm", Json(true))));

        Assert.False(result.Success);
        Assert.Contains("退出码 1", result.Error);
    }

    // ==================== restart / cancel ====================

    [Fact]
    public void Restart_RequiresConfirm()
    {
        var handler = CreateHandler();
        var result = handler.Execute("restart", null);

        Assert.False(result.Success);
        Assert.Contains("confirm", result.Error);
        Assert.Empty(_started);
    }

    [Fact]
    public void Restart_WithConfirm_Executes()
    {
        var handler = CreateHandler();
        var result = handler.Execute("restart", Args(("confirm", Json(true))));

        Assert.True(result.Success);
        Assert.Equal("/r /t 0", Assert.Single(_started).Arguments);
    }

    [Fact]
    public void CancelShutdown_NoConfirmNeeded()
    {
        var handler = CreateHandler();
        var result = handler.Execute("cancel_shutdown", null);

        Assert.True(result.Success);
        Assert.Equal("/a", Assert.Single(_started).Arguments);
    }

    // ==================== explorer / 未知指令 ====================

    [Fact]
    public void ExplorerRestart_KillsThenStartsExplorer()
    {
        var handler = CreateHandler();
        var result = handler.Execute("explorer_restart", null);

        Assert.True(result.Success);
        Assert.Equal(2, _started.Count);
        Assert.Equal("taskkill.exe", _started[0].FileName);
        Assert.Equal("explorer.exe", _started[1].FileName);
    }

    [Fact]
    public void UnknownCommand_Rejected()
    {
        var handler = CreateHandler();
        var result = handler.Execute("format_disk", null);

        Assert.False(result.Success);
        Assert.Empty(_started);
    }

    [Fact]
    public void CanHandle_CoversAllDocumentedCommands()
    {
        var handler = CreateHandler();
        foreach (var command in new[] { "shutdown", "restart", "cancel_shutdown", "lock", "sleep", "monitor_off", "explorer_restart" })
            Assert.True(handler.CanHandle(command));
        Assert.False(handler.CanHandle("status"));
    }

    // ==================== 系统动作注入（锁屏/睡眠等绝不真实执行） ====================

    [Theory]
    [InlineData("lock")]
    [InlineData("sleep")]
    [InlineData("monitor_off")]
    public void SystemActions_GoThroughInjectedDelegate_NotRealSystem(string command)
    {
        var handler = CreateHandler();
        var result = handler.Execute(command, null);

        Assert.True(result.Success);
        Assert.Equal(command, Assert.Single(_systemActions)); // 经假实现执行，未触碰真实系统
        Assert.Empty(_started);
    }

    [Fact]
    public void SystemAction_Failure_ReturnsError()
    {
        var handler = new PowerCommandHandler(
            new PowerActions(psi => { _started.Add(psi); return 0; }),
            _ => false); // 假实现返回失败
        var result = handler.Execute("lock", null);

        Assert.False(result.Success);
        Assert.Contains("锁屏失败", result.Error);
    }
}
