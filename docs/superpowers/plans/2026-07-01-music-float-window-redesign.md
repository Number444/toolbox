# 网易云音乐悬浮窗 UI 重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 精简悬浮窗——删除所有按钮/进度条，仅保留封面+歌名+歌手，底层完全透明，封面圆角裁切，尺寸缩小至 90%，歌曲切换时带淡入淡出滑入动画，清理 Release 构造产物只保留 Debug。

**Architecture:** 悬浮窗 `MusicFloatWindow` 是一个独立的 WPF `Window`，通过 `SMTCListener` 获取网易云音乐播放状态。此次重构仅修改 XAML 布局和 code-behind 逻辑，不涉及 `SMTCListener`、`NowPlayingInfo` 模型、`NeteaseMusicTool` 主窗口面板。动画使用 WPF `Storyboard` + `TranslateTransform` + `DoubleAnimation`。

**Tech Stack:** .NET 9, WPF, xUnit, C#, Windows SMTC API

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml` | 修改 | 删除按钮+进度条，重构为封面+文字布局，透明背景 |
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs` | 修改 | 删除进度/按钮逻辑，新增歌曲切换动画、检测逻辑 |
| `Toolbox.Tests/NeteaseMusicToolTests.cs` | 修改 | 新增歌曲切换动画触发条件测试、窗口透明度属性测试 |
| `Toolbox.sln` | 修改 | 移除 Release 配置项 |
| `bin/Release/` | 删除 | 清理 Release 构造产物 |
| `**/obj/Release/` | 删除 | 清理各项目 Release 中间产物 |

**不修改的文件（无需改动）：**
- `NowPlayingInfo.cs` — 模型属性保留（Position/Duration/ProgressText 仍被 SMTCListener 和测试使用）
- `SMTCListener.cs` — 监听逻辑不变
- `NeteaseMusicTool.cs` — `CreateContent()` 返回主窗口面板，与悬浮窗无关
- `Toolbox.Core` 项目全部文件

---

### Task 1: 清理 Release 构造产物

**Files:**
- Delete: `D:\Agent Space\Toolbox\bin\Release\`
- Delete: `D:\Agent Space\Toolbox\Toolbox.Core\obj\Release\`
- Delete: `D:\Agent Space\Toolbox\Toolbox.Plugins\obj\Release\`
- Delete: `D:\Agent Space\Toolbox\Toolbox.Tests\obj\Release\`
- Delete: `D:\Agent Space\Toolbox\obj\Release\`
- Modify: `D:\Agent Space\Toolbox\Toolbox.sln`

- [ ] **Step 1: 删除 Release 二进制输出目录**

From the solution root, delete the Release bin output:

```powershell
Remove-Item -Recurse -Force "D:\Agent Space\Toolbox\bin\Release" -ErrorAction SilentlyContinue
```

- [ ] **Step 2: 删除各项目 Release 中间产物**

```powershell
$projects = @(
  "D:\Agent Space\Toolbox\Toolbox.Core\obj\Release",
  "D:\Agent Space\Toolbox\Toolbox.Plugins\obj\Release",
  "D:\Agent Space\Toolbox\Toolbox.Tests\obj\Release",
  "D:\Agent Space\Toolbox\obj\Release"
)
foreach ($p in $projects) {
  Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue
}
```

- [ ] **Step 3: 从 .sln 中移除 Release 配置项**

Read `D:\Agent Space\Toolbox\Toolbox.sln` first, then remove the Release blocks.
Delete the 3 Release lines from `GlobalSection(SolutionConfigurationPlatforms)`:

```
		Release|Any CPU = Release|Any CPU
		Release|x64 = Release|x64
		Release|x86 = Release|x86
