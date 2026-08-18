using Microsoft.Win32;

namespace Toolbox.Plugins.Helpers;

/// <summary>
/// 任务栏控件深浅主题适配助手。
/// 读取系统应用主题（AppsUseLightTheme），供任务栏控件/媒体卡片的文字与选中框配色使用。
/// </summary>
internal static class TaskbarThemeHelper
{
    /// <summary>当前是否为浅色应用主题。</summary>
    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch
        {
            return false; // 读取失败按深色处理（任务栏深色为默认观感）
        }
    }

    /// <summary>主文字颜色（歌名/按钮图标）。</summary>
    public static System.Windows.Media.Color TextColor(bool light) =>
        light ? System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A) : System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>次级文字颜色（歌手/时间）。</summary>
    public static System.Windows.Media.Color SecondaryTextColor(bool light) =>
        light ? System.Windows.Media.Color.FromArgb(0x99, 0x1A, 0x1A, 0x1A) : System.Windows.Media.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

    /// <summary>悬停选中框背景色：深色主题 15% 白，浅色主题 15% 黑（Win11 任务栏按钮悬停语言）。</summary>
    public static System.Windows.Media.Color HoverHighlightColor(bool light) =>
        light ? System.Windows.Media.Color.FromArgb(0x26, 0x00, 0x00, 0x00) : System.Windows.Media.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);

    /// <summary>封面描边色（12% 前景色，深色任务栏上防封面与背景糊在一起）。</summary>
    public static System.Windows.Media.Color CoverStrokeColor(bool light) =>
        light ? System.Windows.Media.Color.FromArgb(0x1F, 0x00, 0x00, 0x00) : System.Windows.Media.Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
}
