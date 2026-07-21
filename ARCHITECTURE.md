# Toolbox 项目架构

> 最后更新: 2026-07-21

## 目录结构

```
Toolbox/
├── Toolbox.sln                             解决方案（4 个项目）
├── Directory.Build.props                   通用 MSBuild 属性
├── Directory.Build.targets                 通用 MSBuild 目标
│
├── App.xaml (+ .cs)                        WPF 应用程序入口：单实例互斥 + 三层全局异常捕获
├── MainWindow.xaml (+ .cs)                 主窗口：Acrylic 毛玻璃 + 自定义标题栏 + 导航 + 设置浮层
├── Toolbox.csproj                          主项目文件（WinExe, net9.0-windows10.0.19041.0）
├── AssemblyInfo.cs                         程序集信息
├── Toolbox.ico                             应用图标
│
├── Helpers/
│   ├── Win32Helper.cs                      Win32 P/Invoke：圆角/深色模式/单实例互斥/Frame扩展/窗口查找 + WndProc 消息钩子（Acrylic 由 MainWindow.xaml.cs 内联实现）
│   ├── SystemTrayHelper.cs                 纯 Win32 系统托盘图标（不依赖 WinForms）
│   ├── CustomScrollBar.cs                  自定义迷你滚动条（深色主题，替代系统 ScrollBar）
│   └── TransitioningContentControl.cs      内容切换淡入动画控件
│
├── Services/
│   └── ToolRegistry.cs                     工具注册中心：三策略插件加载（单文件发布/plugins目录/调试目录）
│
├── Views/
│   └── SettingsView.xaml (+ .cs)           设置页面：5 个 ToggleSwitch（最小化/自启悬浮窗/自启/鼠标光晕/控件边缘发光）+ ComboBox(悬浮窗大小) + 退出按钮
│
├── ViewModels/
│   └── MainViewModel.cs                    主窗口 ViewModel（工具发现、分组、搜索过滤、UI 缓存）
│
├── Toolbox.Core/                           核心抽象层（class library，被主项目 / 插件引用）
│   ├── Toolbox.Core.csproj
│   ├── Models/
│   │   ├── ITool.cs                        工具接口：Name/Description/IconGlyph/Category/CreateContent()
│   │   ├── ToolGroup.cs                    工具分组模型（IsExpanded / IsHovered / ArrowText / HoverIcon）
│   │   └── ToolCategory.cs                 工具分类常量（6 大类）
│   └── Services/
│       └── AppSettings.cs                  单例全局设置（settings.json）：最小化关闭/自启悬浮窗/悬浮窗尺寸/开机自启
│
├── Toolbox.Plugins/                        插件实现层（独立程序集，运行时反射加载）
│   ├── Toolbox.Plugins.csproj
│   │
│   ├── ShutdownTool.cs                     定时关机：6 个快捷按钮 + 自定义分钟 + 取消关机
│   ├── ScreensaverTool.cs                  系统屏保：ComboBox 选择并启动
│   ├── RestartExplorerTool.cs              重启资源管理器：结束并重启 explorer.exe
│   ├── JunkCleanerTool.cs                  C盘垃圾清理：12 类分类扫描，回收站删除，受保护文件跳过
│   ├── SoftwareUninstallTool.cs            软件卸载管理器：注册表扫描 + 双击卸载 + 轮询检测
│   ├── QrCodeTool.cs                       二维码生成：文本/URL 实时生成 + 保存 + 复制
│   ├── QrCodeHelper.cs                     QRCoder 库辅助封装
│   │
│   ├── Tools/                              网易云音乐悬浮窗子模块
│   │   ├── NeteaseMusicTool.cs             工具面板：胶囊开关 + 模式切换 + 毛玻璃/锁定/贴边设置
│   │   ├── Models/
│   │   │   └── NowPlayingInfo.cs           当前播放信息模型
│   │   ├── Services/
│   │   │   ├── SMTCListener.cs             SMTC 监听器：Windows 原生 API 监听媒体，陈旧封面重试（6 次退避）
│   │   │   ├── MusicFloatWindowManager.cs  悬浮窗管理器（单例）：创建/切换透明/毛玻璃窗口，生命周期管理
│   │   │   └── EdgeDockService.cs          贴边自动缩入服务：状态机（Free→Docking→Docked→Expanding→Expanded）
│   │   └── Views/
│   │       ├── AcrylicMusicWindow.xaml     毛玻璃悬浮窗（WindowChrome + DWM Acrylic，新架构）
│   │       └── TransparentMusicWindow.xaml 纯透明悬浮窗（AllowsTransparency=True）
│   │
│   ├── Controls/
│   │   ├── MusicContentControl.xaml        悬浮窗共享内容控件（封面/歌名/大小模式/跑马灯/切歌动画）
│   │   └── DockTriggerBar.xaml             贴边触发条控件（梯形圆角 + 方向箭头）
│   │
│   ├── Services/
│   │   ├── AudioflowSettings.cs            悬浮窗独立设置（audioflow.json）：毛玻璃/锁定/贴边/窗口位置
│   │   └── SoftwareUninstallService.cs     已安装软件扫描 + 卸载执行（注册表 + 图标提取 + UAC 提权）
│   │
│   ├── Models/
│   │   ├── InstalledSoftware.cs            已安装软件数据模型
│   │   └── SortMode.cs                     排序模式枚举 + 扩展方法
│   │
│   └── Helpers/
│       ├── DwmHelper.cs                    DWM 背景效果帮助类（Mica/Acrylic/圆角/深色模式）
│       └── MonitorHelper.cs                多屏工作区查询（MonitorFromWindow + GetMonitorInfo）
│
├── Toolbox.Tests/                          单元测试项目
│   ├── Toolbox.Tests.csproj
│   ├── AppSettingsTests.cs
│   ├── QrCodeToolTests.cs
│   ├── SoftwareUninstallToolTests.cs
│   └── NeteaseMusicToolTests.cs
│
├── setup/
│   ├── ToolboxSetup.iss                    Inno Setup 安装脚本
│   ├── Toolbox_Setup.exe                   打包好的安装程序
│   └── publish/                            单文件发布产物
│
└── docs/
    ├── music-float-window-structure.md          悬浮窗结构说明
    ├── music-float-window-edge-dock-design.md  贴边缩入设计文档
    ├── 按钮亚克力毛玻璃改造方案.md               主窗口 Acrylic + 按钮毛玻璃方案
    └── plans/                                  各类功能设计文档
```

