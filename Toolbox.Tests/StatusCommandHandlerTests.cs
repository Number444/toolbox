using Toolbox.Plugins.Handlers;
using Toolbox.Plugins.Models;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// StatusCommandHandler 响应结构测试：注入固定快照断言统一包装与字段完整性（不依赖真实环境，设计文档 9 章）。
/// </summary>
public class StatusCommandHandlerTests
{
    private static StatusCommandHandler CreateHandler(SystemStatusSnapshot? status = null, NetworkDetailSnapshot? network = null) =>
        new(
            statusSource: () => status ?? new SystemStatusSnapshot { CpuPercent = 12.5, MemoryTotalGB = 32, MemoryUsedGB = 18.2, Ipv4 = "192.168.1.100", Uptime = "3 天 4 小时" },
            networkSource: () => network ?? new NetworkDetailSnapshot { Mac = "AA-BB-CC-DD-EE-FF", Gateway = "192.168.1.1", Dns = ["192.168.1.1"], PublicIp = "8.8.8.8" });

    [Fact]
    public void Status_ReturnsSuccessWithSnapshotData()
    {
        var handler = CreateHandler();
        var result = handler.Execute("status", null);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        var data = Assert.IsType<SystemStatusSnapshot>(result.Data);
        Assert.Equal(12.5, data.CpuPercent);
        Assert.Equal(32, data.MemoryTotalGB);
        Assert.Equal("192.168.1.100", data.Ipv4);
    }

    [Fact]
    public void Network_ReturnsSuccessWithSnapshotData()
    {
        var handler = CreateHandler();
        var result = handler.Execute("network", null);

        Assert.True(result.Success);
        var data = Assert.IsType<NetworkDetailSnapshot>(result.Data);
        Assert.Equal("AA-BB-CC-DD-EE-FF", data.Mac);
        Assert.Equal("192.168.1.1", data.Gateway);
        Assert.Contains("192.168.1.1", data.Dns!);
        Assert.Equal("8.8.8.8", data.PublicIp);
    }

    [Fact]
    public void CanHandle_StatusAndNetwork_True()
    {
        var handler = CreateHandler();
        Assert.True(handler.CanHandle("status"));
        Assert.True(handler.CanHandle("network"));
        Assert.False(handler.CanHandle("shutdown"));
    }

    [Fact]
    public void UnknownCommand_Rejected()
    {
        var handler = CreateHandler();
        var result = handler.Execute("bogus", null);

        Assert.False(result.Success);
        Assert.Contains("未知指令", result.Error);
    }
}
