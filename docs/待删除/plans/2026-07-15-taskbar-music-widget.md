# 方案：任务栏音乐播放控件（仿 TrafficMonitor 同款）

## 目标

在 Windows 任务栏上嵌入一个迷你音乐播放控件，通过 `SetParent` 将窗口注入任务栏进程，实现与 TrafficMonitor 同级别的任务栏集成效果。

控件形态：
```
┌──────────────────────────────────────────────────────────────┐
│ [开始]  [搜索]  [◀◀][▶/⏸][▶▶] ♪ 晴天 - 周杰伦    [通知区域]│
└──────────────────────────────────────────────────────────────┘
                  ↑ 我们的控件（嵌入在任务栏窗口树中）
```

---

## 技术路线

### 核心机制：`SetParent` 窗口注入

不通过任何 COM 接口（DeskBand 在 Win11 已死），直接将自己创建的窗口"收养"到任务栏进程的窗口树中。

```csharp
// 伪代码
var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
var myWidgetHwnd = CreateWindowEx(...); // 无边框迷你窗口
SetParent(myWidgetHwnd, taskbarHwnd);   // 注入
MoveWindow(myWidgetHwnd, x, y, w, h);   // 定位
```

### 为什么不用 WPF

- WPF 的 `HwndSource` + `Dispatcher` 渲染管线假设自己是独立的顶层窗口
- `SetParent` 到外部进程后，WPF 的 DirectX swapchain 可能与任务栏的 DWM 合成冲突
- TrafficMonitor 也是用 MFC + 自绘，不走 WPF

### 我们的渲染方案

| 阶段 | 方案 |
|------|------|
| 第一版 | **Win32 窗口 + GDI+ 自绘**。最稳定、最兼容、最轻量 |
| 升级版 | Vortice.Direct2D1 + DirectComposition（需要透明时） |

GDI+ 完全够用：画三个按钮矩形 + 歌名字符串，匹配任务栏背景色。不需要透明。

---

## 架构设计

```
┌─────────────────────────────────────────────────────┐
│  WPF 主程序 (Toolbox)                                │
│  NeteaseMusicTool → MusicFloatWindow                 │
│                              │                       │
│                     NamedPipe Server                  │
│                  (推送歌名/状态/专辑)                  │
└──────────────────────────┬──────────────────────────┘
                           │ IPC
┌──────────────────────────▼──────────────────────────┐
│  TaskbarWidget.exe (独立进程)                        │
│                                                      │
│  NamedPipe Client ← 接收音乐状态                     │
│         │                                            │
│  ┌──────▼────────┐   ┌─────────────────┐            │
│  │ TaskbarInjector│   │ WidgetRenderer  │            │
│  │ FindWindow     │   │ GDI+ 自绘       │            │
│  │ SetParent      │   │ 按钮 + 文字     │            │
│  │ MoveWindow     │   │ 鼠标点击检测    │            │
│  │ DPI 适配       │   │ 深色/浅色适配   │            │
│  └───────────────┘   └─────────────────┘            │
│                                                      │
│  消息循环 (Application.Run)                          │
│  ├─ WM_PAINT → WidgetRenderer.Draw()                 │
│  ├─ WM_LBUTTONDOWN → 点击检测/回调                    │
│  ├─ WM_DISPLAYCHANGE → 重定位                        │
│  └─ 定时器 → 定期检查位置/DPI                         │
└─────────────────────────────────────────────────────┘
```

### 为什么独立进程

- 主进程 crash 不影响任务栏控件，反之亦然
- `SetParent` 后窗口生命周期独立于 WPF 的 `Application`
- 启动/退出可以独立管理

---

## 关键实现细节

### 1. 任务栏窗口树结构

```
Shell_TrayWnd              ← 任务栏根窗口
├─ ReBarWindow32           ← 工具栏容器（Win10 以前用）
│  └─ MSTaskSwWClass       ← 运行中程序列表区
├─ TrayNotifyWnd           ← 通知区域（Win10）
│  └─ SysPager
│     └─ ToolbarWindow32   ← 系统托盘图标
└─ ...

Win11 结构不同，不再有 ReBarWindow32/MSTaskSwWClass 的明确层级
```

**Win11 关注点**：TrafficMonitor 在 Win11 上直接 `SetParent` 到 `Shell_TrayWnd`，然后根据开始按钮位置 + 居中/居左对齐模式计算位置。

### 2. 嵌入流程

```
1. FindWindow("Shell_TrayWnd")           → 任务栏 HWND
2. GetWindowRect(taskbarHwnd)            → 任务栏位置/尺寸
3. 判断任务栏方向 (top/bottom/left/right)
4. EnumChildWindows 找到关键子窗口        → 定位参考点
5. CreateWindowEx 创建迷你窗口 (WS_CHILD)
6. SetParent(widgetHwnd, taskbarHwnd)    → 注入
7. MoveWindow 定位到通知区域左侧          → 最终位置
8. 检测嵌入是否成功（坐标验证）
```

### 3. 定位计算（Win11 底部任务栏 + 居中开始菜单）

