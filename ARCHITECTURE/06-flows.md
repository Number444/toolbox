# 06 · 关键流程

## 启动流程

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
    3. EnableAcrylicBackdrop()                             // Acrylic 毛玻璃（经 Core 的 DwmHelper 封装）
       - Win11 22H2+: DWMWA_SYSTEMBACKDROP_TYPE=38, Acrylic=3
       - Win10: ACCENT_ENABLE_ACRYLICBLURBEHIND=4, tint=0x661A1A1A
    4. Win32Helper.EnableDarkMode(hwnd)                    // 沉浸式深色模式
    5. Win32Helper.ExtendFrameIntoClientArea(hwnd)         // 帧扩展到标题栏
    6. HwndSource.AddHook(Win32Helper.WndProc)             // WM_NCCALCSIZE + WM_ERASEBKGND 拦截
    7. HwndTarget.BackgroundColor = Transparent            // 交换链透明
    8. Dispatcher.BeginInvoke: UpdateCornerMask() + InitGroupHeights() + InitHighlight()
    9. 若 AutoOpenFloatWindow → 经 MusicFloatControllerHost.Current 打开悬浮窗（控制器未注册则跳过）
    10. 启动遮罩入场链（v1.8.2：两段式品牌动画 + 交叉淡化对焦 + 淡出×内容入场编排，
        时序骨架 3.3s 盖住 WPF 黑屏期，见 07-ui-system「启动遮罩」）

  MainWindow 构造函数末尾：
    10. InitHalo() — 初始化鼠标光晕系统 + EdgeGlowLayer（CompositionTarget.Rendering 逐帧轮询）

→ MainViewModel 构造函数:
  1. ToolRegistry.DiscoverTools()
     → TryLoadFromDefaultContext()    // 唯一策略：Assembly.Load("Toolbox.Plugins")，默认加载上下文
     → 反射获取 MusicFloatWindowManager → MusicFloatControllerHost.Register（悬浮窗控制器接线）
     → 反射扫描 ITool 实现 → 实例化 → 按 Category 分组
  2. BuildGroups()：按 ToolCategory.All 顺序 + "系统维护"默认展开
  3. ApplyFilter()：初始化可见分组
  4. 默认选中第一个工具
```

> 步骤 3-4 为 Acrylic 毛玻璃链：步骤 6-7 + 步骤 5 共同构成三层 Win32/DirectX 拦截链，消除特定机器上的窗口边缘白色线条残留。

## 设置流程

```
点击标题栏齿轮按钮 → EnterSettingsView：ContentScrollViewer 折叠 → SettingsLayer 显示
                   → 淡入 + 8px 上滑 360ms EaseOut（_settingsAnimToken 防连点）
                   → SettingsView 加载，绑定 AppSettings.Instance
                   → OCR 引擎状态每次进入设置页刷新（IsVisibleChanged，非启动时一次性检测）

退出（Back 返回）→ ExitSettingsView：
                   → 下层内容区立即可见 + 设置层 150ms 淡出下滑 → 完成后折叠复位

退出（设置页内点击工具）→ 串行对齐：
                   → 工具切换退场（200ms）期间下层保持折叠（退场在折叠容器内不可见）
                   → TransitioningContentControl.ExitCompleted 后显示下层 + 设置层 150ms 快速退场
                   → 露出正在淡入的新内容 → 恢复高亮条位置
```

## 切换工具流程

```
点击导航项 (Border) → NavItem_MouseLeftButtonDown
                    → MainViewModel.SelectedTool = tool
                    → 内容区滚动回顶（SelectedTool PropertyChanged 订阅，Collapsed 下设置偏移安全）
                    → PropertyChanged → CurrentContent 重新创建（缓存复用）
                    → TransitioningContentControl 两段式：旧内容 200ms 淡出 → 新内容 400ms 淡入+滑入
                      （标题区同款对向动画：-8 下滑；退场期间回写旧内容，_pendingContent 以最新为准）
                    → PositionHighlight() 高亮条动画移动
                    → 若设置页打开：设置层退场串行对齐（见设置流程）
```

## 分组展开/折叠流程

```
点击分类头 → GroupHeader_MouseLeftButtonDown
           → ToolGroup.IsExpanded 切换
           → AnimateGroupHeight()：Height 动画 200ms EaseOut
           → 折叠时自动切换到下一个可见工具
           → 动画完成后 ScheduleHighlightReposition() 重定位高亮
