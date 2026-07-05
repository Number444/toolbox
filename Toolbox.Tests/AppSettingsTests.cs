using System;
using System.IO;
using Xunit;

namespace Toolbox.Tests;

public class AppSettingsTests
{
    private static readonly string TestDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Toolbox.Tests");

    private static void CleanTestDir()
    {
        if (Directory.Exists(TestDir))
            Directory.Delete(TestDir, recursive: true);
    }

    [Fact]
    public void Default_MinimizeOnClose_IsFalse()
    {
        // 尚未 Load 过的默认值应为 false
        CleanTestDir();
        var settings = new Toolbox.Core.Services.AppSettings(TestDir);
        Assert.False(settings.MinimizeOnClose);
    }

    [Fact]
    public void SaveAndLoad_PreservesValue()
    {
        CleanTestDir();
        var s1 = new Toolbox.Core.Services.AppSettings(TestDir);
        s1.MinimizeOnClose = true;
        s1.Save();

        var s2 = new Toolbox.Core.Services.AppSettings(TestDir);
        s2.Load();
        Assert.True(s2.MinimizeOnClose);
    }

    [Fact]
    public void Load_NonExistentFile_DoesNotThrow()
    {
        CleanTestDir();
        var ex = Record.Exception(() =>
        {
            var s = new Toolbox.Core.Services.AppSettings(TestDir);
            s.Load();
        });
        Assert.Null(ex);
    }

    // ── MusicFloatSizeMode 持久化 ──────────────────────────

    [Fact]
    public void Default_MusicFloatSizeMode_IsLarge()
    {
        CleanTestDir();
        var settings = new Toolbox.Core.Services.AppSettings(TestDir);
        Assert.Equal("Large", settings.MusicFloatSizeMode);
    }

    [Fact]
    public void SaveAndLoad_MusicFloatSizeMode_PreservesValue()
    {
        CleanTestDir();
        var s1 = new Toolbox.Core.Services.AppSettings(TestDir);
        s1.MusicFloatSizeMode = "Compact";
        s1.Save();

        var s2 = new Toolbox.Core.Services.AppSettings(TestDir);
        s2.Load();
        Assert.Equal("Compact", s2.MusicFloatSizeMode);
    }

    [Fact]
    public void SaveAndLoad_MusicFloatSizeMode_Default_WhenNotSaved()
    {
        CleanTestDir();
        var s1 = new Toolbox.Core.Services.AppSettings(TestDir);
        s1.MusicFloatSizeMode = "Compact";
        s1.Save();

        // 删除 settings 文件 -> 重新加载应返回默认值 "Large"
        File.Delete(Path.Combine(TestDir, "settings.json"));

        var s2 = new Toolbox.Core.Services.AppSettings(TestDir);
        s2.Load();
        Assert.Equal("Large", s2.MusicFloatSizeMode);
    }
}