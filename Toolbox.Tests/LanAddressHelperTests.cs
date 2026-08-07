using Toolbox.Plugins.Helpers;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// LanAddressHelper 纯函数测试。GetLanIPv4s 依赖本机网卡环境，不测（同 SystemInfoHelperTests 惯例）。
/// </summary>
public class LanAddressHelperTests
{
    [Theory]
    [InlineData("192.168.1.100", 8090, "http://192.168.1.100:8090/")]
    [InlineData("10.0.0.5", 12345, "http://10.0.0.5:12345/")]
    public void FormatAccessUrl_BuildsUrl(string ipv4, int port, string expected)
    {
        Assert.Equal(expected, LanAddressHelper.FormatAccessUrl(ipv4, port));
    }

    [Fact]
    public void FormatAccessUrl_NonStandardPort_Kept()
    {
        // 端口冲突换端口场景：URL 必须跟随实际端口
        Assert.Equal("http://192.168.1.100:8888/", LanAddressHelper.FormatAccessUrl("192.168.1.100", 8888));
    }
}