```

## 悬浮窗完整架构

```
MusicFloatWindowManager (单例)
├── SMTCListener             监听 Windows SMTC 会话
│   ├── SemaphoreSlim 串行化   消除并发竞态
│   ├── 陈旧封面检测           切歌后 SMTC 可能返回旧封面
│   ├── 6 次退避重试            200/200/400/800/1500/3000ms
│   ├── Dispose 释放 SemaphoreSlim
│   └── 歌曲身份绑定的重试取消  单纯进度/状态事件不打断封面重试
│
├── EdgeDockService          贴边缩入状态机
│   ├── Free → Docking → Docked → Expanding → Expanded
│   ├── DispatcherTimer 驱动动画（EaseOutCubic）
│   ├── DockTriggerBar 触发条悬停展开
│   ├── SetContentVisible: Opacity + IsHitTestVisible 联动
│   └── EdgeThreshold: 透明窗口 -5px / 毛玻璃窗口 10px
│
├── MusicContentControl      共享内容控件
│   ├── 封面/歌名/大小模式/跑马灯/切歌动画
│   ├── 悬停播放控制按钮（鼠标悬停封面浮出三个按钮：上一首/播放暂停/下一首）
│   │   ├── 150ms 淡入淡出 + 8px 纵向滑动动画
│   │   ├── 按钮点击缩放反馈（0.82→1.0, 180ms EaseOut）
│   │   └── 紧凑模式下整个窗口为触发范围
│   ├── 游戏模式隐藏悬停按钮
│   └── 紧凑模式标题改为跑马灯
│
├── AcrylicMusicWindow       DWM Acrylic 毛玻璃（Win11 22H2+ 原生 API / Win10 备用旧 API）
│   ├── MusicContentControl   共享内容控件
│   ├── 右键菜单：锁定/毛玻璃/大小/游戏模式/复位
│   └── 游戏模式点击穿透：仅 WS_EX_NOACTIVATE + WM_NCHITTEST 拦截
│
├── TransparentMusicWindow   AllowsTransparency=True，无背景效果
│   ├── MusicContentControl   共享内容控件
│   ├── 右键菜单：同上
│   └── 游戏模式点击穿透：WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
│
├── ClickThroughHelper       游戏模式点击穿透共享实现
│   ├── Transparent 窗口：WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
│   ├── Acrylic 窗口：仅 WS_EX_NOACTIVATE + WM_NCHITTEST 拦截
│   └── 开启后鼠标完全穿透悬浮窗，不会切出全屏游戏焦点
│
├── ThemedMenuWindow         深色主题右键菜单（悬浮窗 + TextBox 共用）
│   ├── 深色圆角主题，DropShadowEffect 投影
│   ├── 自动屏幕边界吸附
│   └── 点击外部自动关闭
│
├── TaskbarMusicWindow       任务栏嵌入宿主（v1.8.1，SetParent 进 Shell_TrayWnd）
│   ├── TaskbarMusicWidget    控件内容（封面/歌名歌手跑马灯/播放态角标，纯显示）
│   ├── 左档贴左缘 +8 DIP / 右档贴托盘左侧；拖动 8px 阈值 + 就近吸附
│   ├── TaskbarThemeHelper    任务栏明暗主题探测配色
│   └── 空闲自动隐藏；点击 → 弹卡；WidgetMoved → 弹卡重锚定
│
├── TaskbarMediaPopupWindow  弹出媒体卡片（v1.8.1，Mica + MediaTransportButtonStyle 播放控制）
│   ├── 动画：FluentFlyout 路径——预置最终位置下方 20px → Window.Top/Opacity 直接动画
│   │   （300ms CubicEase EaseOut，内容模糊 8→0 以 450ms 更缓收尾；收拢 180ms EaseIn）
│   ├── 点击外部/Esc 关闭（Activate + Deactivated 链路，400ms 重开防抖）
│   └── 踩坑留档见 tools/netease-music-tool.md「弹出媒体卡片动画」
│
└── 操作：Show / Hide / Close / ToggleBlur / SetSizeMode / SetWindowLocked
         TogglePlayPause / SkipNext / SkipPrevious / ResetPosition / SetClickThrough
         窗口创建即实例化，切换背景类型或尺寸时替换窗口（非原地切换）
```

### 控制器抽象

`MusicFloatWindowManager` 实现 Core 的 `IMusicFloatController`；`ToolRegistry.DiscoverTools()` 加载插件后经反射获取单例并注册到 `MusicFloatControllerHost`。主程序（MainWindow）通过 `MusicFloatControllerHost.Current` 控制悬浮窗，不再直接引用插件类型。

### 窗口替换模式
切换毛玻璃/透明或大小模式时：保存位置 → 创建新窗口 → 显示新窗口 → 关闭旧窗口，避免 DWM 渲染问题。

### 后台闪退全链路防护
- `DragMove` / `UpdateSongInfo` / `OnMarqueeTick` 全部包裹 try-catch
- `MusicContentControl.Unloaded` 停止跑马灯定时器，防止泄漏
- `SMTCListener` 的 `NowPlayingChanged` 事件在后台线程触发 → `Dispatcher.BeginInvoke` 派发到 UI 线程
- `AppSettings.Save()` / `AudioflowSettings.Save()` 加 try-catch 兜底

## 相关文档

- 主程序层 → [02-main-app.md](02-main-app.md)
- 悬浮窗工具 → [tools/netease-music-tool.md](tools/netease-music-tool.md)
