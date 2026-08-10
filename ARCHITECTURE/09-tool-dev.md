# 09 · 新工具开发指南

> 完整规范见 `docs/TOOL_DEVELOPMENT_GUIDELINE.md`（本页为架构侧摘要与入口）。

## 快速开始

1. 在 `Toolbox.Plugins/` 下新建 `{ToolName}.cs`
2. 实现 `Toolbox.Models.ITool` 接口，命名空间 `Toolbox.Tools`
3. 选择合适分类（`ToolCategory` 常量，普通工具不要用 Home）
4. `ToolRegistry` 反射自动发现，**无需注册**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Tools;

public class MyNewTool : ITool
{
    public string Name => "我的工具";
    public string Description => "工具功能描述";
    public string IconGlyph => "🔧";
    public string Category => ToolCategory.System;

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = "功能说明",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 20)
        });
        return panel;
    }
}
```

重新编译主项目后重启即可自动发现（ProjectReference 编译期静态绑定，插件 DLL 随主输出目录发布）。

## 可复用公共类（规范第三章节选）

| 类 | 用途 |
|----|------|
| `ThemeColors` | 颜色常量（禁止自定义色值） |
| `GlowCardMarker` | 卡片发光标记（`SetIsGlowCard(card, true)`） |
| `ThemedMenuWindow` | 深色右键菜单（`ShowAt()`） |
| `ConfirmDialog` | 确认弹窗 |
| `JsonSettingsFile` | JSON 设置读写（原子写入） |
| `DwmHelper` | DWM 窗口效果 |
| `ClickThroughHelper` | 点击穿透 |
| `MonitorHelper` | 多屏工作区 |
| `SystemPowerHelper` | 锁屏 / 关显示器 / 睡眠 |
| `SystemInfoHelper` | 内存占用 / 运行时长 / 磁盘 / IPv4 |
| `ToolNavigation` | 插件→主窗口导航请求 |

## 关键规则

- **UI 构建**：卡片标准模板（`BuildCard`）、按钮规范（标准/危险）、状态反馈模式（✅/❌/⚠️），见规范第四节
- **特效兼容**：Button/TextBox/ComboBox 自动发光；卡片 Border 手动标记；改动 EdgeGlowLayer 必读回归清单；主窗口新增动画须把元素/属性补进光晕跟踪清单（见 07-ui-system「重绘触发与动画同步」）
- **异常处理**：Process.Start / 文件 IO / 注册表必须 try-catch + 用户可见错误
- **禁止事项**：自定义颜色常量、自绘右键菜单、手写 JSON、根元素外包 ScrollViewer、非标准间距
- **复用边界**：只能使用/创建公共或本工具私有的方法和接口；不得调用其他工具类的内部方法、不得抽取其他工具逻辑

## 新增工具时的文档同步

1. 在 `ARCHITECTURE/tools/` 新增叶子文档（参考现有叶子模板）
2. 在 `ARCHITECTURE/04-plugins.md` 工具索引表登记
3. 按 `docs/TOOL_DEVELOPMENT_GUIDELINE.md` 验收
4. README 文档树为引用式（工具清单见 04-plugins 索引），无需维护

> 叶子文档按 README 约定：不写精确行数/计数；已知问题以 `docs/待解决-*.md` 为准，只引用不复述。

## 相关文档

- 完整规范 → docs/TOOL_DEVELOPMENT_GUIDELINE.md
- 回归清单 → docs/EDGE_GLOW_REGRESSION_CHECKLIST.md
- 工具叶子 → [tools/](tools/)
