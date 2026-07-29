using Toolbox.Tools.Helpers;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// SystemInfoHelper 纯函数（FormatUptime / FormatGb）的单测。
/// GetLocalIPv4 / GetMemoryUsagePercent 等依赖本机环境，不测。
/// </summary>
public class SystemInfoHelperTests
{
    [Fact]
    public void FormatUptime_OverOneDay_ShowsDaysAndHours()
    {
        var result = SystemInfoHelper.FormatUptime(TimeSpan.FromDays(3) + TimeSpan.FromHours(5));
        Assert.Equal("3 天 5 小时", result);
    }

    [Fact]
    public void FormatUptime_ExactlyOneDay_ShowsDaysAndZeroHours()
    {
        var result = SystemInfoHelper.FormatUptime(TimeSpan.FromDays(1));
        Assert.Equal("1 天 0 小时", result);
    }

    [Fact]
    public void FormatUptime_OverOneHour_ShowsHoursAndMinutes()
    {
        var result = SystemInfoHelper.FormatUptime(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(14));
        Assert.Equal("2 小时 14 分钟", result);
    }

    [Fact]
    public void FormatUptime_UnderOneHour_ShowsMinutesOnly()
    {
        var result = SystemInfoHelper.FormatUptime(TimeSpan.FromMinutes(45));
        Assert.Equal("45 分钟", result);
    }

    [Fact]
    public void FormatGb_ExactGiB_ShowsInteger()
    {
        var result = SystemInfoHelper.FormatGb(128L * 1024 * 1024 * 1024);
        Assert.Equal("128 GB", result);
    }

    [Fact]
    public void FormatGb_Zero_ShowsZero()
    {
        Assert.Equal("0 GB", SystemInfoHelper.FormatGb(0));
    }

    [Fact]
    public void FormatGb_FractionalGiB_RoundsToNearest()
    {
        var result = SystemInfoHelper.FormatGb((long)(1.4 * 1024 * 1024 * 1024));
        Assert.Equal("1 GB", result);
    }
}