### 行数概览

| 文件 | 行数 | 说明 |
|------|:----:|------|
| App.xaml | 558 | 全局深色主题 + 所有控件样式和模板（含 Button/ToggleButton CornerRadius=6） |
| App.xaml.cs | 118 | 单实例互斥 + 三层全局异常捕获 + crash.log |
| MainWindow.xaml | 486 | 完整的主窗口布局（含 HaloLayer Canvas + EdgeGlowLayer 叠加层） |
| MainWindow.xaml.cs | 672 | DWM/Acrylic 内联实现（含 Win10 降级）+ 半透明背景 + 系统托盘 + 导航高亮动画 + 分组展开折叠 + 鼠标光晕 + 边缘发光集成 |
| Win32Helper.cs | 157 | 圆角/Acrylic/深色模式/消息钩子 P/Invoke |
| | | |
| **主程序 Helpers** | | |
| SystemTrayHelper.cs | 277 | 纯 Win32 系统托盘 |
| CustomScrollBar.cs | 318 | 自定义深色滚动条 |
| TransitioningContentControl.cs | 53 | 淡入过渡控件 |
| ToolRegistry.cs | 109 | 三策略插件加载 |
| | | |
| **Toolbox.Core** | | |
| Helpers/EdgeGlowLayer.cs | 432 | 控件边缘发光引擎（径向渐变描边 + 5点采样遮挡检测 + 视口 PushClip 裁剪 + TextBox/标记卡片收录），主窗口与插件悬浮窗共用 |
| Models/GlowCardMarker.cs | 24 | 卡片发光标记附加属性（IsGlowCard），卡片 Border 显式 opt-in |
| MainViewModel.cs | 171 | 工具分组 + 搜索过滤 + UI 缓存 |
| SettingsView.xaml | 99 | 设置页 UI（5 个 ToggleSwitch + ComboBox 悬浮窗大小 + 退出按钮） |
| SettingsView.xaml.cs | 34 | 设置页后置代码 |
| ITool.cs | 21 | 工具接口（含 Category） |
| ToolCategory.cs | 16 | 6 大分类常量 |
| ToolGroup.cs | 66 | 分组模型 |
| AppSettings.cs | 193 | 全局设置单例（新增：MouseHaloEnabled / ControlGlowEnabled 光晕开关） |
| AudioflowSettings.cs | 173 | 悬浮窗独立设置 |
| | | |
| **插件工具** | | |
| JunkCleanerTool.cs | 1024 | C盘垃圾清理（最大文件） |
| SoftwareUninstallTool.cs | 613 | 软件卸载管理器 |
| MusicContentControl.xaml.cs | 550 | 悬浮窗内容控件 |
| EdgeDockService.cs | 532 | 贴边缩入服务 |
| SMTCListener.cs | 383 | SMTC 监听器 |
| MusicFloatWindowManager.cs | 339 | 悬浮窗管理器 |
| DwmHelper.cs | 296 | DWM 帮助类 |
| NeteaseMusicTool.cs | 285 | 悬浮窗工具面板 |
| SoftwareUninstallService.cs | 284 | 卸载服务 |
| QrCodeTool.cs | 264 | 深色圆角卡片式 + 竖排按钮，实时生成 + 保存 + 复制 |
| ShutdownTool.cs | 241 | 卡片式布局 + 主题色统一 + 快捷按钮重排序 |
| AcrylicMusicWindow.xaml.cs | 146 | 毛玻璃窗口 |
| ScreensaverTool.cs | 186 | 卡片式布局 + 主题色统一 |
| DockTriggerBar.xaml.cs | 123 | 贴边触发条 |
| RestartExplorerTool.cs | 111 | 卡片式布局 + 主题色统一 |
| MonitorHelper.cs | 81 | 多屏辅助 |
| TransparentMusicWindow.xaml.cs | 71 | 透明窗口 |

