# 03 · Toolbox.Core 核心抽象层

> 核心抽象层（被主项目 / 插件引用）：接口定义 + 基础服务层，无 UI 依赖，纯抽象。

## Models/

| 文件 | 行数 | 职责 |
|------|:----:|------|
| ITool.cs | 21 | 工具接口：Name / Description / IconGlyph / Category / CreateContent() |
| ToolGroup.cs | 66 | 工具分组模型（IsExpanded / IsHovered / ArrowText / HoverIcon / CategoryColor） |
| ToolCategory.cs | 17 | 工具分类常量（7 大类，首页固定最前） |
| GlowCardMarker.cs | 24 | 卡片发光标记附加属性（IsGlowCard），卡片 Border 显式 opt-in |
| ThemeColors.cs | 28 | 统一主题色常量（供新工具使用，避免硬编码） |
| FloatSizeMode.cs | 4 | 悬浮窗大小模式枚举（Large / Compact），自插件层移入 Core |
| IMusicFloatController.cs | 35 | 悬浮窗控制器接口（主程序经此控制悬浮窗，不引用插件类型） |
| MusicFloatControllerHost.cs | 20 | 悬浮窗控制器静态宿主（插件加载后由 ToolRegistry 注册实现） |
| ToolNavigation.cs | 15 | 插件→主窗口导航请求中转（首页卡片点击跳工具） |

### ITool 接口规范

```csharp
namespace Toolbox.Models;

public interface ITool
{
    string Name { get; }           // 导航栏显示名称
    string Description { get; }    // 右侧区域描述文字
    string IconGlyph { get; }      // Emoji 图标字符
    string Category { get; }       // 分类名称（使用 ToolCategory 常量）
    UIElement CreateContent();     // 创建工具 UI（缓存复用机制）
}
```

### ToolCategory 分类常量

```csharp
public static class ToolCategory
{
    public const string Home    = "📊 首页";
    public const string System  = "⚙️ 系统维护";
    public const string Network = "🌐 网络与开发";
    public const string Window  = "🏠 窗口与桌面";
    public const string Text    = "🔤 文本与数据";
    public const string File    = "📁 文件管理";
    public const string Media   = "🎵 媒体与娱乐";
}
```

## Services/

| 文件 | 行数 | 职责 |
|------|:----:|------|
| AppSettings.cs | 190 | 单例全局设置（settings.json）：6 个开关 + 悬浮窗尺寸（详见 08-settings.md） |
| JsonSettingsFile.cs | 68 | 泛型 JSON 设置文件读写（原子写入 .tmp→替换，.bak 备份回落） |

## Controls/

| 文件 | 行数 | 职责 |
|------|:----:|------|
| ThemedMenuWindow.cs | 190 | 深色圆角主题弹出菜单窗口（DropShadowEffect 投影 + 屏幕边界吸附） |

## Helpers/

| 文件 | 行数 | 职责 |
|------|:----:|------|
| EdgeGlowLayer.cs | 472 | 控件边缘发光引擎（FrameworkElement 子类），主窗口与插件共用（详见 07-ui-system.md） |
| DwmHelper.cs | 242 | DWM 背景效果帮助类（Mica/Acrylic/圆角/深色模式/纯模糊，含 BackdropType / CornerPreference 枚举） |
| Win32Native.cs | 90 | 全项目唯一 Win32 P/Invoke 声明处（重复声明已清理） |

## 关键机制

- **JsonSettingsFile 原子写入**：先写 .tmp → 替换主文件；旧文件留 .bak；主文件损坏时 Load 自动回落 .bak（清理文件时勿删 .bak）
- **悬浮窗控制器抽象**：主程序经 `MusicFloatControllerHost.Current` 显示/隐藏/关闭/切换毛玻璃/大小模式/锁定/复位悬浮窗，不再直接引用插件类型；插件加载失败时控制器为 null，静默跳过
- **EdgeGlowLayer** 是全项目回归率最高的模块，改动前必读 `docs/EDGE_GLOW_REGRESSION_CHECKLIST.md`

## 相关文档

- 主程序层 → [02-main-app.md](02-main-app.md)
- 插件层 → [04-plugins.md](04-plugins.md)
- 配置项 → [08-settings.md](08-settings.md)
- UI 系统（光晕/主题）→ [07-ui-system.md](07-ui-system.md)
