using System.Windows;

namespace Toolbox.Models;

/// <summary>
/// 卡片发光标记 —— 附加在作为"卡片容器"的 Border 上，供主程序的边缘发光层识别收录。
/// 插件无法直接访问主程序的 EdgeGlowLayer，故标记定义在 Core 中。
/// 仅标记真正的卡片容器 Border；按钮模板内的 Border、展示面、分割线等一律不标记。
/// </summary>
public static class GlowCardMarker
{
    public static readonly DependencyProperty IsGlowCardProperty =
        DependencyProperty.RegisterAttached(
            "IsGlowCard",
            typeof(bool),
            typeof(GlowCardMarker),
            new FrameworkPropertyMetadata(false));

    public static void SetIsGlowCard(DependencyObject element, bool value)
        => element.SetValue(IsGlowCardProperty, value);

    public static bool GetIsGlowCard(DependencyObject element)
        => (bool)element.GetValue(IsGlowCardProperty);
}
