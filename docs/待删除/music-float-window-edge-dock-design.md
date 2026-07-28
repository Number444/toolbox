# 悬浮窗「贴边自动缩入」— 详细实现方案

> 基于 music-float-window-structure.md（10 文件架构）与 edge-dock-plan.md（高层设计），展开为可直接执行的逐文件实现指南。

---

## 1. 架构总览

```
MusicFloatWindowManager（单例）
│
├── EdgeDockService（单例，由 Manager 持有）
│   ├── State Machine: Free | Docking | Docked | Expanding | Expanded
│   ├── Animation Engine: DispatcherTimer @ 60fps + EaseOutCubic
│   ├── Persistent State（窗口替换后保留）:
│   │   ├── State / Direction / SavedLeft / SavedTop / Enabled
│   │   └── IsAnimating（动画锁，防重入）
│   └── Methods: Attach(Window) / Detach() / OnDragCompleted() / Expand() / ScheduleCollapse()
│
├── ActiveWindow（Acrylic 或 Transparent）
│   ├── MusicContentControl（Visibility 由 Service 控制：Docked→Collapsed）
│   └── DockTriggerBar（Visibility 由 Service 控制：Docked→Visible）
│       ├── Trapezoid Path 外形 + 方向箭头 Path
│       ├── 背景色继承父窗口效果（Acrylic 天然 / Transparent 半透明黑）
│       └── Events: MouseEnter→Expand, MouseLeftButtonDown→DragMove, MouseLeave→缩回
```

**关键设计决策：EdgeDockService 归 Manager 层，不归 Window 层。** 因为 Manager 在切换大小模式 / 毛玻璃时会**销毁旧窗口、创建新窗口**，如果 Service 挂在 Window 上状态会丢失。

---

## 2. 数据流

```
用户拖拽窗口到左侧边缘（距离 ≤ 20px）
  → DragMove() 返回
  → Manager.OnDragCompleted()
  → EdgeDockService.CheckEdgeDock()
  → 判定：左边缘距离 5px ≤ 20px 阈值 → StartDock(Left)
  → 动画：Window.Left 从 20 → -(242 - 14) = -228
  → 动画结束 → State=Docked
  → MusicContentControl.Visibility = Collapsed
  → DockTriggerBar.Visibility = Visible

用户鼠标悬停触发条
  → DockTriggerBar.MouseEnter
  → EdgeDockService.Expand()
  → 动画：Window.Left 从 -228 → 20（之前保存的位置）
  → 动画结束 → State=Expanded
  → DockTriggerBar.Visibility = Collapsed
  → MusicContentControl.Visibility = Visible

用户鼠标离开窗口（300ms 无重新进入）
  → Window.MouseLeave → ScheduleCollapse(300ms)
  → 300ms 内 MouseEnter → 取消
  → 300ms 超时 → StartDock() 缩回
```

---

## 3. 详细文件改动

### 3.1 新增文件

#### `Controls/DockTriggerBar.xaml` + `.xaml.cs`

**职责**：渲染贴边时的可见触发条（14px 宽 × 完整窗口高），含梯形轮廓、方向箭头。

**XAML 结构**：

```xml
<UserControl x:Class="Toolbox.Controls.DockTriggerBar"
    Width="14" Height="252"
    IsHitTestVisible="True"
    Cursor="Hand">

    <Grid>
        <!-- 梯形本体：Path 画圆角梯形 -->
        <Path x:Name="ShapePath"
              Fill="#881A1A1A"
              Stroke="{x:Null}" />

        <!-- 方向箭头（贴左→箭头朝右，贴右→箭头朝左） -->
        <Path x:Name="ArrowPath"
              Fill="#CCFFFFFF"
              Stroke="{x:Null}"
              HorizontalAlignment="Center"
              VerticalAlignment="Center" />
    </Grid>
</UserControl>
```

**x:Name 暴露给外部**：`ShapePath`, `ArrowPath`

**Code-behind 方法**：

```csharp
// 设置方向（更新梯形和箭头方向）
public void SetDirection(DockDirection direction)
{
    // Left-dock: 梯形窄边在左(屏幕外)、宽边在右(可见边)
    // Right-dock: 反之
    // 箭头指向屏幕内侧
    UpdateShape(direction);
    UpdateArrow(direction);
}

// 梯形：上边 3px → 下边 14px（逐渐变宽），顶部圆角
private void UpdateShape(DockDirection dir) { /* Path.Data = StreamGeometry */ }

// 箭头：等边三角形，指向屏幕内侧
private void UpdateArrow(DockDirection dir) { /* Path.Data = StreamGeometry */ }

// 拖拽事件委托给 Manager
public event EventHandler? DragRequested;
private void OnMouseLeftButtonDown(...) => DragRequested?.Invoke(this, EventArgs.Empty);
```

