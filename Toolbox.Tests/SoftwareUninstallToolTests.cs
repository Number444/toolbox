using Toolbox.Models;
using Toolbox.Services;
using Toolbox.Tools;
using Xunit;

namespace Toolbox.Tests;

public class SoftwareUninstallToolTests
{
    [Fact]
    public void Model_Properties_SetCorrectly()
    {
        var software = new InstalledSoftware
        {
            DisplayName = "TestApp",
            DisplayVersion = "1.0.0",
            Publisher = "Test Corp",
            InstallDate = "20260701",
            EstimatedSize = 102400,
            UninstallString = "C:\\Test\\uninstall.exe"
        };

        Assert.Equal("TestApp", software.DisplayName);
        Assert.Equal("1.0.0", software.DisplayVersion);
        Assert.Equal("Test Corp", software.Publisher);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(500, "500 KB")]
    [InlineData(2048, "2.0 MB")]
    [InlineData(1048576, "1.00 GB")]
    public void SizeDisplay_FormatsCorrectly(long sizeKb, string expected)
    {
        var sw = new InstalledSoftware
        {
            DisplayName = "Test",
            UninstallString = "test.exe",
            EstimatedSize = sizeKb
        };
        Assert.Equal(expected, sw.SizeDisplay);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("20260701", "2026-07-01")]
    [InlineData("20250115", "2025-01-15")]
    [InlineData("20221301", "20221301")]
    public void DateDisplay_FormatsCorrectly(string dateStr, string expected)
    {
        var sw = new InstalledSoftware
        {
            DisplayName = "Test",
            UninstallString = "test.exe",
            InstallDate = dateStr
        };
        Assert.Equal(expected, sw.DateDisplay);
    }

    [Fact]
    public void ExtractIcon_InvalidPath_ReturnsNull()
    {
        var icon = SoftwareUninstallService.ExtractIcon("C:\\DoesNotExist\\fake.exe");
        Assert.Null(icon);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractIcon_EmptyOrWhitespace_ReturnsNull(string iconPath)
    {
        var icon = SoftwareUninstallService.ExtractIcon(iconPath);
        Assert.Null(icon);
    }

    [Fact]
    public void UninstallSoftware_EmptyUninstallString_ReturnsFalse()
    {
        var sw = new InstalledSoftware { DisplayName = "Test", UninstallString = "" };
        bool result = SoftwareUninstallService.UninstallSoftware(sw, out var error);
        Assert.False(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void InstalledSoftware_ReferenceEquality_WorksAsExpected()
    {
        var sw1 = new InstalledSoftware { DisplayName = "A", UninstallString = "x" };
        var sw2 = new InstalledSoftware { DisplayName = "A", UninstallString = "x" };
        var list = new List<InstalledSoftware> { sw1 };
        Assert.False(list.Remove(sw2));
        Assert.True(list.Remove(sw1));
    }

    [Fact]
    public void Tool_Implements_ITool()
    {
        var tool = new SoftwareUninstallTool();
        Assert.IsAssignableFrom<ITool>(tool);
        Assert.Equal("软件卸载管理器", tool.Name);
        Assert.Equal("🧹", tool.IconGlyph);
        Assert.Equal(Toolbox.Models.ToolCategory.File, tool.Category);
    }
}
