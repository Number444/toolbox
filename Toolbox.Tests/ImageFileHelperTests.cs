using Toolbox.Helpers;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// ImageFileHelper 单元测试 —— 覆盖图片扩展名判断（纯函数部分）
/// </summary>
public class ImageFileHelperTests
{
    [Theory]
    [InlineData(@"C:\pics\a.png")]
    [InlineData(@"C:\pics\b.JPG")] // 大小写不敏感
    [InlineData(@"C:\pics\c.jpeG")]
    [InlineData("photo.bmp")]
    [InlineData("anim.gif")]
    [InlineData("scan.tiff")]
    [InlineData("scan.tif")]
    public void IsImageFile_SupportedExtension_ReturnsTrue(string path)
    {
        Assert.True(ImageFileHelper.IsImageFile(path));
    }

    [Theory]
    [InlineData(@"C:\docs\a.txt")]
    [InlineData(@"C:\docs\b.pdf")]
    [InlineData("archive.zip")]
    [InlineData("noext")]
    [InlineData("")]
    public void IsImageFile_UnsupportedOrEmpty_ReturnsFalse(string path)
    {
        Assert.False(ImageFileHelper.IsImageFile(path));
    }

    [Fact]
    public void IsImageFile_Null_ReturnsFalse()
    {
        Assert.False(ImageFileHelper.IsImageFile(null));
    }

    [Fact]
    public void LoadBitmap_NonexistentFile_ReturnsNull()
    {
        Assert.Null(ImageFileHelper.LoadBitmap(@"C:\definitely\not\exists.png"));
    }
}
