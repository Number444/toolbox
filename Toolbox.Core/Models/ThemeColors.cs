using System.Windows.Media;

namespace Toolbox.Models;

/// <summary>
/// 全局主题色板 —— 各工具界面共享的颜色常量，保证与整体风格一致。
/// 位于 Toolbox.Core：主程序与插件（Toolbox.Plugins）共用。
/// </summary>
public static class ThemeColors
{
    /// <summary>深色背景</summary>
    public static readonly Color BgDark = Color.FromRgb(0x2D, 0x2D, 0x2D);

    /// <summary>主要文本</summary>
    public static readonly Color TextPrimary = Color.FromRgb(0xF0, 0xF0, 0xF0);

    /// <summary>次要文本</summary>
    public static readonly Color TextSecondary = Color.FromRgb(0x80, 0x80, 0x80);

    /// <summary>成功/正常状态（绿）</summary>
    public static readonly Color Success = Color.FromRgb(0x63, 0xD4, 0x7E);

    /// <summary>危险/错误状态（红）</summary>
    public static readonly Color Danger = Color.FromRgb(0xF0, 0x70, 0x70);

    /// <summary>警告状态（橙）</summary>
    public static readonly Color Warning = Color.FromRgb(0xE0, 0xA0, 0x30);

    /// <summary>细边框 / 分割竖线（低调灰）</summary>
    public static readonly Color BorderSubtle = Color.FromRgb(0x45, 0x45, 0x45);

    /// <summary>实心危险按钮（深红，用于确认弹窗确认键等）；
    /// 与 Danger 警示红并存——两种红待用户体验后统一，统一时只需改这一处</summary>
    public static readonly Color DangerButton = Color.FromRgb(0xD0, 0x40, 0x40);
}