## 项目间依赖关系

```
Toolbox ──→ Toolbox.Core                （编译期 ProjectReference，需要 ITool 接口）
         ──→ Toolbox.Plugins            （编译期 ProjectReference，单文件发布嵌入）
         ──→ plugins/Toolbox.Plugins.dll（运行时后备加载，通过 Assembly.LoadFrom）

Toolbox.Plugins ──→ Toolbox.Core        （编译期 ProjectReference，实现 ITool）

Toolbox.Tests ──→ Toolbox.Core          （测试 Core 服务）
               ──→ Toolbox.Plugins      （测试插件层服务）
```

**关键设计**：Toolbox.csproj 有 Toolbox.Plugins 的 ProjectReference，但插件 DLL 仍通过 `ToolRegistry` 反射扫描加载，而非直接类型引用。单文件发布时 .NET 宿主将嵌入式程序集提取到 temp 目录注册到默认加载上下文。

## 架构层次说明

### 层 1：Toolbox.Core（接口定义 + 基础服务层）

- 定义 `ITool` 接口，6 个成员：`Name` / `Description` / `IconGlyph` / `Category` / `CreateContent()`
- `Category` 属性（2026-07 新增）将工具按 `ToolCategory` 六大分类分组
- `ToolGroup` 分组模型支持导航栏手风琴式展开/折叠（IsExpanded/IsHovered/ArrowText/HoverIcon/CategoryColor）
- `AppSettings` 单例管理全局用户偏好，JSON 持久化到 `%LOCALAPPDATA%\Toolbox\settings.json`
- 无 UI 依赖，纯抽象，被主项目和插件项目共同引用