```

Delete all Release mappings from `GlobalSection(ProjectConfigurationPlatforms)` — for each of 4 projects, remove 6 lines each (24 lines total). Pattern per project:
```
		{GUID}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{GUID}.Release|Any CPU.Build.0 = Release|Any CPU
		{GUID}.Release|x64.ActiveCfg = Release|Any CPU
		{GUID}.Release|x64.Build.0 = Release|Any CPU
		{GUID}.Release|x86.ActiveCfg = Release|Any CPU
		{GUID}.Release|x86.Build.0 = Release|Any CPU
```

- [ ] **Step 4: 验证 Debug 构建仍正常**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: `0 个警告, 0 个错误`, 构建成功。

- [ ] **Step 5: Commit**

```bash
git add Toolbox.sln
git commit -m "chore: remove Release configurations, keep Debug only"
```

---

### Task 2: TDD RED — 写动画触发条件测试（歌曲切换检测）

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Tests\NeteaseMusicToolTests.cs` (append)

**背景：** 动画应在歌曲真正切换时触发（Title 或 Artist 变化），而非 SMTC 每次 `MediaPropertiesChanged` 或 `TimelinePropertiesChanged` 事件都触发。需要新增测试验证 `IsSongChanged` 判断逻辑。

- [ ] **Step 1: 写失败测试 — 新增 `NowPlayingInfo_IsSongChanged_DetectsTitleChange`**

在 `NeteaseMusicToolTests.cs` 的 `NeteaseMusicTool_CreateContent_ReturnsPanel` 测试之后添加：

```csharp
    [Fact]
    public void NowPlayingInfo_IsSongChanged_DetectsTitleChange()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "七里香", Artist = "周杰伦" };
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Different title should be detected as song change");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_DetectsArtistChange()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "林俊杰" };
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Different artist should be detected as song change");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_ReturnsFalse_WhenSameSong()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        Assert.False(NowPlayingInfo.IsSongChanged(previous, current),
            "Same title and artist should not be a song change");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_ReturnsTrue_WhenPreviousWasEmpty()
    {
        var previous = new NowPlayingInfo();
        var current = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Transition from no-song to a song should be detected");
    }

    [Fact]
    public void NowPlayingInfo_IsSongChanged_ReturnsTrue_WhenCurrentIsEmpty()
    {
        var previous = new NowPlayingInfo { Title = "晴天", Artist = "周杰伦" };
        var current = new NowPlayingInfo();
        Assert.True(NowPlayingInfo.IsSongChanged(previous, current),
            "Transition from a song to no-song should be detected");
    }
```

- [ ] **Step 2: 运行测试验证失败**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "IsSongChanged" 2>&1
```

Expected: 5 个测试全部失败，编译错误 `'NowPlayingInfo' does not contain a definition for 'IsSongChanged'`。

- [ ] **Step 3: Commit**（RED 完成）

```bash
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "test: add failing IsSongChanged tests for song switch detection"
```

---

### Task 3: TDD GREEN — 实现 `NowPlayingInfo.IsSongChanged` 静态方法

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Models\NowPlayingInfo.cs`

- [ ] **Step 1: 实现最小通过的代码**

在 `NowPlayingInfo` 类末尾（`}` 闭合之前）添加：

```csharp
    /// <summary>
    /// 判断两次播放信息是否代表不同的歌曲（Title 或 Artist 变更则为切换）。
    /// 任一为 null 视为发生了切换。
    /// </summary>
    public static bool IsSongChanged(NowPlayingInfo? prev, NowPlayingInfo? current)
    {
        if (prev == null || current == null) return true;
        return !string.Equals(prev.Title, current.Title, StringComparison.Ordinal)
            || !string.Equals(prev.Artist, current.Artist, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: 运行测试验证通过**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "IsSongChanged" 2>&1
```

Expected: 5 个测试全部 PASS。

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Plugins/Tools/Models/NowPlayingInfo.cs
git commit -m "feat: add IsSongChanged static method to detect song transitions"
```

---

### Task 4: TDD RED — 写悬浮窗透明背景测试

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Tests\NeteaseMusicToolTests.cs` (append)

- [ ] **Step 1: 写测试 — 验证窗口 `AllowsTransparency` 和 `Background` 属性**

