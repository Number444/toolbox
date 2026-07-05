# Toolbox 项目架构

> 最后更新: 2026-07-05

## 目录结构

```
Toolbox/
├── App.xaml (+ .cs)                     WPF 应用程序入口，定义全局深色主题资源（Button / TextBox / ComboBox / CheckBox / WindowButton / ScrollBar 样式）
├── MainWindow.xaml (+ .cs)              主窗口：自定义标题栏(Win11三按钮) + 左侧导航(动画高亮) + 右侧内容(淡入过渡) + 状态栏 + 设置层
├── Toolbox.csproj                       主项目文件（WinExe, net9.0-windows10.0.19041.0）
├── AssemblyInfo.cs                      程序集信息
│
├── Helpers/
│   ├── CustomScrollBar.cs               自定义迷你滚动条（深色主题，替代系统 ScrollBar）
│   ├── TransitioningContentControl.cs   内容切换淡入动画控件
│   └── Win32Helper.cs                   Win11 DWM P/Invoke（圆角、Mica/MicaAlt 材质、帧扩展、暗黑模式）+ WM_NCCALCSIZE/WM_ERASEBKGND 拦截 + WndProc 消息钩子
│
├── Services/
│   └── ToolRegistry.cs                  运行时从 plugins/ 目录通过 Assembly.LoadFrom 动态加载插件 DLL
│
├── Views/
│   ├── SettingsView.xaml (+ .cs)        设置页面：3 个 ToggleSwitch（最小化/悬浮窗/自启）+ 悬浮窗大小选择 + 退出按钮
│
├── ViewModels/
│   └── MainViewModel.cs                 主窗口 ViewModel（工具列表、选中状态、UI 内容缓存、分组与搜索过滤）
│
├── Toolbox.Core/                        接口库（class library，被主项目 / 插件项目引用）
│   ├── Toolbox.Core.csproj
│   ├── Models/
│   │   ├── ITool.cs                     工具接口: Name / Description / IconGlyph / CreateContent()
│   │   ├── ToolGroup.cs                 工具分组模型（IsExpanded / IsHovered / ArrowText 状态）
│   │   └── ToolCategory.cs              工具分类常量
│   └── Services/
│       └── AppSettings.cs               单例应用设置（MinimizeOnClose / AutoOpenFloatWindow / AutoStart / MusicFloatSizeMode）
│                                        JSON 持久化到 %LOCALAPPDATA%\Toolbox\settings.json，AutoStart 同步注册表
│
├── Toolbox.Plugins/                     插件库（class library，独立编译，运行时热加载）
│   ├── Toolbox.Plugins.csproj
│   ├── Directory.Build.props            wpftmp 编译抑制（避免重复编译）
│   ├── ScreensaverTool.cs               屏保工具（ComboBox 选屏保 + 一键启动）
│   ├── ShutdownTool.cs                  定时关机工具（快捷按钮 + 自定义分钟 + 取消关机，已合并）
│   ├── QRCodeTool.cs                    二维码生成工具
│   ├── NeteaseMusicTool.cs              网易云音乐悬浮窗工具
│   └── ForceDeleteTool.cs               强制删除文件工具
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

### 层 1：Toolbox.Core（接口定义 + 基础服务层）
- 定义 `ITool` 接口，作为所有工具的契约
- 四个成员：`Name`（导航栏显示）、`Description`（描述）、`IconGlyph`（Emoji 图标）、`CreateContent()`（返回 WPF UIElement）
- `ToolGroup` 分组模型支持导航栏手风琴式展开/折叠
- `AppSettings` 单例管理所有用户偏好，JSON 持久化 + INotifyPropertyChanged
- 无 UI 依赖，纯抽象，被 Toolbox 和 Toolbox.Plugins 共同引用

### 层 2：Toolbox.Plugins（工具实现层）
- 每个工具一个 `.cs` 文件，实现 `ITool` 接口，`CreateContent()` 返回 `UIElement`
- 独立编译为 class library，DLL 手动部署到 `plugins/` 目录
- 增删工具 **不修改主项目代码**，重新编译插件 + 重启主程序即可
- `Directory.Build.props` 处理 `wpftmp` 临时编译项目的重复生成问题
- 已实现工具：屏保启动、定时关机、二维码生成、网易云音乐悬浮窗、强制删除文件

### 层 3：Toolbox（主程序层）
- 启动时通过 `ToolRegistry.DiscoverTools()` 反射扫描 `plugins/Toolbox.Plugins.dll`
- `MainViewModel` 管理工具列表、分组搜索、选中态、UI 内容缓存
- UI 布局在 `MainWindow.xaml`：自定义标题栏 → 左侧手风琴导航 + 动画高亮 → 右侧 TransitioningContentControl 淡入过渡 + 状态栏 + 设置浮层
- Win11 外观：DWM 圆角 (DWMWCP_ROUND)、Mica Alt 材质 (DWMSBT_TABBEDWINDOW，低版本自动降级)、帧扩展 (DwmExtendFrameIntoClientArea)
- 设置页面 (`SettingsView.xaml`) 作为浮层叠加在内容区上方，通过 `BackRequestedEvent` 气泡事件返回

## 关键流程

### 启动流程
```
App.xaml → MainWindow.xaml (DataContext = MainViewModel)
         → MainWindow 构造函数：
            1. InitializeComponent()
            2. Loaded 事件 →
               - WindowInteropHelper 获取 HWND
               - Win32Helper.EnableDarkMode(hwnd)            // 3. 沉浸式深色模式
               - Win32Helper.SetBorderColor(hwnd)             // 4. 压制 DWM 系统边框
               - Win32Helper.ExtendFrameIntoClientArea(hwnd)  // 5. 扩展帧到标题栏
               - HwndSource.AddHook(Win32Helper.WndProc)      // 6. WM_NCCALCSIZE + WM_ERASEBKGND 拦截
               - HwndTarget.BackgroundColor = Transparent     // 7. DirectX 交换链透明
               - Dispatcher.BeginInvoke: UpdateCornerMask() + InitHighlight()
            3. StateChanged 事件 → UpdateMaximizeIcon()
         → MainViewModel 构造函数:
            ToolRegistry.DiscoverTools()
              → Assembly.LoadFrom("plugins/Toolbox.Plugins.dll")
              → 反射扫描 ITool 实现 → 实例化 → 按分类分组
              → "⚙️ 系统维护" 默认展开，其余折叠
