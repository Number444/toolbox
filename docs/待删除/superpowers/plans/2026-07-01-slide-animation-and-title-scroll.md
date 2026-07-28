# 悬浮窗左右滑动动画修复 + 长歌名滚动 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复歌曲切换时左右移动动画不生效的问题，并为较长歌名添加缓慢自动滚动（跑马灯）效果。

**Architecture:** 
- **滑动动画修复**：核心问题在于 WPF `Storyboard` 默认 `FillBehavior=HoldEnd`（动画结束后持有最终值），导致 Phase 1 结束后 `PanelTranslate.X` 被锁定在 -35，Phase 2 的起始值 `PanelTranslate.X = 35` 赋值与 Phase 2 动画之间产生冲突。修复方案：在 Phase 1 完成回调中先移除动画持有（`BeginAnimation(null)`），再赋值起始值，再启动 Phase 2。
- **歌名滚动**：将 `SongTitle` 包裹在 `Canvas` + `ClipToBounds` 中，通过 `TranslateTransform` 对 TextBlock 做水平位移动画。仅当文本实际宽度超过可用宽度时启用滚动；歌曲切换时停止旧动画并重置位置。

**Tech Stack:** .NET 9, WPF, xUnit, C#

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml` | 修改 | SongTitle 重构为 Canvas+TextBlock 结构，添加 TitleTranslate 变换 |
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs` | 修改 | 修复滑动动画（添加 BeginAnimation(null) 清除持有），新增歌名滚动逻辑 |
| `Toolbox.Tests/NeteaseMusicToolTests.cs` | 修改 | 新增 TitleMarquee 滚动触发条件测试 |

---

### Task 1: 诊断并修复当前动画无 holder 清除问题

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`

**问题：** Phase 1 动画以 HoldEnd 结束，`PanelTranslate.X` 和 `panel.Opacity` 被动画持有。Phase 2 中 `PanelTranslate.X = 35` 虽能直接赋值覆盖，但 Phase 2 的 `DoubleAnimation(35, 0, 200ms)` 启动时可能与 Phase 1 持有的旧动画值冲突，导致 X 位移动画无视觉效果。

**修复：** 在 `Completed` 回调中，设置新值之前先调用 `BeginAnimation(null)` 清除动画持有。

- [ ] **Step 1: 运行当前测试确认基线**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "IsSongChanged" 2>&1
```

Expected: 5 PASS。

- [ ] **Step 2: 修改 `PlaySongSwitchAnimation` 方法**

将 `MusicFloatWindow.xaml.cs` 中 `PlaySongSwitchAnimation` 方法的 `storyboard.Completed` 回调替换为带 holder 清除的版本。

找到以下代码：

```csharp
        storyboard.Completed += (_, _) =>
        {
            // ── 中点：更新内容 ──
            onMidpoint();

            // ── Phase 2: 从右侧淡入（200ms）──
            panel.Opacity = 0;
            PanelTranslate.X = 35;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideRight = new System.Windows.Media.Animation.DoubleAnimation(35, 0, TimeSpan.FromMilliseconds(200));
```

替换为（在 `onMidpoint()` 之后加入 `BeginAnimation(null)` 清除）：

```csharp
        storyboard.Completed += (_, _) =>
        {
            // ── 清除 Phase 1 动画持有，避免 Phase 2 起始值冲突 ──
            panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
            PanelTranslate.BeginAnimation(TranslateTransform.XProperty, null);

            // ── 中点：更新内容 ──
            onMidpoint();

            // ── Phase 2: 从右侧淡入（200ms）──
            panel.Opacity = 0;
            PanelTranslate.X = 35;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideRight = new System.Windows.Media.Animation.DoubleAnimation(35, 0, TimeSpan.FromMilliseconds(200));
```

- [ ] **Step 3: 构建并运行测试验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "IsSongChanged" 2>&1
```

Expected: 0 警告, 0 错误；5 PASS。

- [ ] **Step 4: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git commit -m "fix: clear animation holders before Phase 2 of song switch slide"
```

---