在测试文件末尾（`}` 闭合之前）添加：

```csharp
    [Fact]
    public void MusicFloatWindow_HasTransparentBackground()
    {
        Exception? threadException = null;
        var result = false;
        var thread = new Thread(() =>
        {
            try
            {
                var window = Toolbox.Tools.Views.MusicFloatWindow.Instance;
                result = window.AllowsTransparency
                    && window.Background == System.Windows.Media.Brushes.Transparent;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.True(result, "Window should have transparent background");
    }
```

当前 XAML 已设置 `Background="Transparent"` 和 `AllowsTransparency="True"`，但有一个内层 `Border Background="#2D2D2D"` 覆盖了透明效果。这个测试验证 Window 属性本身是否透明，应该 PASS（Window 本身已经透明了）。但若 Border 仍设为不透明，后续 Task 会修正。

注意：当前这个测试可能直接 PASS（Window 的背景属性已经正确）。如果它直接通过，说明该属性的 TDD RED 无失败可捕获——改为只保留测试作为回归保护，跳过 RED 阶段直接确认通过。

- [ ] **Step 2: 运行测试确认**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "MusicFloatWindow_HasTransparentBackground" 2>&1
```

若直接 PASS，接受它作为回归测试。若 FAIL，记录失败原因。

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "test: add transparent background regression test for MusicFloatWindow"
```

---

### Task 5: 重构 MusicFloatWindow.xaml — 精简布局

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml`

**变更内容：**
1. 删除控制按钮区域（`BtnPrevious`、`BtnPlayPause`、`BtnNext` 所在的 `StackPanel`）
2. 删除进度条区域（`ProgressCurrent`、`ProgressBar`、`ProgressTotal`）
3. 删除拖拽标题栏（`"网易云音乐"` 的 `Border` + `TextBlock`，保留拖拽功能在窗口空白区域）
4. 删除外层 `Border` 的背景色和边框（移除 `Background="#2D2D2D"`、`BorderBrush="#444"`、`BorderThickness="1"`）
5. 封面 `Border` 保留圆角但移除背景色
6. 窗口尺寸改为当前 90%：`Width="252"`、`Height="342"`
7. 添加 `RenderTransform` 用于动画

- [ ] **Step 1: 运行当前测试确保基线通过**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 24 PASS (19 原有 + 5 新增)。

- [ ] **Step 2: 替换 XAML 为精简版本**

用以下完整内容替换整个 `MusicFloatWindow.xaml`：

```xml
<Window x:Class="Toolbox.Tools.Views.MusicFloatWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="网易云音乐"
        Width="252"
        Height="282"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        ShowInTaskbar="False"
        Topmost="True"
        ResizeMode="NoResize"
        WindowStartupLocation="Manual"
        MouseLeftButtonDown="OnDragAreaMouseDown">
    <Grid x:Name="ContentRoot">
        <Grid.RenderTransform>
            <TranslateTransform x:Name="SlideTransform" X="0" />
        </Grid.RenderTransform>
        <StackPanel Opacity="1">
            <StackPanel.RenderTransform>
                <TranslateTransform x:Name="PanelTranslate" X="0" />
            </StackPanel.RenderTransform>

            <!-- 封面（圆角裁切） -->
            <Border x:Name="CoverContainer"
                    Width="180"
                    Height="180"
                    CornerRadius="10"
                    HorizontalAlignment="Center"
                    Margin="0,12,0,0">
                <Border.Clip>
                    <RectangleGeometry RadiusX="10" RadiusY="10"
                        Rect="0,0,180,180" />
                </Border.Clip>
                <Image x:Name="CoverImage"
                       Width="180"
                       Height="180"
                       Stretch="UniformToFill" />
            </Border>

            <!-- 歌曲标题 -->
            <TextBlock x:Name="SongTitle"
                       Text="未在播放"
                       Foreground="#FFFFFF"
                       FontSize="15"
                       FontWeight="Bold"
                       TextAlignment="Center"
                       TextTrimming="CharacterEllipsis"
                       Margin="0,10,0,2">
                <TextBlock.Effect>
                    <DropShadowEffect BlurRadius="6"
                        ShadowDepth="1"
                        Opacity="0.6"
                        Color="Black" />
                </TextBlock.Effect>
            </TextBlock>

            <!-- 歌手 -->
            <TextBlock x:Name="SongArtist"
                       Text="—"
                       Foreground="#CCCCCC"
                       FontSize="12"
                       TextAlignment="Center"
                       TextTrimming="CharacterEllipsis"
                       Margin="0,2,0,12">
                <TextBlock.Effect>
                    <DropShadowEffect BlurRadius="5"
                        ShadowDepth="1"
                        Opacity="0.5"
                        Color="Black" />
                </TextBlock.Effect>
            </TextBlock>
        </StackPanel>
    </Grid>
