using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Toolbox.Helpers;

/// <summary>
/// 本地 OCR 文字识别封装 —— 调用 Windows 内置 Windows.Media.Ocr 引擎（Win10 1809+），
/// 完全离线，无需任何第三方依赖。供截图识字等工具使用，无 UI 依赖。
/// </summary>
public static class OcrHelper
{
    /// <summary>系统是否存在可用的 OCR 语言包（如中文/英文识别包）</summary>
    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    /// <summary>小图放大目标边长：文字过小时识别率明显下降，先等比放大再喂引擎（可调旋钮）</summary>
    private const int UpscaleTarget = 1800;

    /// <summary>最大放大倍率：放大不能凭空补细节，超过该倍率只增加耗时（可调旋钮）</summary>
    private const double MaxUpscaleFactor = 4.0;

    /// <summary>
    /// 识别图片中的文字，返回纯文本（按行换行）。
    /// 识别失败抛出带可读信息的异常，由调用方决定如何提示。
    /// </summary>
    public static async Task<string> RecognizeAsync(BitmapSource source)
    {
        var engine = CreateEngine()
            ?? throw new InvalidOperationException("系统没有可用的 OCR 语言包，请在 Windows 设置中安装中文/英文语言包");

        var bitmap = NormalizeSize(source);

        using var softwareBitmap = ToSoftwareBitmap(bitmap);
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text;
    }

    /// <summary>优先中文识别引擎，其次用户配置语言，最后任意可用语言</summary>
    private static OcrEngine? CreateEngine()
    {
        var zh = OcrEngine.TryCreateFromLanguage(new Language("zh-Hans-CN"));
        if (zh != null) return zh;

        var profile = OcrEngine.TryCreateFromUserProfileLanguages();
        if (profile != null) return profile;

        var first = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
        return first != null ? OcrEngine.TryCreateFromLanguage(first) : null;
    }

    /// <summary>
    /// 尺寸归一化：超过引擎边长上限（通常 2600px）则等比缩小；
    /// 小于 UpscaleTarget 则等比放大（最多 MaxUpscaleFactor 倍），改善小字识别率。
    /// </summary>
    private static BitmapSource NormalizeSize(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        int longest = Math.Max(w, h);

        double scale;
        if (longest > OcrEngine.MaxImageDimension)
            scale = (double)OcrEngine.MaxImageDimension / longest;
        else if (longest < UpscaleTarget)
            scale = Math.Min((double)UpscaleTarget / longest, MaxUpscaleFactor);
        else
            return source;

        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>WPF BitmapSource → WinRT SoftwareBitmap（统一转 BGRA8 像素格式）</summary>
    private static SoftwareBitmap ToSoftwareBitmap(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(pixels.AsBuffer());
        return bitmap;
    }
}