**设计要点**：
- 不自己处理 DragMove → 由 Manager 统一管理（需从 MouseLeftButtonDown 到 DragMove 的桥接）
- IsHitTestVisible=True，确保能捕获鼠标事件
- 触发器条宽度固定 14px，高度跟随窗口（由父容器约束）

---

#### `Tools/Services/EdgeDockService.cs`

**职责**：状态机核心 + 窗口位移动画引擎 + 边缘检测 + 缩回防抖。

**命名空间**：`Toolbox.Tools.Services`

**完整字段**：

```csharp
public enum DockState { Free, Docking, Docked, Expanding, Expanded }
public enum DockDirection { Left, Right }

public class EdgeDockService
{
    // ── 持久状态（窗口替换后保留）──
    public bool Enabled { get; set; } = true;
    public DockState State { get; private set; } = DockState.Free;
    public DockDirection Direction { get; private set; }
    public double SavedLeft { get; private set; }
    public double SavedTop { get; private set; }

    // ── 动画 ──
    private bool _isAnimating;

    // ── 挂载对象（每次窗口替换后重新 Attach）──
    private Window? _window;
    private MusicContentControl? _content;
    private DockTriggerBar? _triggerBar;

    // ── 缩回防抖 ──
    private System.Windows.Threading.DispatcherTimer? _collapseTimer;

    // ── 可配置 ──
    private const double TriggerBarWidth = 14;
    private const double EdgeThreshold = 20;  // 后续可从 AudioflowSettings 读取
    private const int AnimationDurationMs = 250;
    private const int FrameIntervalMs = 16;    // ~60fps
    private const int CollapseDelayMs = 300;
}
```

**核心方法**：

##### `Attach(Window, MusicContentControl, DockTriggerBar)`
- 从旧窗口解绑事件 → 绑定新窗口事件
- 如果当前 State=Docked → 应用停靠位置 + 显示 TriggerBar、隐藏 Content
- 如果当前 State=Expanded → 正常显示
- 监听 `Window.MouseLeave` → `ScheduleCollapse`

##### `Detach()`
- 解绑所有窗口事件
- 停止动画 Timer
- 取消 Collapse Timer

##### `OnDragCompleted()`
```csharp
public void OnDragCompleted()
{
    if (!Enabled || _window == null) return;

    // DragMove 结束后检测贴边
    var screen = System.Windows.Forms.Screen.FromHandle(
        new System.Windows.Interop.WindowInteropHelper(_window).Handle);
    var wa = screen.WorkingArea;

    double leftDist = _window.Left - wa.Left;
    double rightDist = wa.Right - (_window.Left + _window.Width);

    if (leftDist >= -5 && leftDist <= EdgeThreshold)
        StartDock(DockDirection.Left, wa.Left);
    else if (rightDist >= -5 && rightDist <= EdgeThreshold)
        StartDock(DockDirection.Right, wa.Right);
    else
    {
        // 不在边缘 → 保持 Free / Expanded 状态
        SavedLeft = _window.Left;
        SavedTop = _window.Top;
        State = DockState.Free;
    }
}
```

> `leftDist >= -5` 容忍窗口稍微超出左边缘（多屏拖拽时可能）

##### `StartDock(DockDirection direction, double edgeX)`
```csharp
private void StartDock(DockDirection direction, double edgeX)
{
    if (_isAnimating || _window == null) return;

    SavedLeft = _window.Left;   // 保存展开后的位置，供 Expand 复原
    SavedTop = _window.Top;
    Direction = direction;
    State = DockState.Docking;
    _isAnimating = true;

    double targetLeft = direction == DockDirection.Left
        ? edgeX - _window.Width + TriggerBarWidth   // 例：0 - 242 + 14 = -228
        : edgeX - TriggerBarWidth;                   // 例：1920 - 14 = 1906

    AnimateWindowMove(_window.Left, targetLeft, _window.Top, () =>
    {
        State = DockState.Docked;
        _isAnimating = false;
        if (_content != null) _content.Visibility = Visibility.Collapsed;
        if (_triggerBar != null)
        {
            _triggerBar.SetDirection(direction);
            _triggerBar.Visibility = Visibility.Visible;
        }
    });
}
```

