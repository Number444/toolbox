using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Toolbox.Models;
using Toolbox.Core.Services;
using Toolbox.Tools.Views;
using Toolbox.Services;
using Toolbox.Controls;

namespace Toolbox.Tools;

/// <summary>
/// Toolbox 插件——网易云音乐实时信息悬浮窗。
/// UI 布局：左侧胶囊开关控制开/关，右侧带图标的药丸按钮显示并切换模式名。
/// 下方新增独立设置区域：悬浮窗透明度 45% + 锁定位置。
/// </summary>
public class NeteaseMusicTool : ITool
{
    public string Name => "网易云音乐悬浮窗";
    public string Description => "读取网易云音乐实时播放信息并在左侧显示悬浮窗";
    public string IconGlyph => "\u266B";
    public string Category => ToolCategory.Media;

    private PropertyChangedEventHandler? _settingsHandler;
    private EventHandler<bool>? _visibilityHandler;

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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ├─ 胶囊复选框（纯开关）
        var capsuleToggle = new CheckBox
        {
            Style = FindResourceStyle("CapsuleToggleStyle"),
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
            Style = FindResourceStyle("ModeBtnStyle"),
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
            capsuleToggle.IsChecked = MusicFloatWindowManager.Instance.IsVisible;
            var mode = AppSettings.Instance.MusicFloatSizeMode;
            var modeName = mode == "Compact" ? "紧凑模式" : "大模式";
            modeBtn.Content = BuildModeContent(modeName);
            isUpdating = false;
        }

        // 胶囊开关 → 打开/关闭悬浮窗
        capsuleToggle.Checked += (s, e) =>
        {
            if (isUpdating) return;
            var savedMode = AppSettings.Instance.MusicFloatSizeMode;
            var mode = savedMode == "Compact" ? FloatSizeMode.Compact : FloatSizeMode.Large;
            MusicFloatWindowManager.Instance.Show(mode,
                AudioflowSettings.Instance.FloatWindowBlurEnabled);
        };

        capsuleToggle.Unchecked += (s, e) =>
        {
            if (isUpdating) return;
            MusicFloatWindowManager.Instance.Hide();
        };

        // 模式按钮 → 切换大小模式
        modeBtn.Click += (s, e) =>
        {
            var currentMode = AppSettings.Instance.MusicFloatSizeMode;
            var newMode = currentMode == "Compact" ? "Large" : "Compact";
            AppSettings.Instance.MusicFloatSizeMode = newMode;

            var modeEnum = newMode == "Compact" ? FloatSizeMode.Compact : FloatSizeMode.Large;
            MusicFloatWindowManager.Instance.SetSizeMode(modeEnum);

            UpdateUI();
        };

        // 组装主行
        row.Children.Add(capsuleToggle);
        Grid.SetColumn(capsuleToggle, 0);
        var spacer1 = new Grid { Width = 8 };
        row.Children.Add(spacer1);
        Grid.SetColumn(spacer1, 1);
        row.Children.Add(label);
        Grid.SetColumn(label, 2);
        var spacer2 = new Grid { Width = 8 };
        row.Children.Add(spacer2);
        Grid.SetColumn(spacer2, 3);
        row.Children.Add(modeBtn);
        Grid.SetColumn(modeBtn, 4);

        // 复位位置按钮（与模式按钮同行，大小一致）
        var resetBtn = new Button
        {
            Content = "复位位置",
            Style = FindResourceStyle("ModeBtnStyle")
        };
        resetBtn.Click += (_, _) => MusicFloatWindowManager.Instance.ResetPosition();
        var spacer3 = new Grid { Width = 8 };
        row.Children.Add(spacer3);
        Grid.SetColumn(spacer3, 5);
        row.Children.Add(resetBtn);
        Grid.SetColumn(resetBtn, 6);

        root.Children.Add(row);
        root.Children.Add(new Grid { Height = 16 }); // 间距