```
Screen
┌──────────────────────────────────────────────────────────┐
│                    桌面工作区                             │
├────────────┬──────┬──────────┬───────────────────────────┤
│ [开始按钮]  │ 小部件│ 程序列表  │  我们的控件  │ [通知区域] │
│    ← left  │      │(居中排列) │   ◀▶⏸▶▶ ♪ │  时间 日期  │
└────────────┴──────┴──────────┴──────────┬────────────────┘
                                          │
                            Shell_TrayWnd (任务栏 HWND)
```

定位公式：
```
x = 开始按钮右侧 + 程序列表区宽度 + 间距
y = (任务栏高度 - 控件高度) / 2
```

**居左对齐时**：直接放在通知区域左侧固定偏移位置。

### 4. 渲染计划

使用 **GDI+ (System.Drawing)** 在一个 Win32 窗口上绘制：

```
┌───┬───┬───┬───────────────────────┐
│ ◀◀ │ ▶ │ ▶▶ │ ♪ 晴天 - 周杰伦       │
└───┴───┴───┴───────────────────────┘
 24px 24px 24px     剩余宽度
 ←─ 三个按钮 ─→     ← 歌名滚动 →

总宽：约 180px（自适应歌名长度）
总高：任务栏高度 - 4px 上下留白
```

绘制流程：
1. `Graphics.FillRectangle` 画背景（匹配任务栏色）
2. `Graphics.DrawLine` + 画三角形 画按钮
3. `Graphics.DrawString` 画歌名
4. `WM_LBUTTONDOWN` 检测点击区域 → IPC 回调主程序切歌

### 5. 任务栏颜色适配

| 方法 | 实现 |
|------|------|
| 读取注册表 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme` |
| 监听变化 | `WM_SETTINGCHANGE` → 重新读取 |
| 浅色模式 | 按钮 #1A1A1A，文字 #333333 |
| 深色模式 | 按钮 #FFFFFF，文字 #FFFFFF |

### 6. 降级策略（SetParent 失败时）

```
SetParent 失败
    → 切换为浮动窗口模式
    → SetWindowPos(HWND_TOPMOST) 确保可见
    → 用户右键菜单提示"无法嵌入任务栏"
    → 继续正常工作（只是浮在任务栏上方而非嵌入）
```

失败常见原因：安全软件拦截、企业策略限制。

---

## IPC 协议（Named Pipe）

### 主程序 → 任务栏控件

```json
{
  "action": "update",
  "title": "晴天",
  "artist": "周杰伦",
  "isPlaying": true,
  "albumArt": "base64_or_path"
}
```

### 任务栏控件 → 主程序

```json
{
  "action": "command",
  "command": "prev" | "play_pause" | "next" | "show_window"
}
```

---

## 新增/修改文件清单

### 新项目：`TaskbarWidget/`

| 文件 | 说明 | 预估行数 |
|------|------|----------|
| `TaskbarWidget.csproj` | 控制台应用，net9.0-windows，UseWindowsForms | 15 |
| `Program.cs` | 入口，NamedPipe 连接，启动消息循环 | 60 |
| `TaskbarInjector.cs` | FindWindow/SetParent/定位/Win32 P/Invoke | 200 |
| `WidgetRenderer.cs` | GDI+ 绘制 + 鼠标点击区域检测 | 150 |
| `MusicState.cs` | 状态模型 | 30 |
| `IpcClient.cs` | NamedPipe 客户端 | 80 |

### 修改：`Toolbox.Plugins/`

| 文件 | 变更 |
|------|------|
| `NeteaseMusicTool.cs` | 启动/关闭 `TaskbarWidget.exe` 进程 |
| 新增 `IpcServer.cs` | NamedPipe 服务端，推送音乐状态、接收按钮事件 | 80 |

### 修改：`Toolbox/`

| 文件 | 变更 |
|------|------|
| `App.xaml.cs` | 退出时关闭 TaskbarWidget 进程 |

---

## 风险

| 风险 | 级别 | 缓解 |
|------|------|------|
| Win11 更新改变任务栏内部结构 | 中 | 位置计算带冗余校验，失败降级浮动模式 |
| SetParent 被安全软件拦截 | 中 | 自动降级浮动，右键提示用户 |
| GDI+ 在 DPI 缩放下的清晰度 | 低 | `SetProcessDpiAwareness` + 字体自适应 |
| 进程间通信超时 | 低 | NamedPipe 自带超时重连 |

---

## 与 TrafficMonitor 的差异

| 维度 | TrafficMonitor | 我们 |
|------|---------------|------|
| 语言 | C++ / MFC | C# / .NET 9 |
| 渲染 | GDI 或 Direct2D | GDI+（第一版） |
| 数据源 | 系统性能计数器 | NamedPipe 接收主程序状态 |
| 交互 | 右键菜单 + 双击 | 左键切歌 + 右键菜单 |
| 配置 | INI 文件 | 主程序统一管理 |
| 架构 | 单体 MFC 应用 | 独立进程 + IPC |