### 层 2：Toolbox.Plugins（工具实现层）

- 每个工具一个 `.cs` 文件实现 `ITool`，`CreateContent()` 返回 `UIElement`
- 独立编译为 class library，反射加载
- **增删工具不修改主项目代码**，仅重新编译插件
- 已实现 7 个工具 + 1 个完整悬浮窗子模块
- 子模块（悬浮窗）进一步分层：Views / Controls / Services / Models / Helpers
- `Directory.Build.props` 处理 `wpftmp` 临时编译项目的重复生成问题

### 层 3：Toolbox（主程序层）

- 启动时通过 `App.xaml.cs`：单实例互斥（Mutex）→ 全局异常捕获（3 层）→ 加载 AppSettings + AudioflowSettings
- `MainViewModel` 管理工具列表、分组、搜索过滤、UI 缓存
- 主窗口 UI：Acrylic 毛玻璃背景 → 自定义标题栏 + 左侧手风琴导航 + 右侧 TransitioningContentControl 淡入过渡 + 设置浮层 + Canvas HaloLayer + EdgeGlowLayer
- 关闭按钮支持最小化到系统托盘（纯 Win32，不依赖 WinForms）
- 启动时根据设置自动打开悬浮窗
- **v1.1**（状态栏显示）

#### 半透明背景体系（Acrylic 毛玻璃配套，004db07）

所有面板使用带 alpha 通道的半透明色，让 DWM Acrylic 毛玻璃效果从背景透入，形成统一的毛玻璃视觉。

| 区域 | Background | 不透明度 | 说明 |
|------|-----------|:-------:|------|
| 标题栏 | `Transparent` | 0% | 完全透明，Acrylic 完全透入 |
| 状态栏 | `Transparent` | 0% | 同上 |
| 导航栏 | `#992D2D2D` | ~60% | 半透明暗色表面 |
| 搜索框区域 | `#662D2D2D` | ~40% | 更透明，突出搜索输入框 |
| 搜索输入框 | `#80404040` | ~50% | 提亮背景，保持可读性 |
| 内容区 | `#66323232` | ~40% | 半透明卡片效果 |
| 设置层 | `#4D323232` | ~30% | 最透明，叠加在内容区上方 |
| CornerMask | `BgDarkBrush` | 100% | 四角纯色遮盖（不透，堵 DWM 帧扩展漏白） |

### 层 4：Toolbox.Tests（单元测试层）

- xUnit 测试框架
- 覆盖核心服务（AppSettings）、工具辅助（QRCode）、悬浮窗（NeteaseMusic）、软件卸载（SoftwareUninstall）

## 关键流程

### 启动流程

