# Toolbox 新工具开发规范

> 本文档规定新工具的写法标准，覆盖类复用、控件复用、外观一致性、特效兼容性。
> 遵守本规范的工具将自动获得：统一深色主题、鼠标边缘发光、右键菜单、设置持久化等能力。

---

## 一、文件与命名空间

```
Toolbox.Plugins/MyTool.cs          # 工具文件
命名空间: Toolbox.Tools             # 必须
```

工具类实现 `Toolbox.Models.ITool` 接口，**不需要**在项目中额外注册 —— `ToolRegistry` 会通过反射自动发现。

---

## 二、ITool 接口规范

```csharp
using Toolbox.Models;

public class MyTool : ITool
{
    public string Name => "工具名称";           // 左侧导航栏显示
    public string Description => "一句话描述";    // 详情区标题
    public string IconGlyph => "🔧";            // 单个 Emoji 字符
    public string Category => ToolCategory.System;  // 必须用 ToolCategory 常量
    public UIElement CreateContent() { ... }
}
```

**分类常量（`ToolCategory`）：**

| 常量 | 含义 |
|------|------|
| `ToolCategory.Home` | 📊 首页（保留给仪表盘，普通工具不要使用） |
| `ToolCategory.System` | ⚙️ 系统维护 |
| `ToolCategory.Network` | 🌐 网络与开发 |
| `ToolCategory.Window` | 🏠 窗口与桌面 |
| `ToolCategory.Text` | 🔤 文本与数据 |
| `ToolCategory.File` | 📁 文件管理 |
| `ToolCategory.Media` | 🎵 媒体与娱乐 |

> 普通工具请用 Home 以外的分类;Home 固定排在导航最前，是启动默认页。

---

## 三、可复用类目录

以下是开发新工具时会用到的所有共享类，按使用频率排列。

### 3.1 外观 —— `ThemeColors`

```csharp
using Toolbox.Models;  // 命名空间

// 6 个颜色常量，必须使用，不得自行定义色值
new SolidColorBrush(ThemeColors.BgDark)         // #2D2D2D  卡片/面板背景
new SolidColorBrush(ThemeColors.TextPrimary)    // #F0F0F0  标题/正文
new SolidColorBrush(ThemeColors.TextSecondary)  // #808080  描述/提示
new SolidColorBrush(ThemeColors.Success)        // #63D47E  成功状态（绿）
new SolidColorBrush(ThemeColors.Danger)         // #F07070  错误/危险（红）
new SolidColorBrush(ThemeColors.Warning)        // #E0A030  警告（橙）
```

> **禁止**在工具类中定义 `private static readonly Color BgDark = ...`，统一用 `ThemeColors`。

### 3.2 卡片发光标记 —— `GlowCardMarker`

```csharp
using Toolbox.Models;

var card = new Border { ... };
GlowCardMarker.SetIsGlowCard(card, true);   // 鼠标靠近时自动边缘发光
```

> **规则**：只标记"卡片容器"的 Border。按钮模板内部、分隔线、展示面板不标记。`EdgeGlowLayer` 会自动为 `Button`/`TextBox`/`ComboBox` 发光，无需手动标记。

### 3.3 右键菜单 —— `ThemedMenuWindow`

```csharp
using Toolbox.Core.Controls;

ThemedMenuWindow.ShowAt(screenPoint, new[] {
    new ThemedMenuWindow.Item {
        Text = "菜单项",
        IsChecked = settings.SomeToggle,      // 可选：显示 ✓
        IsEnabled = true,                     // 可选：默认 true
        Action = () => DoSomething()
    },
    ThemedMenuWindow.Item.Separator(),
    new ThemedMenuWindow.Item {
        Text = "退出",
        Action = () => Shutdown()
    }
});
```

### 3.4 确认弹窗 —— `ConfirmDialog`

```csharp
var dlg = new ConfirmDialog("确定要执行此操作吗？", "确认", "确定删除");
dlg.ShowDialog();
if (dlg.Confirmed) { /* 执行 */ }
```

### 3.5 设置持久化 —— `JsonSettingsFile`

