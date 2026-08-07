using System.IO;
using Toolbox.Plugins.Services;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// RemoteControlSettings 持久化测试：上次密钥/端口/自动生成开关读写往返（注入临时路径，不碰真实 LocalAppData）。
/// </summary>
public class RemoteControlSettingsTests
{
    private static string NewTempPath()
        => Path.Combine(Path.GetTempPath(), $"toolbox-rc-settings-{Guid.NewGuid():N}", "remote-control.json");

    [Fact]
    public void Settings_RoundTrip_PersistsAllFields()
    {
        var path = NewTempPath();
        try
        {
            var settings = new RemoteControlSettings(path);
            settings.LastKey = "my-secret-key";
            settings.LastPort = "9090";
            settings.AutoGenerateKey = false;

            var reloaded = new RemoteControlSettings(path);
            Assert.Equal("my-secret-key", reloaded.LastKey);
            Assert.Equal("9090", reloaded.LastPort);
            Assert.False(reloaded.AutoGenerateKey);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
            catch (Exception) { }
        }
    }

    [Fact]
    public void Settings_MissingFile_UsesDefaults()
    {
        var path = NewTempPath();
        try
        {
            var settings = new RemoteControlSettings(path);
            Assert.Equal("", settings.LastKey);
            Assert.Equal("8090", settings.LastPort);
            Assert.True(settings.AutoGenerateKey);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
            catch (Exception) { }
        }
    }
}
