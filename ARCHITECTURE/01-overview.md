# 01 · 项目概览

> 基于 .NET 9 的 Windows 桌面工具箱，三层插件式架构。
> 目标平台：Windows 10 19041+。许可：MIT。

## 项目定位

- **插件式架构**：核心抽象层（Toolbox.Core）+ 工具实现层（Toolbox.Plugins）+ WPF 界面层，新增工具无需改动主程序
- **毛玻璃界面**：Acrylic / Mica / Aero 三种背景模式，深浅色主题，自定义标题栏与滚动条
- **音乐悬浮窗**：贴边自动缩入的桌面悬浮窗，支持封面显示与多档背景效果
- **纯 Win32 系统托盘**：不依赖 WinForms

## 项目间依赖关系

```
Toolbox ──→ Toolbox.Core                （编译期 ProjectReference，需要 ITool 接口 + Core 基础设施）
         ──→ Toolbox.Plugins            （编译期 ProjectReference，单文件发布嵌入；运行时经 ToolRegistry 反射加载）

Toolbox.Plugins ──→ Toolbox.Core        （编译期 ProjectReference，实现 ITool / IMusicFloatController + 共用 EdgeGlowLayer/ThemeColors/DwmHelper/Win32Native）

Toolbox.Tests ──→ Toolbox.Core          （测试 Core 服务）
               ──→ Toolbox.Plugins      （测试插件层服务）
```

## 关键设计

### 插件加载（单一策略）
`Toolbox.csproj` 有 `Toolbox.Plugins` 的 ProjectReference，但插件 DLL 仍通过 `ToolRegistry` 反射扫描加载，而非直接类型引用。单文件发布时 .NET 宿主将嵌入式程序集提取到 temp 目录注册到默认加载上下文。

加载策略唯一：`Assembly.Load` 经默认加载上下文加载——ProjectReference + 编译期静态绑定保证插件 DLL 必在主输出目录且登记于 deps.json，故无需（也已删除）plugins/ 目录与基目录两条 `LoadFrom` 回退路径。

插件加载成功后 `ToolRegistry` 反射获取 `MusicFloatWindowManager` 并注册至 `MusicFloatControllerHost`，主程序经 `Current` 控制悬浮窗，不直接引用插件类型。

### Win32 P/Invoke 收拢
全部 Win32 P/Invoke 声明收拢于 `Toolbox.Core/Helpers/Win32Native.cs`（唯一声明处），Win32Helper/DwmHelper/ClickThroughHelper/SystemTrayHelper/MainWindow 中的重复声明已清理。

### 2026-07 架构变化摘要
- `EdgeGlowLayer` 从 MainWindow 内联实现迁移至 Core，供主窗口与插件悬浮窗共用
- 新增 `ThemedMenuWindow`、`ThemeColors`、`JsonSettingsFile` 等基础设施至 Core 层
- `DwmHelper` 自插件层整体迁入 Core（含 BackdropType / CornerPreference 枚举），主窗口 Acrylic 背景改经 Core 的 DwmHelper 实现
- 新增悬浮窗控制器抽象（`IMusicFloatController` + `MusicFloatControllerHost`，主程序不再直接引用插件类型，`FloatSizeMode` 枚举移入 Core）
- 新增截图识字 OCR 工具（OcrTool + OcrHelper/PaddleOcrWrapper/EngineDownloader/ImageFileHelper + DownloadDialog）
- 插件层命名空间统一为 `Toolbox.Plugins.*`（Helpers/Services/Models/Controls），工具类保留 `Toolbox.Tools.*`

### 鲁棒性加固（2026-07）
- `JsonSettingsFile` 原子写入（.tmp→替换）+ `.bak` 备份回落，设置不因断电写半截丢光
- crash.log 2MB 轮转
- `SMTCListener` 启动退避重试（5/15/30s）、30s 看门狗自愈、休眠唤醒重建
- 测试套件 80/80 全绿 baseline

## 目录结构速览

```
Toolbox/
├── App.xaml (+ .cs)                    WPF 入口：单实例互斥 + 三层全局异常捕获
├── MainWindow.xaml (+ .cs)             主窗口：Acrylic + 自定义标题栏 + 导航 + 设置浮层
├── Helpers/                            主程序 Helper（Win32Helper/SystemTrayHelper/...）
├── Services/                           ToolRegistry 工具注册中心
├── Views/                              SettingsView 设置页
├── ViewModels/                         MainViewModel
├── Toolbox.Core/                       核心抽象层（详见 03-core.md）
├── Toolbox.Plugins/                    工具实现层（详见 04-plugins.md）
├── Toolbox.Tests/                      单元测试（详见 05-tests.md）
└── setup/                              Inno Setup 安装脚本 + publish 产物
```

## 相关文档

- 主程序细节 → [02-main-app.md](02-main-app.md)
- Core 层细节 → [03-core.md](03-core.md)
- 插件/工具 → [04-plugins.md](04-plugins.md)
- 流程 → [06-flows.md](06-flows.md)
- UI 系统 → [07-ui-system.md](07-ui-system.md)
