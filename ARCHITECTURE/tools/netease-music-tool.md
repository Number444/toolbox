# 工具 · 网易云音乐悬浮窗（NeteaseMusicTool）— 悬浮窗子模块

- **分类**：🎵 媒体与娱乐（Media）
- **入口文件**：`Toolbox.Plugins/Tools/NeteaseMusicTool.cs`（317 行）
- **定位**：独立子模块，进一步分层 Views / Controls / Services / Models / Helpers

## 功能

- 工具面板：胶囊开关 + 模式切换
- 毛玻璃 / 锁定 / 贴边 / 游戏模式设置
- 桌面悬浮窗：贴边自动缩入、封面显示、多档背景效果
- 悬停封面播放控制（上一首 / 播放暂停 / 下一首）
- 游戏模式点击穿透（鼠标完全穿透，适合全屏游戏）

## 子模块树

```
Tools/
├── NeteaseMusicTool.cs              工具面板（317 行）
├── Models/
│   └── NowPlayingInfo.cs            当前播放信息模型
├── Services/
│   ├── SMTCListener.cs              582 行 · SMTC 监听器（Windows 原生 API 监听媒体）
│   ├── MusicFloatWindowManager.cs   539 行 · 悬浮窗管理器（单例，实现 Core 的 IMusicFloatController）
│   └── EdgeDockService.cs           532 行 · 贴边自动缩入状态机
└── Views/
    ├── AcrylicMusicWindow.xaml(.cs)      184 行 · 毛玻璃悬浮窗（WindowChrome + DWM Acrylic）
    └── TransparentMusicWindow.xaml(.cs)  92 行 · 纯透明悬浮窗（AllowsTransparency=True）
```

共享控件（插件层 Controls/）：
- `MusicContentControl.xaml(.cs)` 804 行 · 悬浮窗共享内容控件
- `DockTriggerBar.xaml(.cs)` 123 行 · 贴边触发条控件

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

## 配置（AudioflowSettings）

毛玻璃 / 锁定 / 贴边 / 游戏模式 / 播放按钮 / 窗口位置，独立 `audioflow.json`（详见 ../08-settings.md）。

## 测试覆盖

- `NeteaseMusicToolTests.cs`

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| IMusicFloatController / MusicFloatControllerHost | 控制器抽象（Core） |
| AudioflowSettings | 悬浮窗独立设置 |
| ClickThroughHelper / MonitorHelper / ThemedMenuWindow | 穿透 / 多屏 / 右键菜单 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 已知问题（未解决）

- **P2-12**：ToggleBlur/SetSizeMode 失败回滚路径泄漏半成品新窗口——`newWindow.Show()` 抛异常后回滚只重挂旧窗口，newWindow 未 Close（极端低概率）
- **P2-14**：SMTCListener 并发小噪音——Stop 置空竞态下 `_session!` 可能 NRE（被 catch 吞）；StartAsync 重试可能短暂重复订阅（均被兜住，无实际危害）
- **P2-16**：MusicFloatWindow.xaml/.cs 是仅供测试实例化的废弃死代码，未标记废弃（生产只创建 Acrylic/Transparent；移动/删除前须先改测试）
- **P2-17**：悬浮窗首次 Save 静默失败——Save() 无条件写入位置 NaN，.NET 9 STJ Strict 模式序列化 NaN 抛异常被 JsonSettingsFile catch 吞掉，未拖过悬浮窗时切换设置不持久化
- 见 `docs/待解决-2026-07-31.md`

## 相关文档


- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 关键流程（悬浮窗架构）→ [../06-flows.md](../06-flows.md)
- 配置项 → [../08-settings.md](../08-settings.md)