```
> **启动步骤 3-7 为完整的三层 Win32/DirectX 拦截链**，用于消除特定机器上的窗口边缘白色线条残留。

### 设置流程
```
点击标题栏齿轮按钮 → SettingsLayer.Visibility = Visible
                   → SettingsView 加载，所有控件绑定 AppSettings.Instance
                   → ToggleSwitch 通过 Trigger.EnterActions/ExitActions 播放滑块动画
                   → BackButton → BackRequestedEvent 气泡事件 → MainWindow 隐藏设置层
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

### Inno Setup 安装包发布流程
```
dotnet publish Toolbox.csproj -c Release -r win-x64 --self-contained true ^
  -o setup/publish -p:DebugType=none

→ ISCC.exe setup/ToolboxSetup.iss (LZMA2 最高压缩)
→ setup/Toolbox_Setup.exe (~54 MB)
```
- 安装到 `{autopf}\Toolbox`（需管理员权限）
- 桌面快捷方式可选
- 安装完成后可选启动 Toolbox

## UI 白色线条修复方案（三层 Win32/DirectX 拦截）

### 根因
`AllowsTransparency="False"` + `WindowStyle="None"` + `DwmExtendFrameIntoClientArea(-1)` 组合下，三个独立白色来源在窗口外缘叠加：
1. WPF GDI 非客户区 1px 骨架（`WindowChrome` 无法彻底消除）
2. 系统默认 `WM_ERASEBKGND` 白色背景底漆
3. DirectX 交换链默认白色清除色 (`HwndTarget.BackgroundColor`)

在特定 GPU 驱动 + 非整数 DPI 缩放下未被抗锯齿糊掉，合为可见白色线条。

### 解决

| 防御层 | 层面 | 方法 |
|--------|------|------|
| 1. `WM_NCCALCSIZE` 拦截 | Win32 消息 | 返回 0，宣告无 NC 区域 |
| 2. `WM_ERASEBKGND` 拦截 | Win32 消息 | 返回 1，跳过系统默认白色底漆 |
| 3. `HwndTarget.BackgroundColor = Transparent` | DirectX 交换链 | 禁止渲染目标白色清除 |

实现位置：
- `Win32Helper.WndProc()` 静态回调 — `MainWindow.xaml.cs` 中通过 `HwndSource.AddHook` 注册
- `MainWindow.xaml.cs` Loaded 事件 — 直接设置 `HwndTarget.BackgroundColor`

## AppSettings 配置项

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MinimizeOnClose` | `bool` | `true` | 关闭按钮最小化到任务栏 |
| `AutoOpenFloatWindow` | `bool` | `false` | 启动时自动打开悬浮窗 |
| `AutoStart` | `bool` | `false` | 开机自动启动 Toolbox（同步 `HKCU\...\Run\Toolbox`） |
| `MusicFloatSizeMode` | `string` | `"Large"` | 悬浮窗默认大小（Large / Compact） |

持久化：`%LOCALAPPDATA%\Toolbox\settings.json`，JSON 格式。所有属性变更触发 `PropertyChanged` + `Save()`。

## 当前工具列表

| 工具 | 文件名 | 功能 |
|------|--------|------|
| 定时关机 | `ShutdownTool.cs` | 6 个快捷时长按钮 (1min~2h) / 自定义分钟输入 / 红色取消关机按钮 |
| 屏保启动 | `ScreensaverTool.cs` | ComboBox 选择 6 种系统屏保，一键启动 |
| 二维码生成 | `QRCodeTool.cs` | 文本输入 → 实时生成二维码图片 |
| 网易云音乐悬浮窗 | `NeteaseMusicTool.cs` | 可拖拽悬浮窗、歌词显示、封面展示 |
| 强制删除文件 | `ForceDeleteTool.cs` | 文件路径输入 → 强制删除被占用的文件 |
| 重启资源管理器 | (内嵌) | 一键重启 explorer.exe |

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

        var desc = new TextBlock
        {
            Text = "这里写功能说明",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 20)
        };
        panel.Children.Add(desc);

        return panel;
    }
}
```

