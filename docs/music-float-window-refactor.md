# 音乐悬浮窗重构方案：透明/毛玻璃 四窗口架构

## 目标

通过 **窗口实例替换**代替**单个窗口内动态开关 DWM 效果**，彻底解决 WindowChrome 方案下毛玻璃 OFF→ON 失效及关闭后无法纯透明的问题。

## 架构总览

```
MusicFloatWindowManager（单例，共享 SMTCListener，管理窗口切换）
    │
    ├── TransparentMusicWindow     ← AllowsTransparency=True，纯透明
    │       └── MusicContentControl（封面/歌名/歌手/大小模式）
    │
    └── AcrylicMusicWindow         ← WindowChrome + DWM Acrylic，毛玻璃
            └── MusicContentControl（同上，复用）
```

- **MusicContentControl**（UserControl）：从当前 MusicFloatWindow 提取所有内容逻辑（封面渲染、歌名跑马灯、大小模式切换、切歌动画）
- **TransparentMusicWindow**（Window）：`AllowsTransparency=True`，无遮罩层，无 DWM 代码
- **AcrylicMusicWindow**（Window）：`WindowChrome` + `OpacityOverlay` + `AcrylicTintOverlay` + DWM Acrylic 初始化
- **MusicFloatWindowManager**（单例）：共享 SMTCListener，管理模糊窗口切换、大小模式切换、状态传递

## 切换流程

```
毛玻璃 ON → OFF:
  1. Manager 从当前 AcrylicWindow 提取状态（位置、SizeMode、锁定、歌曲信息、滚动offset）
  2. Manager 创建 TransparentMusicWindow，注入 ContentControl（注入状态）
  3. 新窗口 Show() 定位，旧窗口 Hide() + Dispose()
  4. SMTCListener 事件重新绑定到新窗口的 ContentControl

毛玻璃 OFF → ON:
  同上，方向相反。

大小模式切换（Large ↔ Compact）:
  委托给当前 active 窗口的 MusicContentControl.ApplySizeMode()
```

## 文件变更清单

### 新增文件

| 文件 | 说明 |
|------|------|
| `Toolbox.Plugins/Controls/MusicContentControl.xaml` | 提取自 MusicFloatWindow.xaml 的内容部分 |
| `Toolbox.Plugins/Controls/MusicContentControl.xaml.cs` | 内容逻辑（封面、歌名跑马灯、大小模式、切歌动画、布局迁移） |
| `Toolbox.Plugins/Tools/Views/TransparentMusicWindow.xaml` | 纯透明窗口 |
| `Toolbox.Plugins/Tools/Views/TransparentMusicWindow.xaml.cs` | 纯透明窗口逻辑（极简，仅封装窗口 API） |
| `Toolbox.Plugins/Tools/Views/AcrylicMusicWindow.xaml` | 毛玻璃窗口 |
| `Toolbox.Plugins/Tools/Views/AcrylicMusicWindow.xaml.cs` | 毛玻璃窗口逻辑（DWM Acrylic 初始化） |
| `Toolbox.Plugins/Tools/Services/MusicFloatWindowManager.cs` | 管理器单例 |

### 修改文件

| 文件 | 变更 |
|------|------|
| `MusicFloatWindow.xaml` | 保留为向后兼容（可能废弃），去除内容 |
| `MusicFloatWindow.xaml.cs` | 同上去除内容逻辑，保留 ForceResetInstance |
| `NeteaseMusicTool.cs` | 引用改为 Manager，SizeMode/FloatWindow 枚举可能迁移 |
| `MainWindow.xaml.cs` | 引用改为 Manager |
| `Toolbox.Plugins.csproj` | 确保新文件包含在项目中 |
| `Toolbox.Tests` 相关测试 | 更新到新架构 |

### 删除内容

- MusicFloatWindow 中的 DWM Acrylic 代码（迁移至 AcrylicMusicWindow）
- MusicFloatWindow 中的 SMTCListener（迁移至 Manager）
- MusicFloatWindow 中的 SetBlurEnabled / SetWindowOpacity（Manager 接管）

## 详细设计

### 1. MusicContentControl（UserControl）

**XAML**（从 MusicFloatWindow.xaml 的 Grid 中提取）：
- ContentRoot → 保留为根 Grid
- ContentPanel → StackPanel（内容容器）
- LayoutLarge + CoverGrid + TitleCanvas + SongArtist → 大模式
- LayoutCompact + CompactCoverSlot + CompactTextSlot → 紧凑模式
- CoverImage, CoverImageBack → 双层交叉淡入

