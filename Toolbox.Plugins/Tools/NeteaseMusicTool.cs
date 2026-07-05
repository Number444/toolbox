using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;
using Toolbox.Core.Services;
using Toolbox.Tools.Views;

namespace Toolbox.Tools;

/// <summary>
/// Toolbox 插件——网易云音乐实时信息悬浮窗。
/// UI 布局：左侧胶囊开关控制开/关，右侧带图标的药丸按钮显示并切换模式名。
/// </summary>
public class NeteaseMusicTool : ITool
{
    public string Name => "网易云音乐悬浮窗";
    public string Description => "读取网易云音乐实时播放信息并在左侧显示悬浮窗";
    public string IconGlyph => "\u266B";
    public string Category => ToolCategory.Media;

    public UIElement CreateContent()
    {
        var root = new StackPanel { Margin = new Thickness(8) };

        // 主行：胶囊开关 | 文字说明 | 模式切换按钮
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ├─ 胶囊复选框（纯开关）
        var capsuleToggle = new CheckBox
        {
            Style = FindResourceStyle("CapsuleToggleStyle", null),
            VerticalAlignment = VerticalAlignment.Center
        };

        // ├─ 文字说明：悬浮窗
        var label = new TextBlock
        {
            Text = "悬浮窗",
            FontSize = 13,
            Foreground = FindResourceBrush("TextSecondaryBrush", Brushes.Gray),
            VerticalAlignment = VerticalAlignment.Center
        };

        // └─ 模式切换按钮（图标 + 文字）
        var modeBtn = new Button
        {
            Style = FindResourceStyle("ModeBtnStyle", null),
            VerticalAlignment = VerticalAlignment.Center
        };

        // ── 用 TextBlock + Run 构建带切换图标的文本内容 ──
        TextBlock BuildModeContent(string modeName)
        {
            return new TextBlock(new System.Windows.Documents.Run($"⇄ {modeName}"));
        }

        bool isUpdating = false;

        void UpdateUI()
        {
            isUpdating = true;
            var w = MusicFloatWindow.Instance;
            capsuleToggle.IsChecked = w.IsVisible;
            var mode = AppSettings.Instance.MusicFloatSizeMode;
            var modeName = mode == "Compact" ? "紧凑模式" : "大模式";
            modeBtn.Content = BuildModeContent(modeName);
            isUpdating = false;
        }

        // 胶囊开关 → 打开/关闭悬浮窗
        capsuleToggle.Checked += (s, e) =>
        {
            if (isUpdating) return;
            var w = MusicFloatWindow.Instance;
            var savedMode = AppSettings.Instance.MusicFloatSizeMode;
            w.SizeMode = savedMode == "Compact" ? FloatSizeMode.Compact : FloatSizeMode.Large;
            w.Show();
        };

        capsuleToggle.Unchecked += (s, e) =>
        {
            if (isUpdating) return;
            MusicFloatWindow.Instance.Hide();
        };

        // 模式按钮 → 切换大小模式
        modeBtn.Click += (s, e) =>
        {
            var currentMode = AppSettings.Instance.MusicFloatSizeMode;
            var newMode = currentMode == "Compact" ? "Large" : "Compact";
            AppSettings.Instance.MusicFloatSizeMode = newMode;

            var w = MusicFloatWindow.Instance;
            if (w.IsLoaded && w.IsVisible)
            {
                w.SizeMode = newMode == "Compact" ? FloatSizeMode.Compact : FloatSizeMode.Large;
            }

            UpdateUI();
        };

        // 组装
        row.Children.Add(capsuleToggle);
        Grid.SetColumn(capsuleToggle, 0);
        row.Children.Add(new Grid { Width = 8 });
        Grid.SetColumn(row.Children[^1] as Grid, 1);
        row.Children.Add(label);
        Grid.SetColumn(label, 2);
        row.Children.Add(new Grid { Width = 8 });
        Grid.SetColumn(row.Children[^1] as Grid, 3);
        row.Children.Add(modeBtn);
        Grid.SetColumn(modeBtn, 4);
        root.Children.Add(row);

        root.Loaded += (_, _) => UpdateUI();

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

    private static Style? FindResourceStyle(string key, Style? fallback)
    {
        try
        {
            if (Application.Current?.Resources[key] is Style style)
                return style;
        }
        catch { }
        return fallback;
    }
}