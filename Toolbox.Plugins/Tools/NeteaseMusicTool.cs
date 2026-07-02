using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Toolbox.Models;
using Toolbox.Core.Services;
using Toolbox.Tools.Views;

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
    /// 创建工具的 UI 面板。卡片布局：打开/关闭按钮 + 形态选择。
    /// </summary>
    public UIElement CreateContent()
    {
        var root = new StackPanel { Margin = new Thickness(8) };

        // ── 打开/关闭按钮行 ──
        var btnOpen = new Button
        {
            Content = "打开悬浮窗",
            Height = 36,
            Margin = new Thickness(0, 4, 0, 4)
        };
        btnOpen.Click += (s, e) =>
        {
            var w = Views.MusicFloatWindow.Instance;

            // 加载保存的悬浮窗大小设置
            var savedMode = AppSettings.Instance.MusicFloatSizeMode;
            if (savedMode == "Compact")
                w.SizeMode = FloatSizeMode.Compact;
            else
                w.SizeMode = FloatSizeMode.Large;

            w.Show();
        };

        var btnClose = new Button
        {
            Content = "关闭悬浮窗",
            Height = 36,
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = FindResourceBrush("BorderSubtleBrush", Brushes.Gray),
            BorderThickness = new Thickness(1)
        };
        btnClose.Click += (s, e) => Views.MusicFloatWindow.Instance.Hide();

        root.Children.Add(btnOpen);
        root.Children.Add(btnClose);

        // ── 分隔线 ──
        root.Children.Add(new Rectangle
        {
            Height = 1,
            Fill = FindResourceBrush("BorderSubtleBrush", Brushes.LightGray),
            Margin = new Thickness(0, 8, 0, 8)
        });

        // ── 显示形态选择 ──
        var sizeLabel = new TextBlock
        {
            Text = "显示形态",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResourceBrush("TextSecondaryBrush", Brushes.Gray),
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(sizeLabel);

        var isCompact = AppSettings.Instance.MusicFloatSizeMode == "Compact";
        var sizeStatus = new TextBlock
        {
            Text = isCompact ? "当前：紧凑模式" : "当前：大模式",
            FontSize = 13,
            Foreground = FindResourceBrush("TextPrimaryBrush", Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center
        };
        var toggleBtn = new Button
        {
            Content = "切换大小",
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            BorderBrush = FindResourceBrush("BorderSubtleBrush", Brushes.Gray),
            BorderThickness = new Thickness(1)
        };
        toggleBtn.Click += (s, e) =>
        {
            var w = Views.MusicFloatWindow.Instance;
            if (!w.IsLoaded)
            {
                sizeStatus.Text = "请先打开悬浮窗";
                return;
            }
            var newMode = w.SizeMode == FloatSizeMode.Large
                ? FloatSizeMode.Compact
                : FloatSizeMode.Large;
            w.SizeMode = newMode;
            sizeStatus.Text = newMode == FloatSizeMode.Large
                ? "当前：大模式"
                : "当前：紧凑模式";

            // 保存设置
            AppSettings.Instance.MusicFloatSizeMode = newMode == FloatSizeMode.Large ? "Large" : "Compact";
        };

        var sizeRow = new Grid();
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sizeStatus.SetValue(Grid.ColumnProperty, 0);
        toggleBtn.SetValue(Grid.ColumnProperty, 1);
        sizeRow.Children.Add(sizeStatus);
        sizeRow.Children.Add(toggleBtn);
        root.Children.Add(sizeRow);

        return root;
    }

    private static Brush FindResourceBrush(string key, Brush fallback)
    {
        try
        {
            if (Application.Current?.Resources[key] is Brush brush)
                return brush;
        }
        catch { }
        return fallback;
    }
}