```
App.xaml → App.xaml.cs OnStartup:
  1. 注册三层异常捕获：
     - DispatcherUnhandledException（UI 线程）
     - AppDomain.UnhandledException（非 UI 线程）
     - UnobservedTaskException（Task 异常）
  2. 创建 Mutex("ToolboxSingleInstanceMutex") 检测单实例
  3. 已有实例 → ActivateExistingInstance() → Shutdown()
  4. AppSettings.Instance.Load()
  5. AudioflowSettings.Instance.Load()

→ MainWindow.xaml (DataContext = MainViewModel):
  Loaded 事件：
    1. WindowInteropHelper 获取 HWND
    2. Win32Helper.EnableRoundedCorners(hwnd)              // 圆角 (DWMWCP_ROUND)
    3. EnableAcrylicBackdrop(hwnd)                         // Acrylic 毛玻璃（内联实现）
       - Win11 22H2+: DWMWA_SYSTEMBACKDROP_TYPE=38, Acrylic=3
       - Win10: ACCENT_ENABLE_ACRYLICBLURBEHIND=4, tint=0x661A1A1A
    4. Win32Helper.EnableDarkMode(hwnd)                    // 沉浸式深色模式
    5. Win32Helper.ExtendFrameIntoClientArea(hwnd)         // 帧扩展到标题栏
    6. HwndSource.AddHook(Win32Helper.WndProc)             // WM_NCCALCSIZE + WM_ERASEBKGND 拦截
    7. HwndTarget.BackgroundColor = Transparent            // 交换链透明
    8. Dispatcher.BeginInvoke: UpdateCornerMask() + InitGroupHeights() + InitHighlight()
    9. 若 AutoOpenFloatWindow → 打开悬浮窗（加载 AudioflowSettings）

  MainWindow 构造函数末尾：
    10. InitHalo() — 初始化鼠标光晕系统 + EdgeGlowLayer 集成（CompositionTarget.Rendering 逐帧轮询）

→ MainViewModel 构造函数:
  1. ToolRegistry.DiscoverTools()
     → TryLoadFromDefaultContext()    // 单文件发布：Assembly.Load("Toolbox.Plugins")
     → TryLoadFromPluginsDir()        // plugins/Toolbox.Plugins.dll
     → TryLoadFromBaseDir()           // 调试目录直接加载
     → 反射扫描 ITool 实现 → 实例化 → 按 Category 分组
  2. BuildGroups()：按 ToolCategory.All 顺序 + "系统维护"默认展开
  3. ApplyFilter()：初始化可见分组
  4. 默认选中第一个工具
```

> **步骤 3-4 为 Acrylic 毛玻璃链**：步骤 6-7 + 步骤 5 共同构成三层 Win32/DirectX 拦截链，消除特定机器上的窗口边缘白色线条残留。

### 设置流程

```
点击标题栏齿轮按钮 → ContentScrollViewer 隐藏 → SettingsLayer 显示
                   → SettingsView 加载，绑定 AppSettings.Instance
                   → BackButton → BackRequestedEvent → MainWindow 隐藏设置层
                   → 恢复高亮条位置
```

### 切换工具流程

```
点击导航项 (Border) → NavItem_MouseLeftButtonDown
                    → MainViewModel.SelectedTool = tool
                    → PropertyChanged → CurrentContent 重新创建（缓存复用）
                    → TransitioningContentControl 淡入
                    → PositionHighlight() 高亮条动画移动
```

### 分组展开/折叠流程

```
点击分类头 → GroupHeader_MouseLeftButtonDown
           → ToolGroup.IsExpanded 切换
           → AnimateGroupHeight()：Height 动画 200ms EaseOut
           → 折叠时自动切换到下一个可见工具
           → 动画完成后 ScheduleHighlightReposition() 重定位高亮
```

### 悬浮窗完整架构

```
MusicFloatWindowManager (单例)
├── SMTCListener             监听 Windows SMTC 会话
│   ├── SemaphoreSlim 串行化   消除并发竞态
│   ├── 陈旧封面检测           切歌后 SMTC 可能返回旧封面
│   ├── 6 次退避重试            200/200/400/800/1500/3000ms
│   ├── Dispose 释放 SemaphoreSlim（004db07 修复）
│   └── 歌曲身份绑定的重试取消  单纯进度/状态事件不打断封面重试
│
├── EdgeDockService          贴边缩入状态机
│   ├── Free → Docking → Docked → Expanding → Expanded
│   ├── DispatcherTimer 驱动动画（EaseOutCubic）
│   ├── DockTriggerBar 触发条悬停展开
│   ├── SetContentVisible: Opacity + IsHitTestVisible 联动（004db07）
│   └── EdgeThreshold: 透明窗口 -5px / 毛玻璃窗口 10px
│
├── AcrylicMusicWindow       DWM Acrylic 毛玻璃（Win11 22H2+ 原生 API / Win10 备用旧 API）
│   └── MusicContentControl   共享内容控件（封面/歌名/跑马灯/切歌动画）
│
├── TransparentMusicWindow   AllowsTransparency=True，无背景效果
│   └── MusicContentControl   共享内容控件（同上）
│
└── 操作：Show / Hide / Close / ToggleBlur / SetSizeMode / SetWindowLocked
         窗口创建即实例化，切换背景类型或尺寸时替换窗口（非原地切换）
```