        // ===== 设置卡片 =====
        var settingsBorder = new Border
        {
            Background = FindResourceBrush("BgSurfaceBrush", new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D))),
            BorderBrush = FindResourceBrush("BorderSubtleBrush", new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10)
        };
        GlowCardMarker.SetIsGlowCard(settingsBorder, true); // 纳入鼠标光照发光目标

        var settingsPanel = new StackPanel();

        // 设置标题
        var settingsTitle = new TextBlock
        {
            Text = "悬浮窗设置",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResourceBrush("TextPrimaryBrush", Brushes.White),
            Margin = new Thickness(0, 0, 0, 8)
        };
        settingsPanel.Children.Add(settingsTitle);

        // 复选框 1：悬浮窗 Acrylic 毛玻璃背景开关
        var cbBlur = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            Content = "毛玻璃模糊背景",
            Margin = new Thickness(0, 0, 0, 6)
        };
        cbBlur.SetBinding(ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("FloatWindowBlurEnabled")
            {
                Source = AudioflowSettings.Instance,
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

        // 复选框 2：锁定悬浮窗位置
        var cbLock = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            Content = "锁定悬浮窗位置",
            Margin = new Thickness(0, 0, 0, 6)
        };
        cbLock.SetBinding(ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("LockFloatWindow")
            {
                Source = AudioflowSettings.Instance,
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

        // 复选框 3：贴边自动缩入
        var cbEdgeDock = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            Content = "贴边自动缩入",
            Margin = new Thickness(0, 0, 0, 6)
        };
        cbEdgeDock.SetBinding(ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("EdgeDockEnabled")
            {
                Source = AudioflowSettings.Instance,
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

        settingsPanel.Children.Add(cbBlur);
        settingsPanel.Children.Add(cbLock);
        settingsPanel.Children.Add(cbEdgeDock);

        // 复选框 4：游戏模式（点击穿透）
        var cbClickThrough = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            Content = "游戏模式（鼠标穿透，不可点击）",
            Margin = new Thickness(0, 0, 0, 6)
        };
        cbClickThrough.SetBinding(ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("ClickThroughEnabled")
            {
                Source = AudioflowSettings.Instance,
                Mode = System.Windows.Data.BindingMode.TwoWay
            });
        settingsPanel.Children.Add(cbClickThrough);

        // 复选框 5：悬停播放控制
        var cbPlaybackControls = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            Content = "悬停封面显示播放控制",
            Margin = new Thickness(0, 0, 0, 6)
        };
        cbPlaybackControls.SetBinding(ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("ShowPlaybackControls")
            {
                Source = AudioflowSettings.Instance,
                Mode = System.Windows.Data.BindingMode.TwoWay
            });
        settingsPanel.Children.Add(cbPlaybackControls);

        settingsBorder.Child = settingsPanel;

        root.Children.Add(settingsBorder);

        root.Loaded += (_, _) =>
        {
            // 加载后刷新 UI
            UpdateUI();

            // 订阅可见性变化，实时同步胶囊开关
            _visibilityHandler = (_, visible) =>
                capsuleToggle.IsChecked = visible;
            MusicFloatWindowManager.Instance.VisibilityChanged += _visibilityHandler;
        };

        // AudioflowSettings 变化时实时同步
        _settingsHandler = (s, e) =>
        {
            var mgr = MusicFloatWindowManager.Instance;
            switch (e.PropertyName)
            {
                case nameof(AudioflowSettings.FloatWindowBlurEnabled):
                    mgr.ToggleBlur(AudioflowSettings.Instance.FloatWindowBlurEnabled);
                    break;
                case nameof(AudioflowSettings.LockFloatWindow):
                    mgr.SetWindowLocked(AudioflowSettings.Instance.LockFloatWindow);
                    break;
                case nameof(AudioflowSettings.EdgeDockEnabled):
                    mgr.DockService.Enabled = AudioflowSettings.Instance.EdgeDockEnabled;
                    if (!mgr.DockService.Enabled)
                        mgr.DockService.ForceRestore();
                    break;
            }
        };
        AudioflowSettings.Instance.PropertyChanged += _settingsHandler;

        // 工具页销毁时退订，避免处理器泄漏
        root.Unloaded += (_, _) =>
        {
            if (_settingsHandler != null)
            {
                AudioflowSettings.Instance.PropertyChanged -= _settingsHandler;
                _settingsHandler = null;
            }
            if (_visibilityHandler != null)
            {
                MusicFloatWindowManager.Instance.VisibilityChanged -= _visibilityHandler;
                _visibilityHandler = null;
            }
        };

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

    private static Style? FindResourceStyle(string key)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Style style)
                return style;
        }
        catch { }
        return null;
    }
}