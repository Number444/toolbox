namespace Toolbox.Models;

/// <summary>
/// 所有工具的接口 —— 添加新工具只需实现此接口
/// </summary>
public interface ITool
{
    /// <summary>工具名称（显示在左侧导航栏）</summary>
    string Name { get; }

    /// <summary>工具描述（显示在详情区域标题）</summary>
    string Description { get; }

    /// <summary>图标（支持 Unicode 字符，如 "🖥️"）</summary>
    string IconGlyph { get; }

    /// <summary>分类名称（用于左侧导航栏分组折叠），推荐使用 ToolCategory 常量</summary>
    string Category { get; }

    /// <summary>创建工具的 UI 内容（右侧详情区）</summary>
    System.Windows.UIElement CreateContent();
}