**窗口替换模式**：切换毛玻璃/透明或大小模式时，保存位置 → 创建新窗口 → 显示新窗口 → 关闭旧窗口，避免 DWM 渲染问题。

**后台闪退全链路防护**（004db07）：
- `DragMove` / `UpdateSongInfo` / `OnMarqueeTick` 全部包裹 try-catch
- `MusicContentControl.Unloaded` 停止跑马灯定时器，防止泄漏
- `SMTCListener` 的 `NowPlayingChanged` 事件在后台线程触发 → `Dispatcher.BeginInvoke` 派发到 UI 线程
- `AppSettings.Save()` / `AudioflowSettings.Save()` 加 try-catch 兜底

## 双层鼠标光晕系统（b3b7122）

### 层 1 — HaloLayer（鼠标跟随呼吸光晕）

**位置**：`MainWindow.xaml` → `<Canvas x:Name="HaloLayer">` + `MainWindow.xaml.cs` → `InitHalo()`

| 组件 | 细节 |
|------|------|
| **渲染元素** | `Ellipse` 140×140px，`RadialGradientBrush` 10 个色标等差衰减（中心 `#40FFFFFF` → 边缘 `#00FFFFFF`） |
| **呼吸动画** | XAML `EventTrigger.Loaded` → `Storyboard` 循环驱动 `ScaleTransform` 0.9↔1.1，`SineEase` 1.5s |
| **位置跟随** | `GetCursorPos`（Win32）逐帧读取 → `PointFromScreen` 转换 → `lerp(0.12)` 插值滞后 |
| **淡入淡出** | `_haloOpacity` 系数 0.08 平滑过渡，移出窗口缓出 |
| **数据源选择** | 用 `GetCursorPos` 而非 `Mouse.GetPosition`——后者在 `HTCAPTION` 非客户区不更新 |

### 层 2 — EdgeGlowLayer（控件边缘发光叠加层）

**位置**：`Toolbox.Core/Helpers/EdgeGlowLayer.cs`，`FrameworkElement` 子类，在 `MainWindow.xaml` 最顶层及插件悬浮窗 `MusicContentControl` 内渲染。

**基本参数**：
- `GlowRadius = 120px`（发光影响范围）
- `MaxAlpha = 1.0`（hover 峰值不透明度，完全过曝）
- `StrokeThickness = 2px`（硬切高光线）
- `MaxLitRadius = 100px`（照亮半径上限，亮弧大小贴合鼠标光晕）
- 强度公式：`t = 1 - d/120` → `alpha = t² × 1.0`；沿边框亮度 = `alpha × (1-offset)^0.6 × 1.3`（径向渐变画笔，中心即光标，只照亮朝向鼠标的弧段）

**核心机制**：

1. **控件识别 → 模板边界提取**：`ButtonBase` / `ComboBox` / `TextBox` 可发光；卡片容器（`Border`）可显式通过附加属性 `GlowCardMarker.IsGlowCard` opt-in。递归视觉树查找模板内首个 `Border` 的 `CornerRadius`，描边逐像素贴合控件玻璃边缘。

2. **径向渐变描边**：描边用 `RadialGradientBrush`（`MappingMode=Absolute`，中心=光标位置）。10 段色标，`alpha × (1-offset)^0.6 × 1.3`，近光心一端形成过曝平台，背光侧完全熄灭——模拟灯光扫过物体。`MaxLitRadius=100px`，大卡片照亮弧段不会超过此范围。

