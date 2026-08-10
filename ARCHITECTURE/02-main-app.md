# 02 · 主程序层（Toolbox）

> 主程序（WinExe, net9.0-windows10.0.19041.0）：WPF UI + MVVM + 纯 Win32 系统托盘。

## 文件清单

| 文件 | 职责 |
|------|------|
| App.xaml | 全局深色主题 + 所有控件样式和模板（含 Button/ToggleButton CornerRadius=6） |
| App.xaml.cs | 单实例互斥 + 三层全局异常捕获 + crash.log（2MB 轮转存档） |
| MainWindow.xaml | 完整的主窗口布局（含 HaloLayer Canvas + EdgeGlowLayer 叠加层） |
| MainWindow.xaml.cs | Acrylic 背景（经 Core 的 DwmHelper 实现，Win10 降级）+ 半透明背景 + 系统托盘 + 导航高亮动画 + 分组展开折叠 + 设置层进出过渡（淡入上滑/渐隐，设置页点工具时与切换退场**串行对齐**：退场期间下层折叠、`ExitCompleted` 后 150ms 快速退场）+ 工具切换内容区回顶 + 鼠标光晕 + 边缘发光集成 |
| AssemblyInfo.cs | 程序集信息 |
| Toolbox.ico | 应用图标 |

## Helpers/

| 文件 | 职责 |
|------|------|
| Win32Helper.cs | Win32 业务封装（圆角/深色模式/帧扩展/窗口查找 + WndProc 消息钩子；P/Invoke 声明统一在 Core 的 Win32Native） |
| SystemTrayHelper.cs | 纯 Win32 系统托盘图标（不依赖 WinForms） |
| CustomScrollBar.cs | 自定义迷你滚动条（深色主题，替代系统 ScrollBar） |
| TransitioningContentControl.cs | 内容切换过渡控件（旧内容 200ms 淡出 → 新内容 400ms 淡入+滑入，退场期间回写旧内容实现真正先后交接；`SlideFromY` 控制滑入方向：默认 8 上滑，工具标题区用 -8 下滑形成对向关系；`_pendingContent` 退场中再切换以最新为准；暴露 `IsExiting`/`ExitCompleted` 供设置层退出串行对齐） |
| TextBoxContextMenuHelper.cs | 统一深色主题 TextBox 右键菜单 |

> ⚠️ 主程序专用：`Win32Helper` / `CustomScrollBar` / `TransitioningContentControl` 位于主程序，插件层仅引用 Core，无法访问（避免循环依赖）。

## Services/

| 文件 | 职责 |
|------|------|
| ToolRegistry.cs | 工具注册中心：单策略插件加载（Assembly.Load 默认加载上下文）+ 反射注册悬浮窗控制器 |

## Views / ViewModels

| 文件 | 职责 |
|------|------|
| SettingsView.xaml | 设置页 UI（5 个 ToggleSwitch + ComboBox 悬浮窗大小 + OCR 引擎卡片 + 退出按钮） |
| SettingsView.xaml.cs | 设置页后置代码（OCR 引擎状态检测/确认删除，每次进入设置页刷新） |
| MainViewModel.cs | 工具分组 + 搜索过滤 + UI 缓存 + Tools 只读列表暴露 |

## 半透明背景体系（Acrylic 配套）

所有面板使用带 alpha 通道的半透明色，让 DWM Acrylic 毛玻璃效果从背景透入，形成统一毛玻璃视觉。

| 区域 | Background | 不透明度 | 说明 |
|------|-----------|:-------:|------|
| 标题栏 | `Transparent` | 0% | 完全透明，Acrylic 完全透入 |
| 状态栏 | `Transparent` | 0% | 同上 |
| 导航栏 | `#992D2D2D` | ~60% | 半透明暗色表面 |
| 搜索框区域 | `#662D2D2D` | ~40% | 更透明，突出搜索输入框 |
| 搜索输入框 | `#80404040` | ~50% | 提亮背景，保持可读性 |
| 内容区 | `#66323232` | ~40% | 半透明卡片效果 |
| 设置层 | `#99323232` | ~60% | 模态浮层最暗档，压住下层保证可读性 |
| CornerMask | `BgDarkBrush` | 100% | 四角纯色遮盖（不透，堵 DWM 帧扩展漏白） |

## 主窗口 Acrylic 毛玻璃实现

`MainWindow.xaml.cs` 的 `EnableAcrylicBackdrop()` 经 `Toolbox.Core.Helpers.DwmHelper` 实现，支持两套 API：

- **Win11 22H2+**（Build ≥ 22000）：`DwmHelper.SetBackdrop(this, BackdropType.Acrylic)`，`DWMWA_SYSTEMBACKDROP_TYPE = 38`，`Acrylic = 3`，原生 DWM API
- **Win10 降级**：`DwmHelper.EnableAcrylicBlur(this, 0x661A1A1A)`，`SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND = 4`，`GradientColor = 0x661A1A1A`（40% tint）

### CornerMask 四角遮盖（全窗口内矩形方案）
`WindowChrome.CornerRadius = 8` + `Border CornerRadius = 8` + `Margin = "-1"` 实现圆角窗口。DWM 帧扩展导致圆角外区域漏白，用 Path 几何遮盖：

```
Path.Data = CombinedGeometry(
  Exclude,
  RectangleGeometry(0, 0, ActualWidth, ActualHeight),          // 外矩形（尖角）
  RectangleGeometry(0, 0, ActualWidth, ActualHeight, r, r)     // 内矩形（全窗口圆角）
)
```

关键点：内矩形使用 `(0, 0, FullWidth, FullHeight)` 而非 `(r, r, Width-2r, Height-2r)`，差集结果 = **仅四角**，不含四边，消除四条边黑框。

### UI 白色线条修复方案（三层 Win32/DirectX 拦截）
`AllowsTransparency="False"` + `WindowStyle="None"` + `DwmExtendFrameIntoClientArea(-1)` 组合下，三个独立白色来源在窗口外缘叠加：

| 防御层 | 层面 | 方法 |
|--------|------|------|
| 1. `WM_NCCALCSIZE` 拦截 | Win32 消息 | 返回 0，宣告无 NC 区域 |
| 2. `WM_ERASEBKGND` 拦截 | Win32 消息 | 返回 1，跳过系统白色底漆 |
| 3. `HwndTarget.BackgroundColor = Transparent` | DirectX 交换链 | 禁止渲染目标白色清除 |

实现位置：`Win32Helper.WndProc()` + `MainWindow.xaml.cs` Loaded 事件。

## 发布流程

```
dotnet publish Toolbox.csproj -c Release -r win-x64 --self-contained true ^
  -o setup/publish -p:DebugType=none

→ ISCC.exe setup/ToolboxSetup.iss (LZMA2 最高压缩)
→ setup/Toolbox_Setup.exe
```

## 相关文档

- 启动/设置/切换流程 → [06-flows.md](06-flows.md)
- 光晕/主题系统 → [07-ui-system.md](07-ui-system.md)
- Core 层 → [03-core.md](03-core.md)
