namespace Toolbox.Models;

/// <summary>
/// 工具分类常量 —— 用于左侧导航栏的手风琴分组折叠
/// </summary>
public static class ToolCategory
{
    public const string System = "⚙️ 系统维护";
    public const string Network = "🌐 网络与开发";
    public const string Window = "🏠 窗口与桌面";
    public const string Text = "🔤 文本与数据";
    public const string File = "📁 文件管理";
    public const string Media = "🎵 媒体与娱乐";

    /// <summary>获取所有分类列表（按显示顺序）</summary>
    public static string[] All => [System, Network, Window, Text, File, Media];
}