```csharp
using Toolbox.Core.Services;

// 读取
var data = JsonSettingsFile.Load<MyData>("path/to/settings.json");

// 保存（原子写入：先写 .tmp 再替换，旧文件自动留作 .bak 备份；
// 主文件损坏时 Load 自动回落 .bak，不会因断电/强杀丢光数据）
JsonSettingsFile.Save("path/to/settings.json", data);
```

如需完整的 PropertyChanged + 自动存盘模式，参考 `AppSettings.cs` 或 `AudioflowSettings.cs` 的实现。

> **注意**：`Load` 在主文件损坏时会静默回落 `.bak`，不要再自己实现备份逻辑，也不要在清理"旧文件"时误删 `.bak`。

### 3.6 DWM 窗口效果 —— `DwmHelper`

```csharp
using Toolbox.Tools.Helpers;

// 设置背景材质（MainWindow 回调挂载绑定）
DwmHelper.SetBackdrop(window, BackdropType.Acrylic);
DwmHelper.SetWindowCorners(window, CornerPreference.Round);
DwmHelper.SetImmersiveDarkMode(window, true);
DwmHelper.ExtendFrameIntoClientArea(window);

// 版本检测
DwmHelper.IsWindows11_22H2OrLater();
```

### 3.7 点击穿透 —— `ClickThroughHelper`

```csharp
using Toolbox.Tools.Helpers;

// 窗口 OnSourceInitialized 时注册
ClickThroughHelper.OnSourceInitialized(this, layered: false);

// 开关穿透
ClickThroughHelper.SetClickThrough(window, true, layered: false);
```

### 3.8 多屏工作区 —— `MonitorHelper`

```csharp
using Toolbox.Tools.Helpers;

var wa = MonitorHelper.GetMonitorWorkAreaDips(window);
```

### 3.8.1 系统电源操作 —— `SystemPowerHelper`

```csharp
using Toolbox.Tools.Helpers;

SystemPowerHelper.Lock();            // 锁定电脑（Win+L）
SystemPowerHelper.TurnOffMonitor();  // 关闭显示器
SystemPowerHelper.Sleep();           // 睡眠
// 均返回 bool 表示成败；插件层自含 P/Invoke，不要引用主程序的 Win32Helper
```

### 3.8.2 轻量系统信息 —— `SystemInfoHelper`

```csharp
using Toolbox.Tools.Helpers;

SystemInfoHelper.GetMemoryUsagePercent();  // 内存占用 %(int?)
SystemInfoHelper.GetUptime();              // 运行时长 TimeSpan
SystemInfoHelper.GetDriveSpace("C:");      // (free, total) 字节
SystemInfoHelper.GetLocalIPv4();           // 本机首个有网关网卡的 IPv4
SystemInfoHelper.GetPublicIPv4Async();     // 公网 IPv4（双源 fallback，5s 超时）
SystemInfoHelper.GetBatteryInfo();         // 电池剩余%+状态（笔记本；桌面机 IsBatteryPresent=false）
```

### 3.8.3 工具间导航 —— `ToolNavigation`

```csharp
using Toolbox.Models;

// 请求主窗口切换到指定名称的工具（如仪表盘卡片点击跳转）
ToolNavigation.Request("网络信息");
// 插件无法引用主程序的 MainViewModel，必须经此 Core 中转；主窗口负责实际切换与高亮跟随
```

### 3.9 Win32 / DWM 基础调用 —— `Win32Helper`

> ⚠️ **主程序专用**：`Win32Helper` 位于主程序（Toolbox.csproj），而 `Toolbox.Plugins` 仅引用 `Toolbox.Core`，工具**无法访问**此类。工具需要 DWM 效果请用 `DwmHelper`（3.6）。

```csharp
using Toolbox.Helpers;   // Toolbox 主项目命名空间

Win32Helper.FindWindowByTitle("Toolbox");
Win32Helper.EnableRoundedCorners(hwnd);
Win32Helper.EnableDarkMode(hwnd);
Win32Helper.ExtendFrameIntoClientArea(hwnd);
```

### 3.10 自定义滚动条 —— `CustomScrollBar`

> ⚠️ **主程序专用**：位于主程序（Toolbox.csproj），工具插件无法引用（会形成循环依赖）。工具页面滚动由主窗口 `ContentScrollViewer` 统一接管，无需工具关心。

```xml
<!-- XAML 引用（仅主程序内可用） -->
<helpers:CustomScrollBar TargetScrollViewer="{Binding ElementName=MyScrollViewer}"/>
```