##### `Expand()`
```csharp
public void Expand()
{
    if (_isAnimating || _window == null) return;
    if (State != DockState.Docked) return;

    _collapseTimer?.Stop();
    State = DockState.Expanding;
    _isAnimating = true;

    if (_triggerBar != null) _triggerBar.Visibility = Visibility.Collapsed;
    if (_content != null) _content.Visibility = Visibility.Visible;

    AnimateWindowMove(_window.Left, SavedLeft, SavedTop, () =>
    {
        State = DockState.Expanded;
        _isAnimating = false;
    });
}
```

##### `ScheduleCollapse()`
```csharp
public void ScheduleCollapse()
{
    if (State != DockState.Expanded || !Enabled) return;

    _collapseTimer?.Stop();
    _collapseTimer = new System.Windows.Threading.DispatcherTimer(
        TimeSpan.FromMilliseconds(CollapseDelayMs),
        System.Windows.Threading.DispatcherPriority.Normal,
        (s, e) =>
        {
            _collapseTimer?.Stop();
            if (State != DockState.Expanded) return; // 300ms 内可能已手动改变状态

            // 重新计算边缘位置（屏幕可能已变化）
            if (_window == null) return;
            var screen = System.Windows.Forms.Screen.FromHandle(...);
            var wa = screen.WorkingArea;
            double edgeX = Direction == DockDirection.Left ? wa.Left : wa.Right;
            StartDock(Direction, edgeX);
        },
        _window?.Dispatcher ?? System.Windows.Application.Current.Dispatcher);
    _collapseTimer.Start();
}
```

##### 动画引擎 `AnimateWindowMove`

```csharp
private void AnimateWindowMove(double fromLeft, double toLeft, double top, Action onCompleted)
{
    if (_window == null) return;

    var startTime = DateTime.Now;
    var duration = TimeSpan.FromMilliseconds(AnimationDurationMs);

    var timer = new System.Windows.Threading.DispatcherTimer(
        TimeSpan.FromMilliseconds(FrameIntervalMs),
        System.Windows.Threading.DispatcherPriority.Normal,
        null,
        _window.Dispatcher);

    timer.Tick += (s, e) =>
    {
        var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
        var progress = Math.Min(elapsed / AnimationDurationMs, 1.0);

        // EaseOutCubic: f(t) = 1 - (1-t)^3
        var eased = 1.0 - Math.Pow(1.0 - progress, 3);

        _window.Left = fromLeft + (toLeft - fromLeft) * eased;
        _window.Top = top;

        if (progress >= 1.0)
        {
            timer.Stop();
            _window.Left = toLeft; // 确保最终值精确
            onCompleted();
        }
    };

    timer.Start();
}
```

**注意**：动画期间 `_isAnimating=true`，防止重复触发。所有公开方法入口检查此标记。

---

### 3.2 修改文件

#### `Services/AudioflowSettings.cs`（改 ~20 行）

新增字段：

```csharp
private bool _edgeDockEnabled = true;
public bool EdgeDockEnabled
{
    get => _edgeDockEnabled;
    set
    {
        if (_edgeDockEnabled == value) return;
        _edgeDockEnabled = value;
        OnPropertyChanged();
        Save();
    }
}
```

同步修改 `AudioflowData` 内部类（序列化）和 `Load()` / `Save()` 方法，加上 `EdgeDockEnabled` 字段。

> 阈值 `EdgeThreshold` 暂写死 20px，后续可按需加入 `EdgeDockThreshold` 配置字段。

---

#### `Tools/Views/AcrylicMusicWindow.xaml`（改 ~10 行）

在 `ContentRoot` Grid 内，MusicContentControl 之下新增：

```xml
<!-- 贴边触发条（默认隐藏，Docked 状态时才可见） -->
<controls:DockTriggerBar x:Name="DockTriggerBar"
    Width="14" Height="252"
    VerticalAlignment="Stretch"
    Visibility="Collapsed"
    MouseEnter="DockTriggerBar_MouseEnter"
    MouseLeave="DockTriggerBar_MouseLeave" />
```

触发条 Direction（Left/Right 锚定）在 code-behind 动态设置。

---

#### `Tools/Views/AcrylicMusicWindow.xaml.cs`（改 ~15 行）

新增公开属性/方法 + 事件处理：

