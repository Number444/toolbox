# 08 · 配置项（横切）

## AppSettings 配置项

持久化：`%LOCALAPPDATA%\Toolbox\settings.json`，单例 `AppSettings.Instance`。

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MinimizeOnClose` | `bool` | `false` | 关闭按钮最小化到任务栏 |
| `AutoOpenFloatWindow` | `bool` | `false` | 启动时自动打开悬浮窗 |
| `MusicFloatSizeMode` | `string` | `"Large"` | 悬浮窗默认大小（Large / Compact） |
| `AutoStart` | `bool` | `false` | 开机自动启动（同步 HKCU\...\Run\Toolbox） |
| `MouseHaloEnabled` | `bool` | `true` | 鼠标跟随光晕开关 |
| `ControlGlowEnabled` | `bool` | `true` | 控件边缘发光开关 |

所有属性变更触发 `PropertyChanged` + `Save()`。底层走 `JsonSettingsFile` 原子写入，同目录 `settings.json.bak` 为最近一次成功写入的备份（清理文件时勿删）。

## AudioflowSettings 配置项

持久化：`%LOCALAPPDATA%\Toolbox\audioflow.json`，与 AppSettings 解耦独立保存加载。

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FloatWindowBlurEnabled` | `bool` | `true` | 悬浮窗 Acrylic 毛玻璃开关 |
| `LockFloatWindow` | `bool` | `false` | 锁定悬浮窗移动 |
| `EdgeDockEnabled` | `bool` | `true` | 贴边自动缩入功能 |
| `ClickThroughEnabled` | `bool` | `false` | 游戏模式点击穿透（鼠标穿透窗口） |
| `ShowPlaybackControls` | `bool` | `true` | 悬停封面显示播放控制按钮 |
| `FloatWindowLeft` | `double` | `NaN` | 窗口 X 坐标（NaN=默认位置） |
| `FloatWindowTop` | `double` | `NaN` | 窗口 Y 坐标（NaN=默认位置） |
| `TaskbarWidgetEnabled` | `bool` | `false` | 任务栏嵌入式音乐控件开关（v1.8.1 新增） |
| `TaskbarWidgetPosition` | `int` | `0` | 控件停靠位置（0=左侧 / 1=右侧，右侧紧邻系统托盘） |
| `TaskbarWidgetHideWhenIdle` | `bool` | `true` | 无播放时自动隐藏控件 |
| `TaskbarWidgetLocked` | `bool` | `false` | 锁定控件禁止拖动 |

> v1.8.1 移除 `TaskbarWidgetControlsEnabled`（控件内嵌播放按钮设置）：任务栏控件改为纯显示，播放控制统一收敛到弹出媒体卡片。旧 JSON 字段被反序列化器自动忽略。

## 持久化机制（JsonSettingsFile）

- 原子写入：先写 .tmp → 替换主文件 → 旧文件留 .bak
- 主文件损坏时 Load 自动回落 .bak，不会因断电/强杀丢光数据
- 设置/密码记录不再因断电写半截而丢光

## 相关文档

- Core 层 → [03-core.md](03-core.md)
- 悬浮窗模块 → [tools/netease-music-tool.md](tools/netease-music-tool.md)
