# Toolbox 项目架构

> 最后更新: 2026-06-27

## 目录结构

```
Toolbox/
├── App.xaml (+ .cs)                     WPF 应用程序入口，定义全局深色主题资源（Button / TextBox / ComboBox / WindowButton 样式）
├── MainWindow.xaml (+ .cs)              主窗口：自定义标题栏(Win11三按钮) + 左侧导航(动画高亮) + 右侧内容(淡入过渡) + 状态栏
├── Toolbox.csproj                       主项目文件（WinExe, net9.0-windows）
├── AssemblyInfo.cs                      程序集信息
├── toolbox-new-tools-analysis.md        新增工具分析与实施指南
│
├── Helpers/
│   ├── CustomScrollBar.cs               自定义迷你滚动条（深色主题，替代系统 ScrollBar）
│   ├── TransitioningContentControl.cs   内容切换淡入动画控件
│   └── Win32Helper.cs                   Win11 DWM P/Invoke（圆角、Mica/MicaAlt 材质、帧扩展、暗黑模式）
│
├── Services/
│   └── ToolRegistry.cs                  运行时从 plugins/ 目录通过 Assembly.LoadFrom 动态加载插件 DLL
│
├── ViewModels/
│   └── MainViewModel.cs                 主窗口 ViewModel（工具列表、选中状态、UI 内容缓存）
│
├── Toolbox.Core/                        接口库（class library，被主项目 / 插件项目引用）
│   ├── Toolbox.Core.csproj
│   └── Models/
│       └── ITool.cs                     工具接口: Name / Description / IconGlyph / CreateContent()
│
├── Toolbox.Plugins/                     插件库（class library，独立编译，运行时热加载）
│   ├── Toolbox.Plugins.csproj
│   ├── Directory.Build.props            wpftmp 编译抑制（避免重复编译）
│   ├── ShutdownTool.cs                  定时关机工具（快捷按钮 + 自定义分钟 + 取消关机，已合并）
│
└── bin/Debug/net9.0-windows/
    ├── Toolbox.exe                      主程序
    ├── Toolbox.Core.dll                 接口库
    └── plugins/
        └── Toolbox.Plugins.dll          插件 DLL（热插拔，可独立替换）
```

## 项目间依赖关系

```
Toolbox ──→ Toolbox.Core          （编译期 ProjectReference，需要 ITool 接口）
         ──→ plugins/Toolbox.Plugins.dll（运行时 Assembly.LoadFrom，热插拔。主项目不直接引用插件，仅 Compile Remove 排除）

Toolbox.Plugins ──→ Toolbox.Core  （编译期 ProjectReference，实现 ITool 接口）
```

**关键设计**：`Toolbox.csproj` 通过 `<Compile Remove>` 排除子项目 `*.cs` 文件，防止 SDK 风格的通配编译导致类型重复。主项目编译时不涉及插件代码，插件 DLL 需独立编译后放入 `plugins/` 目录。

## 架构层次说明

### 层 1：Toolbox.Core（接口定义层）
- 定义 `ITool` 接口，作为所有工具的契约
- 四个成员：`Name`（导航栏显示）、`Description`（描述）、`IconGlyph`（Emoji 图标）、`CreateContent()`（返回 WPF UIElement）
- 无业务逻辑，纯抽象，被 Toolbox 和 Toolbox.Plugins 共同引用

### 层 2：Toolbox.Plugins（工具实现层）
- 每个工具一个 `.cs` 文件，实现 `ITool` 接口，`CreateContent()` 返回 `UIElement`
- 独立编译为 class library，DLL 手动部署到 `plugins/` 目录
- 增删工具 **不修改主项目代码**，重新编译插件 + 重启主程序即可
- `Directory.Build.props` 处理 `wpftmp` 临时编译项目的重复生成问题

### 层 3：Toolbox（主程序层）
- 启动时通过 `ToolRegistry.DiscoverTools()` 反射扫描 `plugins/Toolbox.Plugins.dll`
- `MainViewModel` 管理工具列表和选中态，缓存 `CurrentContent` 避免重复创建
- UI 布局在 `MainWindow.xaml`：自定义标题栏 → 左侧 ItemsControl 导航 + 动画高亮 → 右侧 TransitioningContentControl 淡入过渡 + 状态栏
- Win11 外观：DWM 圆角 (DWMWCP_ROUND)、Mica Alt 材质 (DWMSBT_TABBEDWINDOW，低版本自动降级)、帧扩展 (DwmExtendFrameIntoClientArea)

## 关键流程

### 启动流程
```
App.xaml → MainWindow.xaml (DataContext = MainViewModel)
         → MainWindow 构造函数：
            1. InitializeComponent()
            2. Loaded 事件 →
               - WindowInteropHelper 获取 HWND
               - Win32Helper.EnableRoundedCorners(hwnd)
               - Win32Helper.EnableMicaBackdrop(hwnd)
               - Win32Helper.ExtendFrameIntoClientArea(hwnd)
               - Dispatcher.BeginInvoke: UpdateCornerMask() + InitHighlight()
            3. StateChanged 事件 → UpdateMaximizeIcon()
         → MainViewModel 构造函数:
            ToolRegistry.DiscoverTools()
              → Assembly.LoadFrom("plugins/Toolbox.Plugins.dll")
              → 反射扫描 ITool 实现 → 实例化 → 按名称排序
              → 默认选中首个工具
```