### 3.11 淡入过渡 —— `TransitioningContentControl`

> ⚠️ **主程序专用**：同上，工具插件无法引用。工具只需从 `CreateContent()` 返回 `UIElement`，主窗口自动套用 200ms 淡入。

```xml
<!-- XAML 引用（仅主程序内可用） -->
<helpers:TransitioningContentControl Content="{Binding MyContent}"/>
```

---

## 四、UI 构建标准模式

### 4.1 页面整体结构

```csharp
public UIElement CreateContent()
{
    var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

    // ① 说明文字
    panel.Children.Add(new TextBlock
    {
        Text = "工具功能说明。",
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
        Margin = new Thickness(0, 0, 0, 16)
    });

    // ② 卡片区域（见 4.2）
    var card = BuildCard("卡片标题");
    // ... 向卡片内容面板追加子元素 ...
    card.Margin = new Thickness(0, 0, 0, 12);
    panel.Children.Add(card);

    // ③ 状态文字（固定在底部）
    var statusBlock = new TextBlock
    {
        Text = "",
        FontSize = 13,
        Margin = new Thickness(0, 12, 0, 0)
    };
    panel.Children.Add(statusBlock);

    return panel;  // 不要包 ScrollViewer
}
```

### 4.2 卡片构建（标准模板）

```csharp
private static Border BuildCard(string title)
{
    var inner = new StackPanel();
    inner.Children.Add(new TextBlock
    {
        Text = title,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
        Margin = new Thickness(0, 0, 0, 10)
    });

    var card = new Border
    {
        Background = new SolidColorBrush(ThemeColors.BgDark),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        Child = inner
    };
    GlowCardMarker.SetIsGlowCard(card, true);
    return card;
}
```

**带内容版：**
```csharp
private static Border BuildCard(string title, UIElement content)
{
    var card = BuildCard(title);
    ((StackPanel)card.Child).Children.Add(content);
    return card;
}
```

**卡片间间距**：`card.Margin = new Thickness(0, 0, 0, 12);`

### 4.3 按钮规范

**标准按钮**（全局样式自动应用绿色主题）：
```csharp
new Button
{
    Content = "🔍 开始操作",
    FontSize = 14,
    Padding = new Thickness(14, 6, 14, 6),
    Height = 42,
    HorizontalAlignment = HorizontalAlignment.Left
};
```

**危险按钮**：
```csharp
new Button
{
    Content = "🛑 取消",
    Background = new SolidColorBrush(ThemeColors.Danger),
    Foreground = Brushes.White
};
```

**CheckBox（需要自定义样式时）**：
```csharp
new CheckBox
{
    Style = FindResourceStyle("ClassicCheckBoxStyle"),
    Content = "选项",
    IsChecked = true,
    FontSize = 13,
    Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
};
```

**Switch 开关**：
```csharp
new CheckBox
{
    Style = FindResourceStyle("ToggleSwitchStyle"),
    IsChecked = true
};
```

### 4.4 状态反馈模式

```csharp
// 成功
statusBlock.Text = "✅ 操作成功";
statusBlock.Foreground = new SolidColorBrush(ThemeColors.Success);

// 失败（显示异常消息）
statusBlock.Text = $"❌ 操作失败：{ex.Message}";
statusBlock.Foreground = new SolidColorBrush(ThemeColors.Danger);

// 警告
statusBlock.Text = "⚠️ 请检查输入";
statusBlock.Foreground = new SolidColorBrush(ThemeColors.Warning);
```

### 4.5 FindResourceStyle 辅助方法

```csharp
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
```

可用 key：`"ClassicCheckBoxStyle"`、`"ToggleSwitchStyle"`、`"CapsuleToggleStyle"`。

---

## 五、特效兼容性

### 5.1 边缘发光（EdgeGlowLayer）

**自动兼容的元素**（无需任何操作）：
- 所有 `Button`、`ToggleButton`、`CheckBox`、`RadioButton`
- 所有 `ComboBox`
- 所有 `TextBox`

**手动标记的元素**：
- 卡片外框 `Border`：`GlowCardMarker.SetIsGlowCard(card, true);`

**不标记的元素**：
- 纯展示面、分隔线、装饰容器、ComboBox 内部模板的 Border