3. **遮挡检测**：5 点采样（中心 + 四角 20% 内缩）命中测试。仅在所有采样点都被**非控件元素**覆盖时判定遮挡，支持弹窗/设置层遮罩。`HitTestAt` 使用 `HitTestFilterCallback` 跳过 `IsHitTestVisible=false` 的元素（避免 EdgeGlowLayer 自身拦截命中）。

4. **视口 PushClip 裁剪**：绘制卡片完整边缘后，`PushClip` 到滚动视口相交区域再裁掉不可见部分——不把视口边缘误当成控件边缘（修复长卡片滚出视口时底部假亮边）。

5. **目标清单管理**：只存元素引用不存坐标——每帧实时重算。`LayoutUpdated` → `_glowTargetsDirty` → 250ms 节流重建。工具切换/设置层显隐 → `ClearTargets()` 0ms 清除。

**配套样式变更**（App.xaml）：
- Button / ToggleButton 模板 Border → `CornerRadius="6"`
- ComboBoxItem 模板 → `CornerRadius="4"` + `Margin="2,1"`（圆角与弹出层适配）

### 设置开关

`AppSettings` 新增两个 `bool` 属性（默认 `true`），在 `SettingsView.xaml` 中以 ToggleSwitch 呈现：
- `MouseHaloEnabled`：鼠标跟随光晕开关
- `ControlGlowEnabled`：控件边缘发光开关

### 主窗口光晕初始化流程

```csharp
MainWindow 构造函数末尾:
  InitHalo()

InitHalo():
  1. GlowLayer.LayoutUpdated → _glowTargetsDirty = true
  2. MainViewModel.PropertyChanged(SelectedTool) → RequestGlowRebuild()
  3. SettingsLayer.IsVisibleChanged → RequestGlowRebuild()
  4. CompositionTarget.Rendering 逐帧轮询:
     a. GetCursorPos() 读取光标
     b. HaloLayer 位置插值 + 淡入淡出
     c. GlowLayer 目标重建（250ms 节流）
     d. GlowLayer.UpdateCursor(pt, inside)
```

### 发布流程

```
dotnet publish Toolbox.csproj -c Release -r win-x64 --self-contained true ^
  -o setup/publish -p:DebugType=none

→ ISCC.exe setup/ToolboxSetup.iss (LZMA2 最高压缩)
→ setup/Toolbox_Setup.exe (~54 MB)
```

## 全局主题资源（App.xaml 定义）

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
| `ToggleSwitchStyle` | Win11 极简滑块开关（42x22 轨道 + 18x18 滑块 + 0.2s 滑动动画） |
| `ClassicCheckBoxStyle` | 方框+对勾传统复选框（备用） |
| `CustomScrollBar` | 迷你深色滚动条（宽 8px） |

## 当前工具列表

| 工具 | 文件名 | 分类 | 行数 | 功能 |
|------|--------|------|:----:|------|
| 定时关机 | `ShutdownTool.cs` | ⚙️ 系统维护 | 241 | 卡片式布局 + 快捷按钮重排序 + 自定义分钟 + 取消关机 |
| 屏保启动 | `ScreensaverTool.cs` | ⚙️ 系统维护 | 186 | ComboBox 选择 5 种系统屏保 + 卡片式布局 |
| 重启资源管理器 | `RestartExplorerTool.cs` | ⚙️ 系统维护 | 111 | taskkill + explorer 重启 + 卡片式布局 |
| C盘垃圾清理 | `JunkCleanerTool.cs` | ⚙️ 系统维护 | 1024 | 12 类分类扫描 + 自定义确认弹窗 + 取消按钮 + 间距微调 |
| 二维码生成 | `QrCodeTool.cs` | 🌐 网络与开发 | 264 | 深色圆角卡片式 + 竖排按钮，实时生成 + 保存 + 复制 |
| 软件卸载管理器 | `SoftwareUninstallTool.cs` | 📁 文件管理 | 613 | 注册表扫描 + 图标提取 + 双击卸载 |
| 网易云音乐悬浮窗 | `NeteaseMusicTool.cs` | 🎵 媒体与娱乐 | 285 | 胶囊开关 + 模式切换 + 悬浮窗设置面板 |