### Task 2: TDD RED — 写歌名滚动触发条件测试

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Tests\NeteaseMusicToolTests.cs` (append)

**背景：** 歌名滚动（跑马灯）应仅在文本实际宽度超过可用显示宽度时启用。需要新增 `TitleMarquee` 静态方法来判定是否需要滚动。在单元测试中我们无法测量 WPF TextBlock 实际渲染宽度，因此测试方法通过计算近似中文字符宽度来判定。

- [ ] **Step 1: 在测试文件末尾添加以下测试**

在 `MusicFloatWindow_HasTransparentBackground` 的 `}` 之后、类闭包 `}` 之前添加：

```csharp
    [Fact]
    public void TitleMarquee_NeedsScroll_ReturnsTrue_WhenTextTooLong()
    {
        // 窗口实际可用宽度约 220px（Width=252, 减去边距）
        // 中文字符约 15px(FontSize) × 1.1 = 16.5px 每字符
        // 220 / 16.5 ≈ 13 个中文字符；14 字符需滚动
        Assert.True(MusicFloatWindow.TitleMarquee.NeedsScroll(
            "这是一个很长的中文歌曲标题需要滚动显示", 220.0, 15.0),
            "Long text should require scrolling");
    }

    [Fact]
    public void TitleMarquee_NeedsScroll_ReturnsFalse_WhenTextFits()
    {
        Assert.False(MusicFloatWindow.TitleMarquee.NeedsScroll(
            "短歌名", 220.0, 15.0),
            "Short text should not require scrolling");
    }

    [Fact]
    public void TitleMarquee_NeedsScroll_ReturnsFalse_WhenNoTitle()
    {
        Assert.False(MusicFloatWindow.TitleMarquee.NeedsScroll(
            "未在播放", 220.0, 15.0),
            "Default placeholder should not scroll");
    }
```

注意：`MusicFloatWindow.TitleMarquee` 是尚未存在的 nested class，当前测试应编译失败。

- [ ] **Step 2: 运行测试验证失败**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "TitleMarquee" 2>&1
```

Expected: 编译错误 `'MusicFloatWindow' does not contain a definition for 'TitleMarquee'`。

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "test: add failing TitleMarquee.NeedsScroll tests for long title detection"
```

---

### Task 3: TDD GREEN — 实现 `TitleMarquee` 判定类

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs` (在类末尾添加 nested class)

- [ ] **Step 1: 在 `MusicFloatWindow` 类末尾（最后一个 `}` 之前）添加 nested class**

```csharp
    /// <summary>
    /// 歌名滚动判定工具。用于判断文本是否超出可用宽度而需要跑马灯滚动。
    /// </summary>
    public static class TitleMarquee
    {
        /// <summary>
        /// 中文字符宽度系数（FontSize × 系数 ≈ 单字符像素宽度，中文约 1.0，英文约 0.55）。
        /// </summary>
        private const double CnCharWidthFactor = 1.05;
        private const double EnCharWidthFactor = 0.55;

        /// <summary>
        /// 判断指定文本在给定可用宽度和字体大小下是否需要滚动显示。
        /// </summary>
        /// <param name="text">要显示的文本</param>
        /// <param name="availableWidth">可用像素宽度</param>
        /// <param name="fontSize">字体大小（px）</param>
        /// <returns>true 表示文本超出宽度需要滚动</returns>
        public static bool NeedsScroll(string text, double availableWidth, double fontSize)
        {
            if (string.IsNullOrEmpty(text)) return false;

            double estimatedWidth = 0;
            foreach (char c in text)
            {
                // CJK 统一表意文字范围 + 全角标点
                if (c >= 0x4E00 && c <= 0x9FFF
                    || c >= 0x3400 && c <= 0x4DBF
                    || c >= 0xF900 && c <= 0xFAFF
                    || c >= 0x3000 && c <= 0x303F  // CJK 标点
                    || c >= 0xFF00 && c <= 0xFFEF)  // 全角形式
                {
                    estimatedWidth += fontSize * CnCharWidthFactor;
                }
                else
                {
                    estimatedWidth += fontSize * EnCharWidthFactor;
                }
            }

            return estimatedWidth > availableWidth;
        }
    }
```

- [ ] **Step 2: 运行测试验证通过**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "TitleMarquee" 2>&1
```

Expected: 3 PASS。

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git commit -m "feat: add TitleMarquee.NeedsScroll for long title scroll detection"
```

