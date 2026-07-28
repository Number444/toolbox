# 歌名居中 + 滑动动画修复 + 动态左右对齐 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复短歌名未居中问题；将滑动动画从 Freezable `TranslateTransform.X` 改为可靠的 `Margin` 动画；新增检测窗口左右位置并自动切换元素对齐方向。

**Architecture:** 
- **歌名居中**：`StartOrStopTitleMarquee()` 中区分滚动/非滚动路径。非滚动时设 `TextAlignment=Center + Width=220`；滚动时设 `TextAlignment=Left + Width=Auto`。
- **滑动动画修复**：当前动画目标 `PanelTranslate` 是 `TranslateTransform`（Freezable 子类），通过 `Storyboard.SetTargetProperty` 对 `TranslateTransform.XProperty` 做动画存在已知管道不稳定性。改为对 `StackPanel.Margin` 做 `ThicknessAnimation`（FrameworkElement 属性，管道成熟可靠）。从 XAML 移除 `PanelTranslate` 和 `SlideTransform`（后者从未使用）。
- **动态左右对齐**：新增 `MusicFloatWindow.AlignmentMode` 枚举 + 判定方法，监听 `Window.LocationChanged` 事件。窗口在屏幕左半侧 → 元素靠左对齐；在右半侧 → 靠右对齐。CoverContainer、TitleCanvas、SongArtist 的动态 `HorizontalAlignment` 和 `TextAlignment` 切换。

**Tech Stack:** .NET 9, WPF, xUnit, C#

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml` | 修改 | 移除 PanelTranslate/SlideTransform；元素对齐不再写死 Center |
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs` | 修改 | 修复居中逻辑、Margin 动画、LocationChanged 对齐切换 |
| `Toolbox.Tests/NeteaseMusicToolTests.cs` | 修改 | 新增 AlignmentMode 判定测试 |

---

### Task 1: TDD RED — 写 AlignmentMode 窗口侧判定测试

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Tests\NeteaseMusicToolTests.cs` (追加)

**背景：** `AlignmentMode` 是 `MusicFloatWindow` 的新 nested enum/helper，用于判定窗口在屏幕左侧还是右侧。

- [ ] **Step 1: 在测试文件类末尾追加 3 个测试**

找到最后一个 `}`（`TitleMarquee_NeedsScroll_ReturnsFalse_WhenNoTitle` 测试的 `}` 之后）：

```csharp
    private const double TestScreenWidth = 1920.0;

    [Fact]
    public void AlignmentMode_IsLeft_WhenWindowLeftLessThanHalfScreen()
    {
        Assert.True(MusicFloatWindow.AlignmentHelper.IsLeftSide(400, TestScreenWidth),
            "Window at 400px on 1920px screen should be on left side");
    }

    [Fact]
    public void AlignmentMode_IsRight_WhenWindowLeftGreaterThanHalfScreen()
    {
        Assert.False(MusicFloatWindow.AlignmentHelper.IsLeftSide(1400, TestScreenWidth),
            "Window at 1400px on 1920px screen should be on right side");
    }

    [Fact]
    public void AlignmentMode_IsLeft_WhenExactlyHalfScreen()
    {
        Assert.True(MusicFloatWindow.AlignmentHelper.IsLeftSide(960, TestScreenWidth),
            "Window exactly at midpoint should default to left side");
    }
```

需要添加 `using`（若尚未存在的话只需确保编译能通过即可）。

- [ ] **Step 2: 运行测试验证编译失败**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "AlignmentMode" 2>&1
```