```csharp
// 公开给 Manager 访问
public DockTriggerBar TriggerBar => DockTriggerBar;

// DragMove 后回调 → 通知 Manager 检测贴边
public event EventHandler? DragMoveCompleted;

private void OnDragRequested(object? sender, EventArgs e)
{
    if (!_isLocked && Mouse.LeftButton == MouseButtonState.Pressed)
    {
        DragMove();
        DragMoveCompleted?.Invoke(this, EventArgs.Empty);  // ← 新增
    }
}

// 触发条拖拽 → 先展开再拖拽（从 Docked 状态脱离）
private void DockTriggerBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    // 先执行 Expand 动画（Manager 在 Attach 时绑定此事件）
    TriggerBarDragRequested?.Invoke(this, EventArgs.Empty);
}

// 触发条悬停 → Manager 调 Expand()
// MouseEnter/MouseLeave 在 XAML 声明，code-behind 为空或转发
private void DockTriggerBar_MouseEnter(object sender, MouseEventArgs e) { }
private void DockTriggerBar_MouseLeave(object sender, MouseEventArgs e) { }
```

> `TriggerBarDragRequested` 事件在 Manager 的 `Attach()` 中订阅，Manager 负责先调 `Expand()` 展开窗口，然后触发 `DragMove()`，最后检测贴边。

---

#### `Tools/Views/TransparentMusicWindow.xaml`（改 ~10 行）

与 AcrylicMusicWindow 相同，加 DockTriggerBar。区别：Transparent 窗口无毛玻璃，触发条背景用半透明黑 `#881A1A1A`（已在 DockTriggerBar 控件内处理）。

```xml
<controls:DockTriggerBar x:Name="DockTriggerBar"
    Width="14" Height="252"
    VerticalAlignment="Stretch"
    Visibility="Collapsed"
    MouseEnter="DockTriggerBar_MouseEnter"
    MouseLeave="DockTriggerBar_MouseLeave" />
```

---

#### `Tools/Views/TransparentMusicWindow.xaml.cs`（改 ~15 行）

与 AcrylicMusicWindow.cs 完全相同的改动。

---

#### `Tools/Services/MusicFloatWindowManager.cs`（改 ~40 行）

这是集成的主战场。

##### 新增字段与初始化

```csharp
private readonly EdgeDockService _dockService = new();

public EdgeDockService DockService => _dockService;
```

##### 修改 `CreateWindow()` — 创建后立即 Attach

```csharp
private Window CreateWindow()
{
    Window window = _blurEnabled
        ? new AcrylicMusicWindow()
        : new TransparentMusicWindow();

    GetContentControl(window).SizeMode = _sizeMode;

    // 挂载 EdgeDockService
    _dockService.Attach(
        window,
        GetContentControl(window),
        GetTriggerBar(window));

    return window;
}
```

##### 新增 `GetTriggerBar(Window)`

```csharp
private static DockTriggerBar GetTriggerBar(Window window) =>
    window switch
    {
        TransparentMusicWindow tw => tw.TriggerBar,
        AcrylicMusicWindow aw => aw.TriggerBar,
        _ => throw new InvalidOperationException("Unknown window type")
    };
```

##### 修改窗口切换方法（`ToggleBlur` / `SetSizeMode`）

在关闭旧窗口**之前**，Service 状态已保存（因为 Service 独立于 Window）。创建新窗口后调用 `Attach()` 自动应用状态。

关闭旧窗口时调用 `_dockService.Detach()`：

```csharp
// ToggleBlur / SetSizeMode 中：
_activeWindow.LocationChanged -= OnWindowMoved;
_dockService.Detach();  // ← 新增：解绑旧窗口事件

// ... 创建新窗口 ...
// CreateWindow() 内部已调用 _dockService.Attach()
```

##### 新增拖拽完成处理

Manager 已有的 `OnWindowMoved` 在拖拽过程中高频触发。`DragMove()` 返回后需要调用 `CheckEdgeDock`。

方案：在 `Attach()` 中订阅各窗口的 `DragMoveCompleted` 事件：

```csharp
// Attach 方法中：
if (window is AcrylicMusicWindow aw)
    aw.DragMoveCompleted += OnWindowDragMoveCompleted;
else if (window is TransparentMusicWindow tw)
    tw.DragMoveCompleted += OnWindowDragMoveCompleted;

// 触发条拖拽
GetTriggerBar(window).DragRequested += OnTriggerBarDragRequested;

// 触发条悬停
GetTriggerBar(window).MouseEnter += (_, _) => _dockService.Expand();
```

