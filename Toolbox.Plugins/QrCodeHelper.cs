using QRCoder;

namespace Toolbox.Tools;

/// <summary>
/// 二维码生成辅助类 —— 纯函数，无 UI 依赖，可直接单元测试
/// </summary>
public static class QrCodeHelper
{
    /// <summary>
    /// 根据文本内容生成二维码 PNG 字节数据
    /// </summary>
    /// <param name="content">要编码的文本或 URL</param>
    /// <returns>PNG 字节数组，若内容为空则返回 null</returns>
    public static byte[]? GeneratePngBytes(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(20);
    }
}