编译后放入 `plugins/`，重启即可。

## 全局主题资源速查（App.xaml 定义）

| 资源 Key | 值 | 用途 |
|----------|---|------|
| `BgDarkBrush` | `#1C1C1C` | 右侧内容区背景 |
| `BgSurfaceBrush` | `#2D2D2D` | 左侧导航栏背景 / 卡片背景 |
| `BgCardBrush` | `#323232` | 卡片/输入框背景 |
| `BgHoverBrush` | `#3A3A3A` | 悬停高亮 |
| `AccentBrush` | `#76B580` | 主色调（按钮默认背景、ToggleSwitch 选中色） |
| `AccentHoverBrush` | `#92CD9B` | 按钮悬停 |
| `TextPrimaryBrush` | `#F0F0F0` | 主文字 |
| `TextSecondaryBrush` | `#999999` | 次要/描述文字 |
| `BorderSubtleBrush` | `#3F3F3F` | 分隔线/边框 |
| `SuccessBrush` | `#63D47E` | 成功提示 |
| `DangerBrush` | `#F07070` | 危险/取消按钮 |
| `GlobalFont` | Segoe UI, Microsoft YaHei | 全局字体 |

### 自定义样式

| 样式 Key | 说明 |
|----------|------|
| `WindowButtonStyle` | 46x38 透明→#3A3A3A 标题栏按钮 |
| `CloseButtonStyle` | 继承+悬停#E81123 关闭按钮 |
| `ToggleSwitchStyle` | Win11 风格极简滑块开关（轨道 42x22 + 滑块 18x18 + EnterActions/ExitActions 滑动动画） |
| `ClassicCheckBoxStyle` | 方框+对勾传统复选框（备用） |
| `CustomScrollBar` | 迷你深色滚动条（宽 8px） |

### ToggleSwitch 动画机制

```
Trigger.IsChecked=True →
  EnterActions: DoubleAnimation → Thumb.(UIElement.RenderTransform).(TranslateTransform.X) To="20"
  ExitActions:  DoubleAnimation → Thumb.(UIElement.RenderTransform).(TranslateTransform.X) To="0"
  CubicEase EasingMode="EaseOut"，时长 0.2s
```

**注意**：动画必须绑定在 `Trigger.EnterActions`/`ExitActions` 上，**不能**使用 `EventTrigger`——`EventTrigger` 内的 Storyboard 在 ControlTemplate 名称范围内无法解析 `TargetName`，会导致 `XamlParseException` 进程崩溃。

## 即时预览检查清单

检查过的文件内容匹配最新代码。如有新增文件/样式请在相应章节更新。

## 文件行数概览

| 文件 | 行数 | 说明 |
|------|:----:|------|
| App.xaml | 445 | 全局深色主题 + Button/TextBox/ComboBox/CheckBox(2种)/ScrollBar |
| MainWindow.xaml | 263 | 标题栏(Win11三按钮) + 左侧导航(手风琴+高亮动画) + 右侧内容(淡入) + 状态栏 + 设置浮层 |
| MainWindow.xaml.cs | 195 | 窗口初始化(DWM/Mica/三层拦截) + 标题栏拖拽/双击 + 手风琴动画 + 高亮动画 + 设置层显隐 |
| SettingsView.xaml | 88 | 3 个 ToggleSwitch + ComboBox 悬浮窗大小 + 退出按钮 |
| Win32Helper.cs | 130 | 圆角/Mica/帧扩展/深色模式/边框色 P/Invoke + WM_NCCALCSIZE/WM_ERASEBKGND + WndProc |
| AppSettings.cs | 120 | 单例设置 + JSON 持久化 + AutoStart 注册表同步 |
| ShutdownTool.cs | 215 | 快捷按钮 2×3 网格 + 自定义分钟 + 取消关机 |
| ScreensaverTool.cs | 129 | ComboBox 选屏保 + 路径预览 + 一键启动 |
| CustomScrollBar.cs | 248 | 自定义迷你滚动条控件 |
| MainViewModel.cs | 66 | 工具列表 + 分组搜索 + 选中状态 + UI 缓存 |
| ToolRegistry.cs | 53 | 反射扫描插件 + 优雅降级 |
| TransitioningContentControl.cs | 46 | 淡入过渡控件 |
| ITool.cs | 19 | 工具接口定义 |
| ToolGroup.cs | 28 | 分组模型（IsExpanded/IsHovered/ArrowText） |
| App.xaml.cs | 10 | 入口 |
| AssemblyInfo.cs | 9 | 程序集信息 |

**最大文件 445 行，保持在可控范围内。**