```csharp
private void OnWindowDragMoveCompleted(object? sender, EventArgs e)
{
    _dockService.OnDragCompleted();
}

private void OnTriggerBarDragRequested(object? sender, EventArgs e)
{
    // 从 Docked 状态展开 → 然后用户拖拽到新位置
    _dockService.Expand();
    // 注意：Expand() 是异步动画，DragMove 应在动画完成后触发
    // 简化方案：如果当前 Docked，先展开到原位置，然后在 DragMoveCompleted 中重新检测
}
```

> **触发条拖拽的时序问题**：`Expand()` 有 250ms 动画，而 DragMove 是同步阻塞的。两种处理方式：
> （A）接受拖拽时窗口先闪现回原位置再被拖走 → 简单但体验差
> （B）拖拽触发条时不先 Expand，而是直接结束 Docked 状态，让窗口跳到 SavedLeft/SavedTop，然后立即 DragMove → 更好
>
> **推荐方案 B**：`OnTriggerBarDragRequested` 中直接 `_dockService.DetachFromDock()` 把窗口瞬移到保存位置并设为 Free，然后窗口自己的 `OnDragRequested` 会调用 `DragMove()`，松手后 `OnDragCompleted` 重新检测。

##### 全局开关处理

```csharp
// 在 AudioflowSettings.PropertyChanged 处理中加入：
case nameof(AudioflowSettings.EdgeDockEnabled):
    _dockService.Enabled = AudioflowSettings.Instance.EdgeDockEnabled;
    if (!_dockService.Enabled)
    {
        // 关闭贴边 → 如果当前 Docked，强制展开
        _dockService.ForceExpand();
    }
    break;
```

##### `ForceExpand()` in EdgeDockService

```csharp
public void ForceExpand()
{
    _collapseTimer?.Stop();
    if (_isAnimating) return;

    if (State == DockState.Docked || State == DockState.Docking)
    {
        _isAnimating = false;
        // 瞬移到保存位置，无动画
        if (_window != null)
        {
            _window.Left = SavedLeft;
            _window.Top = SavedTop;
        }
        if (_triggerBar != null) _triggerBar.Visibility = Visibility.Collapsed;
        if (_content != null) _content.Visibility = Visibility.Visible;
        State = DockState.Free;
    }
}
```

---

#### `Tools/NeteaseMusicTool.cs`（改 ~15 行）

在设置卡片中加一个 CheckBox："贴边自动缩入"

```csharp
// 紧跟 cbLock 之后
var cbEdgeDock = new CheckBox
{
    Style = FindResourceStyle("ClassicCheckBoxStyle"),
    Content = "贴边自动缩入",
    Margin = new Thickness(0, 0, 0, 0)
};
cbEdgeDock.SetBinding(ToggleButton.IsCheckedProperty,
    new System.Windows.Data.Binding("EdgeDockEnabled")
    {
        Source = AudioflowSettings.Instance,
        Mode = System.Windows.Data.BindingMode.TwoWay
    });
settingsPanel.Children.Add(cbEdgeDock);
```

在 `_settingsHandler` 中新增：

```csharp
case nameof(AudioflowSettings.EdgeDockEnabled):
    mgr.DockService.Enabled = AudioflowSettings.Instance.EdgeDockEnabled;
    if (!mgr.DockService.Enabled)
        mgr.DockService.ForceExpand();
    break;
```

**注意**：`MusicFloatWindowManager.DockService` 需要改为 public 或 internal。

---

## 4. 文件清单

| 操作 | 文件 | 预估行数 |
|------|------|----------|
| **新建** | `Controls/DockTriggerBar.xaml` | ~50 |
| **新建** | `Controls/DockTriggerBar.xaml.cs` | ~80 |
| **新建** | `Tools/Services/EdgeDockService.cs` | ~220 |
| **修改** | `Services/AudioflowSettings.cs` | +20 |
| **修改** | `Views/AcrylicMusicWindow.xaml` | +10 |
| **修改** | `Views/AcrylicMusicWindow.xaml.cs` | +20 |
| **修改** | `Views/TransparentMusicWindow.xaml` | +10 |
| **修改** | `Views/TransparentMusicWindow.xaml.cs` | +20 |
| **修改** | `Services/MusicFloatWindowManager.cs` | +50 |
| **修改** | `Tools/NeteaseMusicTool.cs` | +15 |
| **总计** | 3 新建 + 7 修改 = 10 文件 | ~500 行 |

---

## 5. 执行顺序

按依赖关系排序，每步完成后编译验证：