</Window>
```

关键变更说明：
- `Width="252"` / `Height="282"`（原 `280` → 252，原 `380` 减去按钮+进度条+标题栏后约 282）
- 移除外层 `Border` 的深色背景 → 真正透明
- 封面 `Border` 保留 `CornerRadius="10"`，用 `Clip` 进行裁切（图片不会超出圆角边界）
- 文字添加 `DropShadowEffect`，透明背景下保证可读性
- `MouseLeftButtonDown="OnDragAreaMouseDown"` 移到 Window 级别，整个窗口可拖拽
- `Grid.RenderTransform` + `StackPanel.RenderTransform` 为动画层预留，Task 7 中启用

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml
git commit -m "refactor: simplify MusicFloatWindow layout — remove buttons, progress bar, transparent background"
```

---

### Task 6: 重构 MusicFloatWindow.xaml.cs — 精简 code-behind

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`

**变更内容：**
1. 删除所有进度定时器相关代码（`_progressTimer`、`_lastPositionUpdate`、`_lastKnownPosition`、`_duration`、`OnProgressTimerTick`、`UpdateProgressDisplay`、`FormatTimeSpan`）
2. 删除控制按钮事件处理器（`OnPlayPauseClick`、`OnPreviousClick`、`OnNextClick`）
3. 删除不再使用的 `using` 指令（`System.Threading.Tasks`、`System.Windows.Input`、`System.Windows.Media.Imaging`、`System.Windows.Threading`）
4. `OnNowPlayingChanged` 简化为仅更新文本和触发封面加载
5. `OnDragAreaMouseDown` 保留（现在绑定到 Window 级别了）
6. 添加 `_previousInfo` 字段，用于歌曲切换检测和第 Task 7 的动画触发器

- [ ] **Step 1: 先运行测试验证当前基线**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 25 PASS (24 原有 + 1 新增透明度测试)。

- [ ] **Step 2: 替换 code-behind 为精简版本**

用以下完整内容替换整个 `MusicFloatWindow.xaml.cs`：

```csharp
using System;
using System.Windows;
using System.Windows.Media;
using Toolbox.Tools.Models;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 网易云音乐实时信息悬浮窗。
/// 单例模式，通过 Instance 属性获取唯一实例。
/// 悬浮窗固定在桌面左侧，置顶显示，支持拖拽移动。
/// 精简版：仅封面 + 歌名 + 歌手，完全透明背景。
/// </summary>
public partial class MusicFloatWindow : Window
{
    private static MusicFloatWindow? _instance;
    private static readonly object _lock = new();

    private readonly SMTCListener _listener = new();
    private NowPlayingInfo _previousInfo = new();

    private MusicFloatWindow()
    {
        InitializeComponent();

        _listener.NowPlayingChanged += OnNowPlayingChanged;

        Loaded += (s, e) =>
        {
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = 0;
            Top = (screenHeight - Height) / 2;
        };

        if (Application.Current.MainWindow != null)
        {
            Application.Current.MainWindow.Closed += (s, e) => Close();
        }
    }