> **回归警示**：`EdgeGlowLayer` 是全项目回归率最高的模块（遮挡透出/Hover 消失曾多次互修互发）。
> 任何改动它或其调用方（hover 跟踪、ScrollViewer、裁剪）前，必读并按
> `docs/EDGE_GLOW_REGRESSION_CHECKLIST.md` 逐项实测后方可交付。
> 复选框等小控件享有更大的感应半径（`CheckBoxRangeScale`），属预期行为。

### 5.2 鼠标跟随光晕（Mouse Halo）

全局自动，无需工具侧任何代码。受 `AppSettings.MouseHaloEnabled` 控制。

### 5.3 内容淡入过渡（TransitioningContentControl）

工具只需返回 `UIElement`，主窗口的 `TransitioningContentControl` 自动处理切换时的 200ms 淡入动画。

### 5.4 系统托盘（SystemTrayHelper）

工具不应直接使用 `SystemTrayHelper`，它由主窗口的关闭即最小化逻辑管理。

### 5.5 主题暗色菜单（ThemedMenuWindow）

工具需要右键菜单时使用 `ThemedMenuWindow.ShowAt()`，自动获得深色圆角一致外观。

---

## 六、异常处理规范

每个工具中**可能失败的操作**必须加 try-catch 保护：

```csharp
// Process.Start
try { Process.Start(...); }
catch (Exception ex) { statusBlock.Text = $"❌ 启动失败：{ex.Message}"; }

// 文件 IO
try { File.WriteAllBytes(path, data); }
catch (Exception ex) { statusBlock.Text = $"❌ 保存失败：{ex.Message}"; }

// 注册表
try { using var key = Registry.CurrentUser.OpenSubKey(...); ... }
catch (Exception ex) { Debug.WriteLine($"注册表操作失败: {ex.Message}"); }
```

**原则**：工具内部异常不应扩散到主窗口导致应用崩溃，应就地捕获并展示用户可读的错误信息。

---

## 七、禁止事项

| ❌ 禁止 | ✅ 应该 |
|---------|--------|
| 自定颜色常量 `static readonly Color BgDark = ...` | 使用 `ThemeColors.BgDark` |
| 卡片不用 `GlowCardMarker` 标记 | 标记所有卡片 Border |
| 自绘右键菜单 | 使用 `ThemedMenuWindow.ShowAt()` |
| 手写 JSON 序列化/反序列化 | 使用 `JsonSettingsFile.Load/Save` |
| 跳过 try-catch 的 Process.Start / 文件 IO | 必须 try-catch + 用户可见错误信息 |
| 在 `CreateContent()` 返回的根元素外再包 `ScrollViewer` | 信赖主窗口的 `ContentScrollViewer` |
| 使用非标准间距/字号 | 遵循第 4 节的标准数值 |

---

## 八、完整示例

以下是一个符合全部规范的最小工具：

```csharp
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Tools;

public class ExampleTool : ITool
{
    public string Name => "示例工具";
    public string Description => "演示新工具写法规范";
    public string Category => ToolCategory.System;
    public string IconGlyph => "⭐";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 说明文字
        panel.Children.Add(new TextBlock
        {
            Text = "这是一个示例工具，展示规范写法。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 16)
        });

        // 状态文字
        var statusBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0)
        };
        panel.Children.Add(statusBlock);

        // 操作按钮
        var btn = new Button
        {
            Content = "🚀 执行操作",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        btn.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    UseShellExecute = true
                });
                statusBlock.Text = "✅ 记事本已启动";
                statusBlock.Foreground = new SolidColorBrush(ThemeColors.Success);
            }
            catch (Exception ex)
            {
                statusBlock.Text = $"❌ 操作失败：{ex.Message}";
                statusBlock.Foreground = new SolidColorBrush(ThemeColors.Danger);
            }
        };

        // 卡片
        var card = BuildCard("操作区域");
        ((StackPanel)card.Child).Children.Add(btn);
        card.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(card);

        return panel;
    }

    private static Border BuildCard(string title)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var card = new Border
        {
            Background = new SolidColorBrush(ThemeColors.BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = inner
        };
        GlowCardMarker.SetIsGlowCard(card, true);
        return card;
    }
}
```

---

*本规范随代码库中可复用类的增加持续更新。*