### 切换工具流程
```
点击左侧导航项 (Border) → NavItem_MouseLeftButtonDown
                         → MainViewModel.SelectedTool = tool
                         → PropertyChanged("CurrentContent")
                         → TransitioningContentControl 淡入
                         → PositionHighlight() 带动画移动高亮条
```

### 更新 / 新增工具流程
```
新增:
  1. Toolbox.Plugins/ 下新建 {ToolName}.cs，实现 ITool
  2. dotnet build Toolbox.Plugins.csproj  (仅编译插件)
  3. 将 Toolbox.Plugins.dll 放入主程序 plugins/ 目录
  4. 重启 Toolbox.exe → 自动发现新工具

修改:
  1. 编辑 Toolbox.Plugins/ 下已有工具 .cs
  2. dotnet build Toolbox.Plugins.csproj  (< 3 秒)
  3. 替换 plugins/Toolbox.Plugins.dll
  4. 重启 Toolbox.exe → 加载新版
```

### 单文件发布流程（方案 C）
```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none
```
> 需配合将 `ToolRegistry` 中 `Assembly.LoadFrom` 改为 `Assembly.Load(new AssemblyName("Toolbox.Plugins"))`，并在 csproj 中增加对 `Toolbox.Plugins.csproj` 的 `ProjectReference`。

## 当前工具列表

| 工具 | 文件名 | 功能 |
|------|--------|------|
| 屏保启动 | `ScreensaverTool.cs` | ComboBox 选择 6 种系统屏保 (Blank/Bubbles/Mystify/Ribbons/3D Text/Photos)，一键启动 |
| 定时关机 | `ShutdownTool.cs` | 6 个快捷时长按钮 (1min~2h) / 自定义分钟输入 / 红色取消关机按钮（合并原独立取消工具） |

## ITool 接口规范

```csharp
namespace Toolbox.Models;

public interface ITool
{
    string Name { get; }           // 导航栏显示名称
    string Description { get; }    // 右侧区域描述文字
    string IconGlyph { get; }     // Emoji 图标字符
    UIElement CreateContent();     // 创建工具 UI（每次切换缓存复用，而非每次重建）
}
```

## 扩展新工具模板

在 `Toolbox.Plugins/` 下新建 `{ToolName}.cs`：

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

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 描述
        var desc = new TextBlock
        {
            Text = "这里写功能说明",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 20)
        };
        panel.Children.Add(desc);

        // 功能控件
        // ...

        return panel;
    }
}
```

编译后放入 `plugins/`，重启即可。

## 全局主题资源速查（App.xaml 定义）

| 资源 Key | 值 | 用途 |
|----------|---|------|
| `BgDarkBrush` | `#1C1C1C` | 右侧内容区背景 |
| `BgSurfaceBrush` | `#2D2D2D` | 左侧导航栏背景 |
| `BgCardBrush` | `#323232` | 卡片/输入框背景 |
| `BgHoverBrush` | `#3A3A3A` | 悬停高亮 |
| `AccentBrush` | `#60CDFF` | 主色调（按钮默认背景） |
| `TextPrimaryBrush` | `#F0F0F0` | 主文字 |
| `TextSecondaryBrush` | `#999999` | 次要/描述文字 |
| `BorderSubtleBrush` | `#3F3F3F` | 分隔线/边框 |
| `SuccessBrush` | `#63D47E` | 成功提示 |
| `DangerBrush` | `#F07070` | 危险/取消按钮 |
| `WindowButtonStyle` | 46x38 透明→#3A3A3A | 标题栏最小化/最大化按钮 |
| `CloseButtonStyle` | 继承+悬停#E81123 | 标题栏关闭按钮 |
| `GlobalFont` | Segoe UI, Microsoft YaHei | 全局字体 |

## 文件行数概览

| 文件 | 行数 | 说明 |
|------|:----:|------|
| App.xaml | 307 | 全局深色主题样式 + Button/TextBox/ComboBox/ScrollBar |
| MainWindow.xaml | 263 | 标题栏(Win11三按钮) + 左侧导航(高亮动画) + 右侧内容(淡入) + 状态栏 |
| ShutdownTool.cs | 215 | 快捷按钮 2×3 网格 + 自定义分钟 + 取消关机 |
| CustomScrollBar.cs | 248 | 自定义迷你滚动条控件 |
| MainWindow.xaml.cs | 189 | 窗口初始化(DWM/Mica) + 标题栏拖拽/双击 + 高亮动画 + 按钮事件 |
| ScreensaverTool.cs | 129 | ComboBox 选屏保 + 路径预览 + 一键启动 |
| Win32Helper.cs | 81 | 圆角/Mica/帧扩展 P/Invoke |
| MainViewModel.cs | 66 | 工具列表 + 选中状态 + UI 缓存 |
| TransitioningContentControl.cs | 46 | 淡入过渡控件 |
| ToolRegistry.cs | 53 | 反射扫描插件 + 优雅降级 |
| ITool.cs | 19 | 工具接口定义 |
| App.xaml.cs | 10 | 入口 |
| AssemblyInfo.cs | 9 | 程序集信息 |

**最大文件 307 行，保持在可控范围内。**