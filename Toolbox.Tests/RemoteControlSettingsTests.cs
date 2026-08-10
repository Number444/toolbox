using System.IO;
using Toolbox.Core.Services;
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
            Assert.Equal(AppPaths.DefaultRemotePort, settings.LastPort);   // Debug=8091 / Release=8090，跟随编译期常量
            Assert.True(settings.AutoGenerateKey);
            Assert.Empty(settings.KnownDevices);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
            catch (Exception) { }
        }
    }

    [Fact]
    public void Devices_RecordAndRemove_PersistAcrossReload()
    {
        var path = NewTempPath();
        try
        {
            var settings = new RemoteControlSettings(path);
            settings.RecordDevice("192.168.1.50", "iPhone");
            Assert.True(settings.IsKnownDevice("192.168.1.50"));
            Assert.False(settings.IsKnownDevice("192.168.1.99"));

            // 同 IP 重新登录 → 刷新记录而非重复
            settings.RecordDevice("192.168.1.50", "Android");
            var devices = settings.KnownDevices;
            Assert.Single(devices);
            Assert.Equal("Android", devices[0].DeviceName);

            // 重载（模拟重启）→ 设备仍在（持久化）
            var reloaded = new RemoteControlSettings(path);
            Assert.True(reloaded.IsKnownDevice("192.168.1.50"));
            Assert.Equal("Android", reloaded.KnownDevices[0].DeviceName);

            // 移除 → 撤销自动填密钥资格
            reloaded.RemoveDevice("192.168.1.50");
            Assert.False(reloaded.IsKnownDevice("192.168.1.50"));

            var final = new RemoteControlSettings(path);
            Assert.Empty(final.KnownDevices);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
            catch (Exception) { }
        }
    }
}