    public static MusicFloatWindow Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new MusicFloatWindow();
                }
            }
            return _instance;
        }
    }

    public new void Show()
    {
        if (!_listener.IsListening)
        {
            _ = _listener.StartAsync();
        }
        base.Show();
    }

    public new void Hide()
    {
        base.Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        base.OnClosed(e);
    }

    private void OnNowPlayingChanged(object? sender, NowPlayingInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            var isNewSong = NowPlayingInfo.IsSongChanged(_previousInfo, info);
            _previousInfo = info;

            if (isNewSong)
            {
                PlaySongSwitchAnimation(() =>
                {
                    SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
                    SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
                    _ = LoadCoverAsync();
                });
            }
            else
            {
                // Same song, just update silently (e.g. position changed)
            }
        });
    }

    private async System.Threading.Tasks.Task LoadCoverAsync()
    {
        try
        {
            var bitmap = await _listener.GetThumbnailAsync();
            if (bitmap != null)
            {
                CoverImage.Source = bitmap;
            }
        }
        catch
        {
            // 封面加载失败时保持现有封面
        }
    }

    private void OnDragAreaMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    // 动画方法在 Task 7 中实现 — 此处仅预留调用点
    private void PlaySongSwitchAnimation(Action onMidpoint)
    {
        // Step 1: Slide out to left (placeholder, Task 7 实现)
        // Step 2: Swap content via onMidpoint callback
        onMidpoint();
        // Step 3: Slide in from right (placeholder, Task 7 实现)
    }
}
```

注意：
- 动画方法 `PlaySongSwitchAnimation` 在此 Task 仅预留骨架调用（调用 `onMidpoint()` 直接执行内容更新），无实际动画。动画实现见 Task 7。
- 删除了全部进度相关代码和按钮事件。
- `_previousInfo` 初始化为 `new NowPlayingInfo()`（Title=""），首次有歌曲时 `IsSongChanged` 返回 true，触发"首次出现"动画。
- 仅同歌进度更新时不触发动画（`isNewSong == false` 分支为空）。

- [ ] **Step 3: 构建并运行测试验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 0 警告, 0 错误；25 个测试全部 PASS。

- [ ] **Step 4: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git commit -m "refactor: simplify MusicFloatWindow code-behind — remove progress/button logic, add song change detection"
```

---

### Task 7: 实现歌曲切换动画（淡出左滑 + 淡入右滑）

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs` (replace `PlaySongSwitchAnimation` method)

**动画时序（总时长 ~500ms）：**
1. 当前内容淡出 + 向左滑出：`Opacity 1→0`，`TranslateX 0→-35`，持续时间 200ms
2. 动画中点：调用 `onMidpoint` 更新封面/文字
3. 新内容从右侧移至原位：起始 `Opacity=0, TranslateX=+35`，动画至 `Opacity=1, TranslateX=0`，持续时间 200ms

使用 `Storyboard` + `DoubleAnimation` 实现（WPF 原生动画框架，无需第三方库）。

- [ ] **Step 1: 替换 `PlaySongSwitchAnimation` 方法实现**

将 `MusicFloatWindow.xaml.cs` 中的 `PlaySongSwitchAnimation` 方法替换为：

```csharp
    private void PlaySongSwitchAnimation(Action onMidpoint)
    {
        var panel = (System.Windows.Controls.StackPanel)ContentRoot.Children[0];

        // ── Phase 1: 淡出左滑（200ms）──
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        var slideLeft = new System.Windows.Media.Animation.DoubleAnimation(0, -35, TimeSpan.FromMilliseconds(200));

        var storyboard = new System.Windows.Media.Animation.Storyboard();
        System.Windows.Media.Animation.Storyboard.SetTarget(fadeOut, panel);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeOut,
            new PropertyPath(System.Windows.UIElement.OpacityProperty));
        System.Windows.Media.Animation.Storyboard.SetTarget(slideLeft, PanelTranslate);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideLeft,
            new PropertyPath(TranslateTransform.XProperty));
        storyboard.Children.Add(fadeOut);
        storyboard.Children.Add(slideLeft);

        storyboard.Completed += (_, _) =>
        {
            // ── 中点：更新内容 ──
            onMidpoint();

            // ── Phase 2: 从右侧淡入（200ms）──
            panel.Opacity = 0;
            PanelTranslate.X = 35;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideRight = new System.Windows.Media.Animation.DoubleAnimation(35, 0, TimeSpan.FromMilliseconds(200));

            var storyboard2 = new System.Windows.Media.Animation.Storyboard();
            System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, panel);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn,
                new PropertyPath(System.Windows.UIElement.OpacityProperty));
            System.Windows.Media.Animation.Storyboard.SetTarget(slideRight, PanelTranslate);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideRight,
                new PropertyPath(TranslateTransform.XProperty));
            storyboard2.Children.Add(fadeIn);
            storyboard2.Children.Add(slideRight);

            storyboard2.Begin();
        };

        storyboard.Begin();
    }