不包含的 XAML 元素：
- OpacityOverlay → 保留在 AcrylicMusicWindow
- AcrylicTintOverlay → 保留在 AcrylicMusicWindow

**CS 核心方法**（从 MusicFloatWindow.xaml.cs 迁移）：
```
// 歌曲
PlaySongSwitchAnimation(Action onMidpoint, Action? onPhase2Complete)
ApplySongInfo(NowPlayingInfo info)
LoadCoverFromData(byte[]? thumbnailData)

// 大小模式
ApplySizeMode()
ApplyCoverMetrics(double size, double radius, double blur, double depth)
ApplyLargeMargins()
SetCompactMargins()
ApplyAlignment(bool isLeft)
UpdateWindowSize(double width, double height)  // 返回新窗口尺寸，不直接操作 Window

// 歌名滚动
StartOrStopTitleMarquee()
OnMarqueeTick()
TitleMarquee.NeedsScroll(...)

// 元素迁移
EnsureChildInPanel(Panel, UIElement)
MoveChildToSlot(UIElement, Border)
MoveChildToSlot(UIElement, Panel)
ClearGridAttachedProperties(UIElement[])

// 动画
ResetPanelToVisibleState(StackPanel, Thickness?)
```

**公开属性/事件**：
```csharp
public FloatSizeMode SizeMode { get; set; }        // 大小模式
public event EventHandler? SizeChanged;             // 窗口尺寸变化时触发
public (double width, double height) GetRequiredSize(); // 获取当前模式需要的窗口尺寸
public void UpdateSongInfo(NowPlayingInfo info);     // 外部注入歌曲信息
public void SetAlignment(bool isLeft);              // 窗口位置变化时更新对齐
```

### 2. TransparentMusicWindow

```xml
<Window AllowsTransparency="True"
        Background="Transparent"
        WindowStyle="None"
        ShowInTaskbar="False"
        Topmost="True"
        ResizeMode="NoResize">
    <local:MusicContentControl x:Name="Content" />
</Window>
```

CS 职责：
- 窗口拖拽（委托 Content.MouseLeftButtonDown → DragMove）
- SetWindowLocked(bool)
- 公开 Content 属性（Manager 需要访问 ContentControl）
- 窗口位置恢复/保存

### 3. AcrylicMusicWindow

```xml
<Window Background="Transparent"
        WindowStyle="None"
        ...>
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="0" ResizeBorderThickness="0"
                      GlassFrameThickness="0" CornerRadius="0"
                      NonClientFrameEdges="None" UseAeroCaptionButtons="False"/>
    </WindowChrome.WindowChrome>
    <Grid>
        <!-- 遮罩层：不启用 Acrylic 时覆盖（现在不再需要，但保留以备将来） -->
        <Border x:Name="OpacityOverlay" Background="#731A1A1A" CornerRadius="10"
                HorizontalAlignment="Stretch" VerticalAlignment="Stretch"
                Margin="4" Visibility="Collapsed"/>
        <!-- Acrylic 轻量着色层 -->
        <Border x:Name="AcrylicTintOverlay" Background="#01FFFFFF" CornerRadius="10"
                HorizontalAlignment="Stretch" VerticalAlignment="Stretch"
                Margin="4" IsHitTestVisible="False" Visibility="Visible"/>
        <local:MusicContentControl x:Name="Content"/>
    </Grid>
</Window>
```

CS 职责：
- InitializeBackdropBase() → HwndTarget 透明 + ExtendFrame + DarkMode + Corners
- ApplyBackdropEffect() → SetBackdrop(Acrylic)
- ReapplyHwndTransparency() → 交换链重建后恢复透明
- 窗口拖拽
- SetWindowLocked(bool)
- Loaded 事件中调用 InitializeBackdropBase + ApplyBackdropEffect
- UpdateWindowSize 延迟调用 ReapplyHwndTransparency + ApplyBackdropEffect

### 4. MusicFloatWindowManager（单例）

