# 02 · 主程序层（Toolbox）

> 主程序（WinExe, net9.0-windows10.0.19041.0）：WPF UI + MVVM + 纯 Win32 系统托盘。

## 文件清单

| 文件 | 职责 |
|------|------|
| App.xaml | 全局深色主题 + 所有控件样式和模板（含 Button/ToggleButton CornerRadius=6、ToolTip 深色样式 + 150ms 淡入） |
| App.xaml.cs | 单实例互斥 + 三层全局异常捕获 + crash.log（2MB 轮转存档） |
| MainWindow.xaml | 完整的主窗口布局（含 HaloLayer Canvas + EdgeGlowLayer 叠加层） |
| MainWindow.xaml.cs | Acrylic 背景（经 Core 的 DwmHelper 实现，Win10 降级）+ 半透明背景 + 系统托盘 + 导航高亮动画 + 分组展开折叠（展开时子项 25ms 间隔错落淡入）+ 侧栏按压反馈（导航项/分组头 0.96 按压缩放 + 回弹，`EnsureMutableScale` 防冻结实例）+ 设置层进出过渡（淡入上滑 + 0.96↔1 缩放，设置页点工具时与切换退场**串行对齐**：退场期间下层折叠、`ExitCompleted` 后 150ms 快速退场）+ 工具切换内容区回顶 + 鼠标光晕 + 边缘发光集成（光晕与 RenderTransform 动画同步：`IsAnyGlowTrackedAnimationActive` 闸门检测 + `ClearClockOnCompleted` 完成清时钟，见 07-ui-system「重绘触发与动画同步」）+ 切回前台动画（`_wasBackground` 三处置位 + `Activated` 触发；`MinimizePreClearHook` SC_MINIMIZE 清场帧拦截消除还原首帧闪烁，见 07-ui-system） |
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
| AppPaths.cs | 应用级路径/命名常量（Toolbox.Core/Services）：`#if DEBUG` 编译期常量统一承载全部隔离点——数据目录名、单实例互斥名、唤起事件名、远程默认端口。Debug 构建与 Release 完全隔离（目录/互斥/事件/端口四重隔离），开发调试版可与正式安装版同时运行互不干扰；Release 常量与原硬编码值逐一一致（行为不变）。全项目共 4 个消费文件（App.xaml.cs / AppSettings / AudioflowSettings / RemoteControlSettings）+ 1 处自动启动端口兜底（RemoteControlTool） |

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

## 发布流程（完整执行顺序）

1. **版本号更新**：`setup/ToolboxSetup.iss`（SetupVersion）+ `MainWindow.xaml` 状态栏版本文本
2. **commit**：一次性提交全部改动（不拆主题）；push 在步骤 6 统一执行
3. **清理构建缓存**：各项目 `obj/` + `bin/Release` + `setup/publish`（**保留 `bin/Debug`**——调试产物常用）
4. **publish**（见下方命令）→ 5. **ISCC 打包**（`setup/ToolboxSetup.iss`，LZMA2 最高压缩）→ 产物 `setup/Toolbox_Setup.exe`
6. **push**：`git push origin master`（代理已配为 git 全局 http.proxy/https.proxy；凭据在 Windows 凭据管理器；push 失败先查 Flclash 代理是否启动）
7. **changelog**：`docs/changelog-YYYY-MM-DD.md` 补发布条目

```
dotnet publish Toolbox.csproj -c Release -r win-x64 --self-contained true ^
  -o setup/publish -p:DebugType=none

→ ISCC.exe setup/ToolboxSetup.iss (LZMA2 最高压缩)
→ setup/Toolbox_Setup.exe
```

> ⚠️ **步骤 4 必须使用 `-c Release`，是硬性要求**：AppPaths 按 `#if DEBUG` 编译期隔离（数据目录/互斥名/唤起事件/端口/注册表值名五重），误用 Debug 配置会产出"假正式版"——Toolbox-Debug 数据目录、8091 端口、独立互斥名，与正式版并存不互斥，且远程控制连不上（端口错位）。若 Debug/Release 行为出现意外差异，先查本文件"Services/AppPaths.cs"条目核对隔离清单。

## 相关文档

- 启动/设置/切换流程 → [06-flows.md](06-flows.md)
- 光晕/主题系统 → [07-ui-system.md](07-ui-system.md)
- Core 层 → [03-core.md](03-core.md)
