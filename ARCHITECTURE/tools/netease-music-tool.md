# 工具 · 网易云音乐悬浮窗（NeteaseMusicTool）— 悬浮窗子模块

- **分类**：🎵 媒体与娱乐（Media）
- **入口文件**：`Toolbox.Plugins/Tools/NeteaseMusicTool.cs`
- **定位**：独立子模块，进一步分层 Views / Controls / Services / Models / Helpers

## 功能

- 工具面板：胶囊开关 + 模式切换
- 毛玻璃 / 锁定 / 贴边 / 游戏模式设置
- 桌面悬浮窗：贴边自动缩入、封面显示、多档背景效果
- 悬停封面播放控制（上一首 / 播放暂停 / 下一首）
- 游戏模式点击穿透（鼠标完全穿透，适合全屏游戏）
- **任务栏嵌入式控件**（v1.8.1）：封面 + 歌名/歌手跑马灯，左/右两档停靠、可拖动吸附、主题自适应、无播放自动隐藏（纯显示，无按钮）
- **弹出媒体卡片**（v1.8.1）：点击控件弹出 340×120 Mica 卡片（封面 + 播放控制三按钮，公共样式 `MediaTransportButtonStyle`），FluentFlyout 式上浮淡入动画，点外部 / Esc 关闭

## 子模块树

```
Tools/
├── NeteaseMusicTool.cs              工具面板
├── Models/
│   └── NowPlayingInfo.cs            当前播放信息模型
├── Services/
│   ├── SMTCListener.cs              SMTC 监听器（Windows 原生 API 监听媒体）
│   ├── MusicFloatWindowManager.cs   悬浮窗管理器（单例，实现 Core 的 IMusicFloatController）
│   └── EdgeDockService.cs           贴边自动缩入状态机
└── Views/
    ├── AcrylicMusicWindow.xaml(.cs)      毛玻璃悬浮窗（WindowChrome + DWM Acrylic）
    ├── TransparentMusicWindow.xaml(.cs)  纯透明悬浮窗（AllowsTransparency=True）
    ├── TaskbarMusicWindow.xaml(.cs)      任务栏嵌入宿主（SetParent 进 Shell_TrayWnd，定位/拖动/吸附）
    └── TaskbarMediaPopupWindow.xaml(.cs) 弹出媒体卡片（Mica + 播放控制 + FluentFlyout 式动画）
```

共享控件（插件层 Controls/）：
- `MusicContentControl.xaml(.cs)` 悬浮窗共享内容控件
- `DockTriggerBar.xaml(.cs)` 贴边触发条控件
- `TaskbarMusicWidget.xaml(.cs)` 任务栏嵌入式控件内容（封面/跑马灯/角标，纯显示）

共享辅助（插件层 Helpers/）：`TaskbarThemeHelper.cs` 任务栏主题探测（明暗配色）。

## 关键机制

### SMTCListener（媒体监听）
- SemaphoreSlim 串行化消除并发竞态
- 陈旧封面检测：切歌后 SMTC 可能返回旧封面
- 6 次退避重试（200/200/400/800/1500/3000ms）
- 歌曲身份绑定的重试取消（单纯进度/状态事件不打断封面重试）
- 加固：启动退避重试（5/15/30s）+ 30s 看门狗自愈 + 休眠唤醒重建
- `NowPlayingChanged` 后台线程事件 → `Dispatcher.BeginInvoke` 派发到 UI 线程

### EdgeDockService（贴边缩入）
- 状态机：Free → Docking → Docked → Expanding → Expanded
- DispatcherTimer 驱动动画（EaseOutCubic）
- DockTriggerBar 触发条悬停展开
- EdgeThreshold：透明窗口 -5px / 毛玻璃窗口 10px

### 窗口替换模式
切换毛玻璃/透明或大小模式时：保存位置 → 创建新窗口 → 显示 → 关闭旧窗口，避免 DWM 渲染问题。

### 游戏模式点击穿透（ClickThroughHelper）
- Transparent 窗口：WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
- Acrylic 窗口：仅 WS_EX_NOACTIVATE + WM_NCHITTEST 拦截
- 开启后鼠标完全穿透，不会切出全屏游戏焦点
- 游戏模式下禁用悬停播放按钮浮出 + 禁用拖拽移动

### 控制器抽象
`MusicFloatWindowManager` 实现 Core 的 `IMusicFloatController`；`ToolRegistry` 加载插件后反射注册到 `MusicFloatControllerHost`；主程序经 `Current` 控制悬浮窗，不直接引用插件类型。

### 任务栏嵌入（TaskbarMusicWindow，v1.8.1）
- 子窗口嵌入 `Shell_TrayWnd`（SetParent），`SetWindowPos(SWP_NOACTIVATE)` 定位，HWND_TOP 防被 ReBar/托盘盖住
- 左档：紧贴任务栏左边缘 + 8 DIP 间距（乘 DPI 缩放）；右档：紧邻系统托盘左侧（TrayNotifyWnd 矩形换算）
- 拖动：8px 阈值区分点击/拖拽，松手就近吸附左右档；坐标全部物理像素（控件 FixedWidth/FixedHeight × dpiScale）
- 点击 → Manager 弹卡；`WidgetMoved` → 弹卡重锚定；400ms 重开防抖（点控件关卡 vs 失焦关卡的竞态）

### 弹出媒体卡片动画（TaskbarMediaPopupWindow，v1.8.1）
- **FluentFlyout 同款路径**（参照 github.com/unchihugo/FluentFlyout）：Show 前预置于最终位置下方 20px、Opacity 0 → 直接动画 `Window.Top` 上移 + `Window.Opacity` 0→1（300ms CubicEase EaseOut），内容层 BlurEffect 8→0 以 450ms 更缓收尾；收拢 180ms EaseIn 后 Hide
- 穿越任务栏段窗口透明 → 层序天然免疫；起点在动画位 → 无首帧闪现
- 踩坑留档：①逐帧改窗口 Top/Height 会触发 DWM 每帧重算背板 → 必抽搐（v5 事故）；②Window.Opacity<1 切 WS_EX_LAYERED 会杀 Acrylic（v2 事故），但 Opacity 回 1 后 WPF 摘 layered 属性、DWM 背板恢复（FluentFlyout 实证）；③"面纱遮罩"只能遮材质闪现、遮不住窗口存在闪现（v6 弃用）；④Mica/Acrylic 真材质只能 DWM 窗口背板，视觉树内不可动画
- 点击外部关闭：Open 时 Activate() 一次（点击来自任务栏无感）→ Deactivated → AnimatedClose

## 配置（AudioflowSettings）

毛玻璃 / 锁定 / 贴边 / 游戏模式 / 播放按钮 / 窗口位置 / 任务栏控件四键（开关/位置/空闲隐藏/锁定），独立 `audioflow.json`（详见 ../08-settings.md）。

## 测试覆盖

- `NeteaseMusicToolTests.cs`

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| IMusicFloatController / MusicFloatControllerHost | 控制器抽象（Core） |
| AudioflowSettings | 悬浮窗独立设置 |
| ClickThroughHelper / MonitorHelper / ThemedMenuWindow | 穿透 / 多屏 / 右键菜单 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 已知问题

> 状态以 `docs/待解决-2026-07-31.md` 为准（唯一事实源），本页不复述具体条目以避免状态过时（当前含 P2-12/P2-14/P2-16/P2-17）。

## 相关文档


- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 关键流程（悬浮窗架构）→ [../06-flows.md](../06-flows.md)
- 配置项 → [../08-settings.md](../08-settings.md)