```csharp
public class MusicFloatWindowManager
{
    // 单例
    public static MusicFloatWindowManager Instance { get; }

    // 当前活跃窗口
    public object ActiveWindow { get; }  // TransparentMusicWindow | AcrylicMusicWindow

    // 共享的 SMTC 监听器（在 Manager 生命周期内只有 1 个）
    private readonly SMTCListener _listener;

    // 歌曲信息缓存（切换窗口时传递给新窗口）
    private NowPlayingInfo _cachedInfo;

    // 操作
    public void Show();                    // 用当前设置创建并显示窗口
    public void Hide();                    // 隐藏当前窗口
    public void Close();                   // 关闭并释放
    public void ToggleBlur(bool enabled);  // 切换透明/毛玻璃
    public void SetSizeMode(FloatSizeMode mode);  // 切换大小模式
    public void SetWindowLocked(bool locked);     // 设置锁定

    // 状态查询
    public bool IsVisible { get; }
    public FloatSizeMode CurrentSizeMode { get; }
}
```

**SMTC 事件处理**：
```
_listener.NowPlayingChanged → 
    缓存歌曲信息 → 
    转发给 activeWindow.Content.UpdateSongInfo(info)
```

**ToggleBlur 流程**：
1. 保存当前窗口状态：位置(Left/Top)、SizeMode、Locked、歌曲信息缓存、滚动offset
2. 构建新窗口（根据 enabled 选 TransparentMusicWindow 或 AcrylicMusicWindow）
3. 注入状态
4. 新窗口.Show() → 旧窗口.Hide() → 旧窗口.Close()（短暂双窗口可见避免闪烁）
5. 新窗口设为 active

**预留扩展方法**：
```csharp
// 后期调整位置和大小预留
public void SetWindowPosition(double left, double top);
public void SetWindowSize(double width, double height);
public void UpdateWindowBounds(double left, double top, double width, double height);
```

### 5. SMTCListener 共享改造

当前：MusicFloatWindow 持有 _listener（private field）
改造后：Manager 持有 _listener（private field），在构造函数中创建

```csharp
public MusicFloatWindowManager()
{
    _listener = new SMTCListener();
    _listener.NowPlayingChanged += OnNowPlayingChanged;
}

private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
{
    _cachedInfo = info;
    if (_activeWindow?.IsLoaded == true)
        _activeWindow.Content.UpdateSongInfo(info);
}
```

## 切换时序

### 场景 1：启动（默认毛玻璃）

```
MainWindow.Loaded
  → MusicFloatWindowManager.Instance.Show()
    → 读取 AudioflowSettings.FloatWindowBlurEnabled (true)
    → 读取 AppSettings.MusicFloatSizeMode
    → new AcrylicMusicWindow(sizeMode)
    → window.Show()
      → window.Loaded → InitializeBackdropBase → ApplyBackdropEffect
    → _listener.StartAsync()
    → _activeWindow = window
```

### 场景 2：毛玻璃 OFF→ON 切换

```
勾选"毛玻璃模糊背景"
  → AudioflowSettings.PropertyChanged
  → Manager.ToggleBlur(true)
    → 保存当前窗口状态
    → new AcrylicMusicWindow(当前SizeMode)
    → window.Show() → Loaded → Acrylic 生效
    → 注入歌曲缓存 → Content.UpdateSongInfo(cached)
    → 旧窗口.Hide() → 旧窗口.Close()
    → _activeWindow = 新窗口
```

### 场景 3：大小模式切换

```
点击模式按钮
  → AppSettings.MusicFloatSizeMode = "Compact"
  → Manager.SetSizeMode(Compact)
    → _activeWindow.Content.SizeMode = Compact
      → ApplySizeMode() → 内容重新布局
      → UpdateWindowSize → 触发 Content.SizeChanged 事件
    → 窗口更新 Width/Height
    → Acrylic 窗口额外：延迟重设 HwndTarget 透明 + 重新应用 Acrylic
```

## 注意事项

1. **跨窗口状态传递**：位置(Left/Top)、SizeMode、Locked、歌曲信息（包括当前歌名、歌手、封面数据、滚动偏移）
2. **SMTC 监听连续性**：Manager 持有 SMTCListener，窗口切换期间不中断，缓存最后一帧
3. **内存**：每次切换创建新窗口，旧窗口 dispose，单例 Manager 长期存在
4. **线程安全**：所有 UI 操作通过 Dispatcher 编组
5. **测试兼容**：保留 ForceResetInstance 或等价方法，更新测试引用
6. **窗口闪烁**：先 Show 新窗口再 Hide 旧窗口（短暂双窗口），用户感知不到切换