| 步骤 | 内容 | 验证方式 |
|------|------|----------|
| 1 | `AudioflowSettings.cs` — 加 `EdgeDockEnabled` 字段 + 序列化 | 编译通过 |
| 2 | `EdgeDockService.cs` — 状态机 + 动画引擎（完整实现，含 `Attach/Detach` 骨架） | 编译通过 |
| 3 | `DockTriggerBar.xaml(.cs)` — 梯形 UI + 箭头 + 方向切换 | 编译通过 |
| 4 | `AcrylicMusicWindow.xaml(.cs)` + `TransparentMusicWindow.xaml(.cs)` — 嵌入 DockTriggerBar + DragMoveCompleted 事件 | 编译通过 |
| 5 | `MusicFloatWindowManager.cs` — 集成 EdgeDockService 生命周期 + 拖拽/悬停事件桥接 | 编译 + 手动拖拽测试 |
| 6 | `NeteaseMusicTool.cs` — 面板开关 UI | 编译 + 开关测试 |

---

## 6. 边界情况与处理

| 场景 | 处理方式 |
|------|----------|
| **多显示器** | `Screen.FromHandle()` 获取窗口当前所在屏的 `WorkingArea`，避免用 `PrimaryScreen` |
| **DPI 缩放** | WPF 使用设备无关像素（DIP），`System.Windows.Forms.Screen.WorkingArea` 返回物理像素 → 需除以 DPI 缩放比转换 |
| **窗口替换（大小模式 / 毛玻璃切换）** | `EdgeDockService` 独立于 Window，`Detach` 旧窗口 → `CreateWindow` → `Attach` 新窗口 → 自动恢复状态 |
| **快速拖拽触发条** | `_isAnimating` 锁防止重入；`ForceExpand` 不经过动画直接跳转 |
| **缩回动画中鼠标重新进入** | `Expand()` 入口检查 `_isAnimating` → 等待当前动画完成后再处理；实际场景极少，因为 Docking 动画只有 250ms |
| **关闭悬浮窗** | `Hide()` → `Detach()` 解绑事件；再次 `Show()` → `CreateWindow()` → `Attach()` 重新绑定 |
| **锁定窗口后拖拽触发条** | 与 MusicContent 一致，检查 `_isLocked`，锁定时不响应拖拽 |
| **贴边开关关闭时处于 Docked** | `PropertyChanged` 监听 → `ForceExpand()` 瞬移到保存位置 |
| **MusicFloatWindow 旧窗口路径** | 不实现贴边功能（旧窗口已标记为兼容保留） |

---

## 7. DPI 转换要点

`System.Windows.Forms.Screen.WorkingArea` 返回物理像素坐标，WPF 窗口 `Left/Top` 使用 DIP（设备无关像素）。转换公式：

```csharp
var source = PresentationSource.FromVisual(_window);
double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

// 物理像素 → DIP
double waLeft = screen.WorkingArea.Left / dpiScaleX;
double waRight = screen.WorkingArea.Right / dpiScaleX;

// DIP → 物理像素（设置 Window.Left 时不需要，WPF 自动处理）
```

此转换在 `OnDragCompleted()` 中执行。

---

## 8. 触发条梯形绘制（StreamGeometry）

以 Left-dock 为例（窄边在左，宽边在右，从 3px 渐变到 14px）：

```
        ┌──┐ ← 上边 3px，圆角半径 2
        │  │
        │  │ ← 右侧主体 14px 高
        │  │
        └──┘ ← 下边 14px，圆角半径 2
```

Right-dock 镜像翻转。

使用 `StreamGeometry` + `StreamGeometryContext` 手绘，避免 XAML Path mini-language 性能开销。箭头用 `Polygon` 或 `Path` 简单三角形。

---

## 9. 与现有功能的交互测试清单

- [ ] 贴边缩入 → 毛玻璃开关切换 → 窗口应保持 Docked 状态
- [ ] 贴边缩入 → 大小模式切换 → 窗口应保持 Docked 状态
- [ ] Docked + 暂停歌曲 → 封面缩放 0.9 → 悬停展开后封面应为 0.9（缩放状态保持）
- [ ] Docked + 切歌 → 展开后封面应显示新封面
- [ ] 贴边开关关闭 → Docked 窗口展开到保存位置
- [ ] 锁定窗口 → 触发条也不可拖动
- [ ] 拖拽窗口到非边缘位置 → 正常停止，不触发贴边
- [ ] 缩回动画中（250ms）鼠标再次进入窗口 → 展开（等待动画完成或中断动画）