## ITool 接口规范

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

`Category` 属性使用预定义常量：
```csharp
public static class ToolCategory
{
    public const string System  = "⚙️ 系统维护";
    public const string Network = "🌐 网络与开发";
    public const string Window  = "🏠 窗口与桌面";
    public const string Text    = "🔤 文本与数据";
    public const string File    = "📁 文件管理";
    public const string Media   = "🎵 媒体与娱乐";
}
```

## AppSettings 配置项

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MinimizeOnClose` | `bool` | `false` | 关闭按钮最小化到任务栏 |
| `AutoOpenFloatWindow` | `bool` | `false` | 启动时自动打开悬浮窗 |
| `MusicFloatSizeMode` | `string` | `"Large"` | 悬浮窗默认大小（Large / Compact） |
| `AutoStart` | `bool` | `false` | 开机自动启动（同步 HKCU\...\Run\Toolbox） |
| `MouseHaloEnabled` | `bool` | `true` | 鼠标跟随光晕开关 |
| `ControlGlowEnabled` | `bool` | `true` | 控件边缘发光（鼠标照亮效果）开关 |

持久化：`%LOCALAPPDATA%\Toolbox\settings.json`，JSON 格式，所有属性变更触发 `PropertyChanged` + `Save()`。

## AudioflowSettings 配置项

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FloatWindowBlurEnabled` | `bool` | `true` | 悬浮窗 Acrylic 毛玻璃开关 |
| `LockFloatWindow` | `bool` | `false` | 锁定悬浮窗移动 |
| `EdgeDockEnabled` | `bool` | `true` | 贴边自动缩入功能 |
| `FloatWindowLeft` | `double` | `NaN` | 窗口 X 坐标（NaN=默认位置） |
| `FloatWindowTop` | `double` | `NaN` | 窗口 Y 坐标（NaN=默认位置） |

持久化：`%LOCALAPPDATA%\Toolbox\audioflow.json`，与 AppSettings 解耦独立保存加载。

## 主窗口 Acrylic 毛玻璃实现

### Acrylic 背景（替代 Mica，004db07）

`MainWindow.xaml.cs` 内联实现（非 Win32Helper），支持两套 API：

- **Win11 22H2+**（Build ≥ 22000）：`DWMWA_SYSTEMBACKDROP_TYPE = 38`，`Acrylic = 3`，原生 DWM API
- **Win10 降级**：`ACCENT_ENABLE_ACRYLICBLURBEHIND = 4`，`GradientColor = 0x661A1A1A`（40% tint），通过 `DwmSetWindowAttribute(hwnd, 19, ...)` 设置

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

#### 根因

`AllowsTransparency="False"` + `WindowStyle="None"` + `DwmExtendFrameIntoClientArea(-1)` 组合下，三个独立白色来源在窗口外缘叠加。

#### 解决

| 防御层 | 层面 | 方法 |
|--------|------|------|
| 1. `WM_NCCALCSIZE` 拦截 | Win32 消息 | 返回 0，宣告无 NC 区域 |
| 2. `WM_ERASEBKGND` 拦截 | Win32 消息 | 返回 1，跳过系统白色底漆 |
| 3. `HwndTarget.BackgroundColor = Transparent` | DirectX 交换链 | 禁止渲染目标白色清除 |

实现位置：`Win32Helper.WndProc()` + `MainWindow.xaml.cs` Loaded 事件。

## 扩展新工具模板

在 `Toolbox.Plugins/` 下新建 `{ToolName}.cs`，选择合适分类：

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
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 20)
        });
        return panel;
    }
}
```

编译后放入 `plugins/`，重启即可自动发现。
