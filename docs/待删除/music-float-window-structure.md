# 网易云音乐悬浮窗 — 文件结构

```
Toolbox.Plugins/
├── Controls/
│   └── MusicContentControl.xaml(.cs)        核心：封面/歌名/跑马灯/动画/大小模式
│
├── Tools/
│   ├── NeteaseMusicTool.cs                  工具面板：胶囊开关/模式按钮/设置卡片
│   │
│   ├── Views/
│   │   ├── MusicFloatWindow.xaml(.cs)       旧窗口（保留兼容）
│   │   ├── AcrylicMusicWindow.xaml(.cs)     毛玻璃窗口
│   │   └── TransparentMusicWindow.xaml(.cs) 纯透明窗口
│   │
│   ├── Services/
│   │   ├── MusicFloatWindowManager.cs       单例管理器：创建/切换窗口，共享SMTC
│   │   └── SMTCListener.cs                  监听网易云音乐播放状态
│   │
│   └── Models/
│       └── NowPlayingInfo.cs                播放信息数据模型
│
├── Services/
│   └── AudioflowSettings.cs                 悬浮窗独立设置持久化
│
└── Helpers/
    └── DwmHelper.cs                         DWM Acrylic P/Invoke
```

## 架构流向

```
NeteaseMusicTool (工具面板)
    ↓ Show / Hide / 切换
MusicFloatWindowManager (单例，持有 SMTCListener)
    ├── AcrylicMusicWindow → MusicContentControl
    └── TransparentMusicWindow → MusicContentControl
```

共 10 个源文件，3 层职责：**面板触发 → 管理器调度 → 窗口+内容渲染**。