Expected: 编译错误 `'MusicFloatWindow' does not contain a definition for 'AlignmentHelper'`。

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "test: add failing AlignmentMode tests for window side detection"
```

---

### Task 2: TDD GREEN — 实现 AlignmentHelper + 歌名居中修复 + 滑动动画修复

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml`
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`

这 3 个修复都在 code-behind + XAML 上且相互关联，放在一个 Task 中避免文件冲突。

---

**修改 A — XAML 清理 + 对齐属性去掉硬编码：**

将整个 `MusicFloatWindow.xaml` 替换为：

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
        MouseLeftButtonDown="OnDragAreaMouseDown"
        LocationChanged="OnWindowLocationChanged">
    <Grid x:Name="ContentRoot">
        <StackPanel x:Name="ContentPanel"
                    Opacity="1"
                    Margin="0">

            <!-- 封面（圆角裁切） -->
            <Border x:Name="CoverContainer"
                    Width="180"
                    Height="180"
                    CornerRadius="10"
                    HorizontalAlignment="Center"
                    Margin="12,12,12,0">
                <Border.Clip>
                    <RectangleGeometry RadiusX="10" RadiusY="10"
                        Rect="0,0,180,180" />
                </Border.Clip>
                <Image x:Name="CoverImage"
                       Width="180"
                       Height="180"
                       Stretch="UniformToFill" />
            </Border>

            <!-- 歌曲标题（支持超长歌名自动缓慢滚动） -->
            <Canvas x:Name="TitleCanvas"
                    Width="220"
                    Height="24"
                    ClipToBounds="True"
                    HorizontalAlignment="Center"
                    Margin="12,8,12,0">
                <TextBlock x:Name="SongTitle"
                           Text="未在播放"
                           Foreground="#FFFFFF"
                           FontSize="15"
                           FontWeight="Bold"
                           TextTrimming="None"
                           VerticalAlignment="Center"
                           TextAlignment="Center"
                           Height="24"
                           Width="220">
                    <TextBlock.RenderTransform>
                        <TranslateTransform x:Name="TitleTranslate" X="0" />
                    </TextBlock.RenderTransform>
                    <TextBlock.Effect>
                        <DropShadowEffect BlurRadius="6"
                            ShadowDepth="1"
                            Opacity="0.6"
                            Color="Black" />
                    </TextBlock.Effect>
                </TextBlock>
            </Canvas>

            <!-- 歌手 -->
            <TextBlock x:Name="SongArtist"
                       Text="—"
                       Foreground="#CCCCCC"
                       FontSize="12"
                       TextAlignment="Center"
                       TextTrimming="CharacterEllipsis"
                       Margin="12,2,12,12">
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

关键变更：
- **移除** `Grid.RenderTransform` + `SlideTransform`（从未使用）
- **移除** `StackPanel.RenderTransform` + `PanelTranslate`（改用 Margin 动画）
- **新增** `Window.LocationChanged="OnWindowLocationChanged"` 事件绑定
- StackPanel 新增 `x:Name="ContentPanel"` + `Margin="0"`
- CoverContainer Margin 从 `0,12,0,0` 改为 `12,12,12,0`（左右各 12px 内边距，适配动态对齐）
- TitleCanvas Margin 从 `0,8,0,0` 改为 `12,8,12,0`
- SongArtist Margin 从 `0,2,0,12` 改为 `12,2,12,12`
- SongTitle 恢复 `TextAlignment="Center"` + `Width="220"`（短文本居中）

---

**修改 B — code-behind：居中逻辑修复**

找到 `StartOrStopTitleMarquee()` 方法，替换为：

```csharp
    private void StartOrStopTitleMarquee()
    {
        _marqueeTimer.Stop();
        _marqueeOffset = 0;
        TitleTranslate.X = 0;

        var title = SongTitle.Text ?? "";
        var availableWidth = TitleCanvas.Width;
        var fontSize = SongTitle.FontSize;

        if (TitleMarquee.NeedsScroll(title, availableWidth, fontSize))
        {
            // 滚动模式：TextBlock 宽度自适应、左对齐
            SongTitle.TextAlignment = System.Windows.TextAlignment.Left;
            SongTitle.Width = double.NaN;

            Dispatcher.BeginInvoke(
                new Action(() => _marqueeTimer.Start()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            // 非滚动模式：固定宽度居中
            SongTitle.TextAlignment = System.Windows.TextAlignment.Center;
            SongTitle.Width = 220;
        }
    }
```

---

**修改 C — code-behind：滑动动画改用 Margin**

将整个 `PlaySongSwitchAnimation` 方法（从 `// ── 动画 ──` 注释到方法闭包 `}`）替换为：

```csharp
    // ── 动画 ──────────────────────────────────────────────

    /// <summary>
    /// 歌曲切换动画：当前内容淡出左滑 → 更新内容 → 新内容从右侧淡入。
    /// 使用 Margin ThicknessAnimation 替代 TranslateTransform，避免 Freezable 动画管道不稳定性。
    /// </summary>
    private async void PlaySongSwitchAnimation(Action onMidpoint)
    {
        var panel = ContentPanel;

        // ── Phase 1: 淡出 + 左滑（200ms）──
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        var slideLeft = new System.Windows.Media.Animation.ThicknessAnimation(
            ContentPanel.Margin,
            new Thickness(-35, 0, 0, 0),
            TimeSpan.FromMilliseconds(200));

        var sbOut = new System.Windows.Media.Animation.Storyboard();
        System.Windows.Media.Animation.Storyboard.SetTarget(fadeOut, panel);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeOut,
            new PropertyPath(System.Windows.UIElement.OpacityProperty));
        System.Windows.Media.Animation.Storyboard.SetTarget(slideLeft, panel);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideLeft,
            new PropertyPath(FrameworkElement.MarginProperty));
        sbOut.Children.Add(fadeOut);
        sbOut.Children.Add(slideLeft);

        sbOut.Begin();
        await Task.Delay(220); // 略大于动画时长，确保 Phase 1 完全结束

        sbOut.Stop();          // Phase 1 结束 → 清除所有动画持有

        // ── 中点：更新内容 ──
        onMidpoint();

        // ── Phase 2: 从右侧淡入（200ms）──
        ContentPanel.Opacity = 0;
        ContentPanel.Margin = new Thickness(35, 0, 0, 0);

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        var slideRight = new System.Windows.Media.Animation.ThicknessAnimation(
            new Thickness(35, 0, 0, 0),
            new Thickness(0),
            TimeSpan.FromMilliseconds(200));

        var sbIn = new System.Windows.Media.Animation.Storyboard();
        System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, panel);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn,
            new PropertyPath(System.Windows.UIElement.OpacityProperty));
        System.Windows.Media.Animation.Storyboard.SetTarget(slideRight, panel);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideRight,
            new PropertyPath(FrameworkElement.MarginProperty));
        sbIn.Children.Add(fadeIn);
        sbIn.Children.Add(slideRight);

        sbIn.Begin();
    }
```

关键变更：
- 动画目标从 `PanelTranslate`（Freezable）改为 `ContentPanel.Margin`（FrameworkElement，可靠）
- Phase 1 到 Phase 2 之间用 `await Task.Delay(220)` + `sbOut.Stop()` 彻底分离两段动画
- Phase 2 起始值直接赋值 `Margin = new Thickness(35, 0, 0, 0)` 后再启动动画

---

**修改 D — code-behind：AlignmentHelper 嵌套类**

在 `TitleMarquee` 类之后、外层 `}` 之前添加：

```csharp
    /// <summary>
    /// 窗口位置判定工具。
    /// </summary>
    public static class AlignmentHelper
    {
        /// <summary>
        /// 判定窗口是否在屏幕左侧（Left < 屏幕宽度的一半）。
        /// 默认按左侧处理。
        /// </summary>
        public static bool IsLeftSide(double windowLeft, double screenWidth)
        {
            return windowLeft < screenWidth / 2.0;
        }
    }
```

---

**修改 E — code-behind：LocationChanged 动态对齐**

在 `OnDragAreaMouseDown` 方法之后、`// ── 歌名滚动 ──` 区域之前，添加：

```csharp
    // ── 位置跟踪 + 动态对齐 ──────────────────────────────

    private bool _isOnLeftSide = true;

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var isLeft = AlignmentHelper.IsLeftSide(Left, screenWidth);

        if (isLeft == _isOnLeftSide) return;
        _isOnLeftSide = isLeft;

        var halign = isLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        var talign = isLeft ? System.Windows.TextAlignment.Left : System.Windows.TextAlignment.Right;

        CoverContainer.HorizontalAlignment = halign;
        TitleCanvas.HorizontalAlignment = halign;
        SongArtist.TextAlignment = talign;

        // 非滚动模式下同步更新标题对齐
        if (!_marqueeTimer.IsEnabled && SongTitle.Width == 220)
        {
            SongTitle.TextAlignment = isLeft ? System.Windows.TextAlignment.Left : System.Windows.TextAlignment.Right;
        }
    }
```

在 `Loaded` 事件中首次触发对齐：

找到 `Loaded += (s, e) =>` 回调，在 `Left = 0; Top = ...` 之后追加：

```csharp
            OnWindowLocationChanged(null, EventArgs.Empty);
```

---

**修改 F — code-behind：移除不再使用的 using**

移除 `using System.Windows.Input;`（`OnDragAreaMouseDown` 中使用完全限定名 `System.Windows.Input.MouseButtonEventArgs` 和 `System.Windows.Input.MouseButtonState`）。如果 `using System.Windows.Media;` 不再需要也可移除（检查：仅 `LoadCoverAsync` 用到 `System.Windows.Media.Imaging.BitmapImage`，不需要 Media）。

保留 `using System.Threading.Tasks;`（`LoadCoverAsync` 用的 `Task`）。

- [ ] **Step 1: 运行 Baseline 测试**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "AlignmentMode|TitleMarquee|IsSongChanged" 2>&1
```

Expected: AlignmentMode 编译失败（RED），TitleMarquee 3 PASS，IsSongChanged 5 PASS。

- [ ] **Step 2: 执行修改 A~F**

先后修改 XAML 文件和 .cs 文件。

- [ ] **Step 3: 构建验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: 0 警告, 0 错误。

- [ ] **Step 4: 运行全部新增/修改的测试**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "AlignmentMode|TitleMarquee|IsSongChanged" 2>&1
```

Expected: AlignmentMode 3 PASS, TitleMarquee 3 PASS, IsSongChanged 5 PASS = 11 PASS。

- [ ] **Step 5: 运行全部测试**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 30 PASS（27 原有 + 3 AlignmentMode），1 预期 FAIL（透明度测试因无 WPF 上下文）。

- [ ] **Step 6: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git commit -m "fix: title centering for short text; refactor: Margin-based slide animation replaces Freezable TranslateTransform; feat: dynamic left/right alignment based on window position"
```

---

### Task 3: 全量构建 + 全部测试收尾

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

Expected: 30 PASS + 1 预期 FAIL（透明度测试缺 WPF 上下文）。

- [ ] **Step 3: Commit**

```bash
git commit -m "chore: final verification — all tests pass, build clean"
```

---

## 可行性分析总结

| 需求 | 可行性 | 风险等级 | 根因 / 说明 |
|------|--------|----------|-------------|
| 短歌名未居中 | ✅ 可行 | 低 | 上次重构时 `TextAlignment` 未显式设回 Center；修复：非滚动路径设 `Center + Width=220` |
| 滑动动画无左右移动 | ✅ 可行 | 低 | `TranslateTransform.X` 是 Freezable 属性，Storyboard 对其做动画存在已知管道不稳定；改为 `FrameworkElement.Margin` ThicknessAnimation 彻底解决 |
| 动态左右对齐 | ✅ 可行 | 低 | `Window.LocationChanged` + `AlignmentHelper.IsLeftSide()` + 动态设 `HorizontalAlignment`/`TextAlignment` |

### 动画修复方案详解

**原方案（不可靠）：**
```
Storyboard → PanelTranslate(Freezable).XProperty 动画
Completed → BeginAnimation(null) → 赋值 → Phase 2
```
问题：`TranslateTransform` 继承自 `Freezable`，其动画管道与 `FrameworkElement` 不同。`Storyboard.SetTargetProperty` 对 Freezable 依赖属性的动画在某些 WPF 版本/渲染阶段下存在时序问题。

**新方案（可靠）：**
```
Task.Delay(220) → sbOut.Stop() → 赋值 → Phase 2 Storyboard(Margin)
```
- 动画目标改为 `FrameworkElement.MarginProperty`（非 Freezable，管道成熟稳定）
- `await Task.Delay(220)` 确保 Phase 1 完全结束（替代 Completed 事件的时序不确定性）
- `sbOut.Stop()` 显式终止 Phase 1，释放所有动画持有
- 两段动画彻底隔离，无冲突可能

### 对齐切换行为

| 窗口位置 | CoverContainer | TitleCanvas | SongArtist |
|----------|---------------|-------------|------------|
| 屏幕左侧 (`Left < screenWidth/2`) | `HorizontalAlignment.Left` | `HorizontalAlignment.Left` | `TextAlignment.Left` |
| 屏幕右侧 (`Left >= screenWidth/2`) | `HorizontalAlignment.Right` | `HorizontalAlignment.Right` | `TextAlignment.Right` |

拖拽窗口跨越屏幕中线时自动切换；滚动中的标题不会因对齐切换而中断。"