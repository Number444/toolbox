# 04 · 插件层总览（Toolbox.Plugins）

> 工具实现层：独立程序集，运行时反射加载。每个工具一个 .cs 文件实现 ITool，
> `CreateContent()` 返回 UIElement。**增删工具不修改主项目代码**。

## 工具索引

| 工具 | 叶子文档 | 分类 |
|------|---------|------|
| 首页仪表盘 | [tools/home-dashboard-tool.md](tools/home-dashboard-tool.md) | 📊 首页 |
| 定时关机 | [tools/shutdown-tool.md](tools/shutdown-tool.md) | ⚙️ 系统维护 |
| 屏保启动 | [tools/screensaver-tool.md](tools/screensaver-tool.md) | ⚙️ 系统维护 |
| 快捷系统操作 | [tools/quick-system-tool.md](tools/quick-system-tool.md) | ⚙️ 系统维护 |
| C盘垃圾清理 | [tools/junk-cleaner-tool.md](tools/junk-cleaner-tool.md) | ⚙️ 系统维护 |
| 二维码生成 | [tools/qrcode-tool.md](tools/qrcode-tool.md) | 🔤 文本与数据 |
| 网络信息 | [tools/network-info-tool.md](tools/network-info-tool.md) | 🌐 网络与开发 |
| 远程控制 | [tools/remote-control-tool.md](tools/remote-control-tool.md) | 🌐 网络与开发 |
| 文件传输 | [tools/file-transfer-tool.md](tools/file-transfer-tool.md) | 🌐 网络与开发 |
| 软件卸载管理器 | [tools/software-uninstall-tool.md](tools/software-uninstall-tool.md) | 📁 文件管理 |
| 密码生成器 | [tools/password-generator-tool.md](tools/password-generator-tool.md) | 🔤 文本与数据 |
| 截图识字 | [tools/ocr-tool.md](tools/ocr-tool.md) | 🔤 文本与数据 |
| 网易云音乐悬浮窗 | [tools/netease-music-tool.md](tools/netease-music-tool.md) | 🎵 媒体与娱乐 |

> 统计口径：13 个工具 + 悬浮窗子模块 + OCR 引擎子系统。新增工具时在此表登记（见 09-tool-dev.md）。

## 共享 Helpers/（非工具，归入层概览）

| 文件 | 职责 |
|------|------|
| SystemPowerHelper.cs | 系统电源操作：Lock / TurnOffMonitor / Sleep（插件层自含 P/Invoke） |
| SystemInfoHelper.cs | 轻量系统信息：内存占用%/运行时长/磁盘空间/本机 IPv4/公网 IP/电池（GetBatteryInfo） |
| MonitorHelper.cs | 多屏工作区查询（MonitorFromWindow + GetMonitorInfo） |
| ClickThroughHelper.cs | 悬浮窗游戏模式点击穿透（Transparent/Acrylic 两套实现） |
| OcrHelper.cs | Windows 内置 OCR 引擎封装（离线识别） |
| PaddleOcrWrapper.cs | PaddleOCR 高精度引擎包装（原生库加载/释放） |
| EngineDownloader.cs | OCR 引擎/模型下载、校验与解压（多下载源 + 重试 + 进度节流） |
| ImageFileHelper.cs | 图片文件校验与格式判断 |
| ClipboardHelper.cs | 剪贴板写入辅助：专用 STA 线程执行（剪贴板被占用时 WPF 内部重试可阻塞 UI 数百 ms，UI 线程直调会"卡一下"），结果经 Dispatcher 回传；NetworkInfoTool 已接入，其余工具复制可复用 |
| TaskbarThemeHelper.cs | 任务栏主题探测（读 SystemUsesLightTheme）：为任务栏嵌入控件提供文字/悬停/描边配色 |

## 共享 Services/

| 文件 | 职责 |
|------|------|
| AudioflowSettings.cs | 悬浮窗独立设置（audioflow.json）：毛玻璃/锁定/贴边/游戏模式/播放按钮/窗口位置/任务栏控件 |
| SoftwareUninstallService.cs | 已安装软件扫描 + 卸载执行（注册表 + 图标提取 + UAC 提权） |

## 共享 Controls/

| 文件 | 职责 |
|------|------|
| MusicContentControl.xaml(.cs) | 悬浮窗共享内容控件（封面/歌名/大小模式/跑马灯/切歌动画/悬停播放按钮） |
| DockTriggerBar.xaml(.cs) | 贴边触发条控件（梯形圆角 + 方向箭头） |
| TaskbarMusicWidget.xaml(.cs) | 任务栏嵌入式音乐控件（封面/歌名歌手双行跑马灯/播放态角标，纯显示，播放控制在弹出卡片） |

## 对话框

| 文件 | 职责 |
|------|------|
| ConfirmDialog.cs | 统一深色主题确认弹窗（通用删除/清空确认；可选 warningText 警示行；PopupAnimator 开关动画，v1.8.3；JunkCleanerTool 私有副本已于 v1.8.3 合并删除） |
| DownloadDialog.cs | OCR 引擎下载进度对话框（进度条 + 取消；PopupAnimator 开关动画，v1.8.3） |

## Models/

| 文件 | 职责 |
|------|------|
| InstalledSoftware.cs | 已安装软件数据模型 |
| SortMode.cs | 排序模式枚举 + 扩展方法 |
| Toolbox.Plugins/Tools/Models/NowPlayingInfo.cs | 当前播放信息模型 |

## 命名空间约定

- 插件层统一 `Toolbox.Plugins.*`（Helpers/Services/Models/Controls）
- 工具类保留 `Toolbox.Tools.*`
- `Directory.Build.props` 处理 wpftmp 临时编译项目的重复生成问题

## 相关文档

- 每个工具的实现细节 → [tools/](tools/)
- 开发新工具规范 → [09-tool-dev.md](09-tool-dev.md) + docs/TOOL_DEVELOPMENT_GUIDELINE.md