```

注意：此方法不直接测试（WPF Storyboard 动画在单元测试中无法可靠验证），任务 2-3 中的 `IsSongChanged` 测试已覆盖动画触发条件。动画的最终验收方式为手动运行悬浮窗观察效果。

- [ ] **Step 2: 构建并运行测试验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 0 警告, 0 错误；25 个测试全部 PASS。

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git commit -m "feat: add song switch slide animation (fade left out, slide right in)"
```

---

### Task 8: 全量构建 + 全部测试验证（收尾）

**Files:** 无修改

- [ ] **Step 1: clean + 全量构建**

```powershell
dotnet clean "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: 0 警告, 0 错误。

- [ ] **Step 2: 运行全部测试**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" 2>&1
```

Expected: 25 个测试全部 PASS。

- [ ] **Step 3: 确认 bin 目录仅有 Debug**

```powershell
Get-ChildItem "D:\Agent Space\Toolbox\bin" -Directory | Select-Object Name
```

Expected: 仅输出 `Debug`，无 `Release`。

- [ ] **Step 4: Commit**

```bash
git commit -m "chore: final verification — all tests pass, Debug-only build clean"
```

---

## 可行性分析总结

| 需求 | 可行性 | 风险等级 | 说明 |
|------|--------|----------|------|
| 删除按钮 | ✅ 可行 | 低 | 仅删除 XAML 元素 + C# 事件处理器，SMTCListener 不受影响 |
| 删除进度条 | ✅ 可行 | 低 | 移除 UI 控件 + 进度定时器，`NowPlayingInfo` 模型属性保留供测试使用 |
| 完全透明背景 | ✅ 可行 | 低 | Window 已设 `AllowsTransparency="True"`，仅需移除外层 Border 背景色 |
| 封面圆角裁切 | ✅ 可行 | 低 | 使用 `Border.Clip` + `RectangleGeometry` 裁切，标准 WPF 技术 |
| 尺寸缩小至 90% | ✅ 可行 | 低 | 直接修改 Width/Height 数值 |
| 歌曲切换动画 | ✅ 可行 | 低 | WPF Storyboard + DoubleAnimation，成熟框架，无需第三方库 |
| Release 清理 | ✅ 可行 | 低 | 删除目录 + 精简 .sln 配置项 |

**不涉及的变更（无回归风险）：**
- `SMTCListener.cs` — 无改动
- `NowPlayingInfo.cs` — 仅新增 `IsSongChanged` 静态方法，无 breaking change
- `NeteaseMusicTool.cs` — `CreateContent()` 无改动，主窗口面板不变
- `Toolbox.Core` 项目全部文件 — 无改动

**测试影响：**
- 新增 6 个测试（5 个 `IsSongChanged` + 1 个透明背景验证）
- 原有 19 个测试不受影响
- 动画行为无法在单元测试中验证（接受为手动验收项）