---

### Task 4: 重构 XAML — SongTitle 改为可滚动布局

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml`

**变更内容：** 将当前 `SongTitle` TextBlock 替换为 `Canvas` + `TextBlock` 结构，支持水平位移变换。

- [ ] **Step 1: 运行基线测试确认**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "TitleMarquee" 2>&1
```

Expected: 3 PASS。

- [ ] **Step 2: 替换 SongTitle 区域**

找到 XAML 中以下代码块：

```xml
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
```

替换为：

```xml
            <!-- 歌曲标题（支持超长歌名自动缓慢滚动） -->
            <Canvas x:Name="TitleCanvas"
                    Width="220"
                    Height="24"
                    ClipToBounds="True"
                    HorizontalAlignment="Center"
                    Margin="0,8,0,0">
                <TextBlock x:Name="SongTitle"
                           Text="未在播放"
                           Foreground="#FFFFFF"
                           FontSize="15"
                           FontWeight="Bold"
                           TextTrimming="None"
                           VerticalAlignment="Center"
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
```

关键变更：
- 用 `Canvas ClipToBounds="True"` 包裹 TextBlock 做可视区域裁剪
- TextBlock `Width="220"` 固定宽度（匹配 Canvas 宽度），`TextTrimming="None"` 允许超出
- 新增 `TitleTranslate` TranslateTransform 用于水平位移动画
- Canvas 高度 24px 匹配单行文本 + 阴影空间

- [ ] **Step 3: 构建验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: 0 警告, 0 错误。

- [ ] **Step 4: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml
git commit -m "refactor: SongTitle wrapped in Canvas for marquee scroll support"
```

---

### Task 5: 实现歌名滚动逻辑

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`

**变更内容：**
1. 添加滚动相关字段：`_marqueeTimer`、`_marqueeOffset`
2. 在 `OnNowPlayingChanged` 中歌曲切换时启动/停止滚动
3. 实现 `StartTitleMarquee`、`StopTitleMarquee`、`OnMarqueeTick` 方法
4. 在 `OnClosed` 中清理定时器

- [ ] **Step 1: 运行基线测试确认**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "TitleMarquee" 2>&1
```

Expected: 3 PASS。

- [ ] **Step 2: 在 code-behind 中添加滚动字段和定时器初始化**

在 `_previousInfo` 字段之后添加：

```csharp
    // 歌名滚动（跑马灯）
    private readonly System.Windows.Threading.DispatcherTimer _marqueeTimer;
    private double _marqueeOffset;

