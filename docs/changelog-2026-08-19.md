# changelog-2026-08-19

> 当日工作日志：任务栏嵌入式音乐控件（新功能）+ v1.8.1 发布。

## 1. 任务栏嵌入式音乐控件（TaskbarMusicWidget）

- **任务栏内嵌控件**：`TaskbarMusicWindow` 以子窗口形式嵌入 Shell_TrayWnd（SetParent），纯显示：封面 32×32（圆角 6 + 描边）、歌名/歌手双行、超宽跑马灯滚动（FormattedText 精确测宽）、播放态角标（Segoe Fluent Icons E768/E769）
- **位置**：左/右两档（右键菜单切换）；左侧紧贴任务栏左边缘 + 8 DIP 间距（随 DPI 缩放）；可拖动，8px 阈值区分点击/拖拽，松手吸附
- **主题自适应**：`TaskbarThemeHelper` 读注册表 SystemUsesLightTheme，文字/悬停色随任务栏明暗切换
- **无播放时自动隐藏**（可配置），悬停 15% 圆角高亮

## 2. 弹出媒体卡片（TaskbarMediaPopupWindow）

- 点击控件弹出 340×120 Mica 卡片（DWM 系统背板 + WindowChrome，边框交 DWM 原生绘制）：封面 88×88 投影、歌名/歌手垂直居中、播放控制按钮行（上一首/播放暂停/下一首）
- **按钮走公共样式**：新增 `MediaTransportButtonStyle`（App.xaml），严格复刻全局 StandardButtonStyle 叠层法（HoverOverlay 白 0.10 / PressOverlay 黑 0.12 + 缩放 0.97）
- **打开动画（FluentFlyout 同款路径，参照 github.com/unchihugo/FluentFlyout 源码）**：Show 前预置于最终位置下方 20px → 直接动画 Window.Top 上移 + Window.Opacity 0→1（300ms CubicEase EaseOut），内容层模糊 8→0 以 450ms 更缓收尾；收拢 180ms EaseIn 镜像后 Hide
  - 踩坑记录：WPF 逐帧改窗口 Top/Height 会引起 DWM 重算背板 → 抽搐；Window.Opacity<1 会切 WS_EX_LAYERED 杀死 Acrylic（但 FluentFlyout 实证 Opacity 回到 1 后背板恢复，最终采用此路径）；面纱机制（v6）证明只能遮材质闪现、遮不住窗口存在闪现，已删除
- **点击外部自动关闭**（Activate + Deactivated 链路，400ms 重开防抖），Esc 关闭

## 3. 退出/重启健壮性修复

- X 关闭主窗口（后台留任务栏）时卡片窗口残留 → 进程不退、重启唤起崩溃（RestoreFromTray 对已关闭窗口 Show）
- 修复：`MainWindow.OnClosed` 兜底关闭卡片窗口 + `_isClosed` 守卫

## 4. 设置项清理

- 移除"任务栏控件内嵌播放按钮"设置（`TaskbarWidgetControlsEnabled` 全链删除：AudioflowSettings / NeteaseMusicTool 复选框 / TaskbarThemeHelper 冗余色）——控件纯显示，播放控制集中在弹出卡片

## 5. v1.8.1 发布

- 版本号 v1.8.0 → **v1.8.1**（`setup/ToolboxSetup.iss` + 底部状态栏）
- 产物 `setup/Toolbox_Setup.exe`（Release 自包含单文件，ISCC LZMA2）；已 push 云端（b2104f6）
