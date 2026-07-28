using System.IO;
using Toolbox.Core.Services;
using Xunit;

namespace Toolbox.Tests;

/// <summary>JsonSettingsFile 原子写入与 .bak 回落的单测</summary>
public class JsonSettingsFileTests : IDisposable
{
    private readonly string _dir;

    private record SampleSettings(string Name, int Value);

    public JsonSettingsFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "JsonSettingsFileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var path = PathFor("a.json");
        var expected = new SampleSettings("hello", 42);

        Assert.True(JsonSettingsFile.Save(path, expected));
        var loaded = JsonSettingsFile.Load<SampleSettings>(path);

        Assert.Equal(expected, loaded);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        Assert.Null(JsonSettingsFile.Load<SampleSettings>(PathFor("missing.json")));
    }

    [Fact]
    public void Load_CorruptMainFile_FallsBackToBak()
    {
        var path = PathFor("b.json");
        var original = new SampleSettings("backup", 7);

        // 第一次保存：只有主文件；第二次保存：旧内容进 .bak
        Assert.True(JsonSettingsFile.Save(path, original));
        Assert.True(JsonSettingsFile.Save(path, new SampleSettings("newer", 8)));
        Assert.True(File.Exists(path + ".bak"));

        // 模拟主文件被截断损坏
        File.WriteAllText(path, "{ \"Name\": \"corrupt");

        var loaded = JsonSettingsFile.Load<SampleSettings>(path);
        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Load_CorruptMainAndBak_ReturnsDefault()
    {
        var path = PathFor("c.json");
        File.WriteAllText(path, "not json");
        File.WriteAllText(path + ".bak", "also not json");

        Assert.Null(JsonSettingsFile.Load<SampleSettings>(path));
    }

    [Fact]
    public void Save_DoesNotLeaveTmpFile()
    {
        var path = PathFor("d.json");
        Assert.True(JsonSettingsFile.Save(path, new SampleSettings("x", 1)));

        Assert.False(File.Exists(path + ".tmp"));
    }
}