```

在构造函数 `InitializeComponent()` 之前添加定时器初始化：

找以下代码：
```csharp
    private MusicFloatWindow()
    {
        InitializeComponent();
```

替换为：
```csharp
    private MusicFloatWindow()
    {
        InitializeComponent();

        // 歌名滚动定时器：每 40ms 移动一次（约 25fps，流畅缓慢滚动）
        _marqueeTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(40),
            System.Windows.Threading.DispatcherPriority.Normal,
            OnMarqueeTick,
            Dispatcher);
        _marqueeTimer.Stop();
```

- [ ] **Step 3: 在 `OnNowPlayingChanged` 中加入滚动启停逻辑**

找到以下代码块：
```csharp
            if (isNewSong)
            {
                PlaySongSwitchAnimation(() =>
                {
                    SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
                    SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
                    _ = LoadCoverAsync();
                });
            }
```

替换为：
```csharp
            if (isNewSong)
            {
                PlaySongSwitchAnimation(() =>
                {
                    SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
                    SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
                    _ = LoadCoverAsync();
                    StartOrStopTitleMarquee();
                });
            }
```

- [ ] **Step 4: 在 `OnClosed` 中清理定时器**

找到以下代码：
```csharp
    protected override void OnClosed(EventArgs e)
    {
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        base.OnClosed(e);
    }
```

替换为：
```csharp
    protected override void OnClosed(EventArgs e)
    {
        _marqueeTimer.Stop();
        _listener.NowPlayingChanged -= OnNowPlayingChanged;
        _listener.Dispose();
        base.OnClosed(e);
    }
```

- [ ] **Step 5: 在文件末尾（`TitleMarquee` 类之后、外层 `}` 之前）添加滚动方法**

```csharp
    private void StartOrStopTitleMarquee()
    {
        _marqueeTimer.Stop();
        _marqueeOffset = 0;
        TitleTranslate.X = 0;

        var title = SongTitle.Text ?? "";
        var availableWidth = TitleCanvas.Width; // 220px
        var fontSize = SongTitle.FontSize; // 15

        if (TitleMarquee.NeedsScroll(title, availableWidth, fontSize))
        {
            // 设置 TextBlock 宽度为实际需要的宽度（让文本不换行）
            SongTitle.Width = double.NaN; // Auto

            // 暂停一帧让布局更新，再启动定时器
            Dispatcher.BeginInvoke(
                new Action(() => _marqueeTimer.Start()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnMarqueeTick(object? sender, EventArgs e)
    {
        // 缓慢向左滚动：每 tick 移动 0.3px（40ms × 0.3px = 7.5px/s）
        _marqueeOffset -= 0.3;

        // 当文本完全滚出左侧后，从右侧重新进入
        var textWidth = SongTitle.ActualWidth;
        var visibleWidth = TitleCanvas.Width;

        if (_marqueeOffset < -(textWidth + 30))
        {
            _marqueeOffset = visibleWidth;
        }

        TitleTranslate.X = _marqueeOffset;
    }
```

- [ ] **Step 6: 构建并运行测试验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 0 警告, 0 错误；28 PASS（25 原有 + 3 新增 TitleMarquee）。

- [ ] **Step 7: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git commit -m "feat: add title marquee scroll for long song names"
```

---

### Task 6: 全量构建 + 全部测试验证（收尾）

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

Expected: 28 PASS（原有 25 + 3 新增 TitleMarquee）。

- [ ] **Step 3: Commit**

```bash
git commit -m "chore: final verification — all tests pass, build clean"
```

---

## 可行性分析总结

| 需求 | 可行性 | 风险等级 | 根因 / 说明 |
|------|--------|----------|------|
| 左右滑动动画修复 | ✅ 可行 | 低 | WPF `Storyboard.FillBehavior=HoldEnd` 导致 Phase 1 动画持有 `PanelTranslate.X` 在 -35，Phase 2 新的 DoubleAnimation 无法覆盖持有值。修复：在 Phase 2 启动前调用 `BeginAnimation(null)` 清除持有。 |
| 歌名缓慢滚动 | ✅ 可行 | 低 | `Canvas.ClipToBounds` + `TranslateTransform` + `DispatcherTimer` 实现标准跑马灯。文本宽度估算用于判定是否启用（无需实际渲染）。 |

### 滑动动画根因详解

当前代码的 Phase 2 逻辑：
```
Completed → PanelTranslate.X = 35 → DoubleAnimation(35→0)
```

但实际上 `Completed` 触发时 Phase 1 的 Storyboard 仍在 hold `PanelTranslate.X` 在 -35。虽然直接赋值 `PanelTranslate.X = 35` 理论上可覆盖，但 WPF 动画系统中 **属性赋值与动画持有之间存在竞争条件**：如果赋值发生在动画时钟标记属性为"已被动画持有"之前（或 WPF 的依赖属性系统在处理动画持有 + 直接赋值时优先动画持有），那么 `DoubleAnimation(35→0)` 可能会从 -35 开始而非 35，导致视觉效果为面板在原地（约 0 附近）淡入 —— 即用户观察到的"只有淡入淡出没有左右移动"。

修复后（`BeginAnimation(null)` 显式清除持有）：
```
Completed → BeginAnimation(null) → PanelTranslate.X = 35 → DoubleAnimation(35→0)
```

清除持有后赋值是无歧义的，动画从 35 顺畅滑动到 0。

### 歌名滚动方案要点

- **判定逻辑**：对每个字符判断是否在 CJK 范围（U+4E00-U+9FFF 等），中文用 1.05×FontSize 宽度，英文用 0.55×FontSize，累加后与可用宽度（220px）比较
- **滚动速度**：0.3px / 40ms = 7.5px/s，适合慢速阅读
- **循环**：文本滚出左侧 30px 后从右侧重新滚入
- **歌曲切换**：立即停止旧滚动、重置位置、重新判定是否需要新滚动