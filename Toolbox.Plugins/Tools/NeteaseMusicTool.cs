using System.Windows;
using System.Windows.Controls;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// Toolbox 插件——网易云音乐实时信息悬浮窗。
/// 通过 Windows SMTC API 监听网易云音乐播放状态，
/// 在桌面左侧创建置顶悬浮窗显示歌曲信息和播放控制。
/// </summary>
public class NeteaseMusicTool : ITool
{
    public string Name => "网易云音乐悬浮窗";
    public string Description => "读取网易云音乐实时播放信息并在左侧显示悬浮窗";
    public string IconGlyph => "\u266B"; // ♫ 音符图标
    public string Category => ToolCategory.Media;

    /// <summary>
    /// 创建工具的 UI 面板。包含打开/关闭悬浮窗的按钮。
    /// </summary>
    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };

        var btnOpen = new Button
        {
            Content = "打开悬浮窗",
            Height = 36,
            Margin = new Thickness(0, 4, 0, 4)
        };
        btnOpen.Click += (s, e) => Views.MusicFloatWindow.Instance.Show();

        var btnClose = new Button
        {
            Content = "关闭悬浮窗",
            Height = 36,
            Margin = new Thickness(0, 4, 0, 4)
        };
        btnClose.Click += (s, e) => Views.MusicFloatWindow.Instance.Hide();

        panel.Children.Add(btnOpen);
        panel.Children.Add(btnClose);

        return panel;
    }
}