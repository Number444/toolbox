using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Toolbox.Plugins.Helpers;

/// <summary>
/// 图片文件共享辅助类 —— 图片扩展名判断、文件加载、剪贴板图片/文件提取。
/// 供截图识字、二维码识别、图片转格式等所有"导入图片"类工具共用。
/// 纯函数无 UI 依赖（仅依赖 WPF 位图类型），可直接单元测试。
/// </summary>
public static class ImageFileHelper
{
    /// <summary>支持的图片扩展名（小写，含点）。受系统 WIC 编解码器限制，超出列表的格式不保证可读</summary>
    public static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    /// <summary>打开文件对话框的 Filter 字符串（仅允许选择图片）</summary>
    public const string DialogFilter =
        "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff";

    /// <summary>按扩展名判断是否为受支持的图片文件</summary>
    public static bool IsImageFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// 从磁盘加载图片为已冻结（可跨线程）的 BitmapImage；文件不存在或解码失败返回 null。
    /// OnLoad 缓存 + 释放流，避免长时间占用文件句柄。
    /// </summary>
    public static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null; // 损坏文件、无对应编解码器等，统一按加载失败处理
        }
    }

    /// <summary>尝试从剪贴板取位图（截图工具、浏览器复制的图片）；无则返回 null</summary>
    public static BitmapSource? TryGetClipboardImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return null;
            var image = Clipboard.GetImage();
            image?.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // 剪贴板被占用等异常按"没有图片"处理
        }
    }

    /// <summary>尝试从剪贴板取文件列表（资源管理器复制的文件）中第一个图片路径；无则返回 null</summary>
    public static string? TryGetClipboardImageFile()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return null;
            var files = Clipboard.GetFileDropList();
            return files.Cast<string>().FirstOrDefault(IsImageFile);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
