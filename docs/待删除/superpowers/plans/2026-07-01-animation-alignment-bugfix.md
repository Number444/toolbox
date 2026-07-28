# 悬浮窗动画 + 对齐 Bug 深度诊断与修复方案

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 4 个 bug：①首次打开无右滑动画 ②首次打开文字未贴侧 ③切歌后排版错乱 ④切歌后左滑动画消失。

**Architecture:** 4 个 bug 全部根因明确：`_isOnLeftSide` 初始化值导致 `OnWindowLocationChanged` 首次 early return（Bug②）；`StartOrStopTitleMarquee` 硬编码 Center 覆盖位置对齐（Bug③）；`async void` + `Task.Delay` + 未清理 `sbIn` 时钟导致动画冲突和 HoldEnd 残留（Bug①④）。修复方案：用 `Storyboard.Completed` 事件替代 `Task.Delay`，引入 `_currentStoryboard` 字段防重入，`_isOnLeftSide` 改为 `bool?`，提取 `GetTextAlignment`/`GetHorizontalAlignment` 统一对齐逻辑。

**Tech Stack:** .NET 9, WPF, xUnit, C#

---

## Bug 根因分析

### Bug ②: 首次打开文字居中未贴侧

**根因：** `_isOnLeftSide` 初始化为 `true`（`MusicFloatWindow.xaml.cs:29`）。

```
首次 Loaded → Left=0 → OnWindowLocationChanged → isLeft=true
→ if (isLeft == _isOnLeftSide) → true == true → return!  ← 早退！
```

`OnWindowLocationChanged` 首次调用就 early return，从未设置 CoverContainer/TitleCanvas/SongArtist 的对齐。XAML 默认值 `HorizontalAlignment="Center"` 和 `TextAlignment="Center"` 保持不变 → 文字居中而非贴侧。

### Bug ③: 切歌后排版错乱

**根因：** `StartOrStopTitleMarquee()`（`:184-208`）在 `onMidpoint()` 回调中对短标题硬编码 `TextAlignment = Center`（`:205`）。

`OnWindowLocationChanged` 之前已将对齐设为 Left/Right，但每次切歌 `StartOrStopTitleMarquee` 都强制重置回 Center，覆盖位置对齐。

### Bug ①: 首次打开无右滑动画

**根因：** `PlaySongSwitchAnimation`（`:230-277`）使用 `async void` + `await Task.Delay(220)` 模式。

1. `sbOut.Begin()` → Phase 1 动画 200ms
2. 200ms 后动画自然结束，`HoldEnd` 锁定 `Opacity=0, Margin=-35`
3. `await Task.Delay(220)` 在 220ms 后恢复（可能更晚）
4. `sbOut.Stop()` → 移除 HoldEnd，属性回到 base（`Opacity=1, Margin=0`）→ 面板瞬闪全显
5. `onMidpoint()` 内 `StartOrStopTitleMarquee` 改 Width/TextAlignment → 布局失效
6. `Opacity=0; Margin=35` → 再次布局失效
7. `sbIn.Begin()` → Phase 2 的 `ThicknessAnimation(35→0)` 起点与实际渲染不同步

结果：用户看到 Phase 1（左滑淡出）后，面板闪烁一下直接出现在中心，看不到从右滑入的过程。

### Bug ④: 切歌后左滑动画消失

**根因：** `sbIn` 是局部变量（`:266`），完成后从不 `Stop()`。`HoldEnd` 持续持有 `Margin=0` 和 `Opacity=1`，动画时钟永不释放。多次切歌积累多个 `sbIn` 残留时钟，与新 `sbOut` 在同一属性上竞争，导致 Phase 1 左滑不可见。

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs` | 修改 | 修复全部 4 个 bug |
| `Toolbox.Tests/NeteaseMusicToolTests.cs` | 修改 | 新增 GetTextAlignment/GetHorizontalAlignment 测试 |

---

### Task 1: TDD RED — 写 AlignmentHelper 新方法测试

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Tests\NeteaseMusicToolTests.cs`

- [ ] **Step 1: 在测试文件末尾最后一个 `}` 之前追加 6 个测试**

```csharp
    [Fact]
    public void AlignmentHelper_GetTextAlignment_Left_WhenLeftSide()
    {
        Assert.Equal(System.Windows.TextAlignment.Left,
            MusicFloatWindow.AlignmentHelper.GetTextAlignment(true));
    }

    [Fact]
    public void AlignmentHelper_GetTextAlignment_Right_WhenRightSide()
    {
        Assert.Equal(System.Windows.TextAlignment.Right,
            MusicFloatWindow.AlignmentHelper.GetTextAlignment(false));
    }

    [Fact]
    public void AlignmentHelper_GetHorizontalAlignment_Left_WhenLeftSide()
    {
        Assert.Equal(System.Windows.HorizontalAlignment.Left,
            MusicFloatWindow.AlignmentHelper.GetHorizontalAlignment(true));
    }

    [Fact]
    public void AlignmentHelper_GetHorizontalAlignment_Right_WhenRightSide()
    {
        Assert.Equal(System.Windows.HorizontalAlignment.Right,
            MusicFloatWindow.AlignmentHelper.GetHorizontalAlignment(false));
    }

    [Fact]
    public void AlignmentHelper_GetTextAlignment_Center_WhenNull()
    {
        Assert.Equal(System.Windows.TextAlignment.Center,
            MusicFloatWindow.AlignmentHelper.GetTextAlignment(null));
    }

    [Fact]
    public void AlignmentHelper_GetHorizontalAlignment_Center_WhenNull()
    {
        Assert.Equal(System.Windows.HorizontalAlignment.Center,
            MusicFloatWindow.AlignmentHelper.GetHorizontalAlignment(null));
    }
```

- [ ] **Step 2: 运行测试验证编译失败**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "AlignmentHelper_Get" 2>&1
```

Expected: 编译错误 `'AlignmentHelper' does not contain a definition for 'GetTextAlignment'`。

- [ ] **Step 3: Commit**

```bash
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "test: add failing AlignmentHelper Get*Alignment tests"
```

---

### Task 2: TDD GREEN + 全部 Bug 修复

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`

5 处修改（A~E）修复全部 4 个 bug 并实现 GREEN。

---

**修改 A — `_isOnLeftSide` 改为 `bool?`（修复 Bug②）**

第 29 行：

```csharp
    private bool _isOnLeftSide = true;
```

替换为：

```csharp
    private bool? _isOnLeftSide = null;
```

---

**修改 B — `OnWindowLocationChanged` 提取 `ApplyAlignment`（修复 Bug②③）**

找到 `OnWindowLocationChanged` 方法（约 `:161-180`），替换为：

```csharp
    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var isLeft = AlignmentHelper.IsLeftSide(Left, screenWidth);

        if (isLeft == _isOnLeftSide) return;
        _isOnLeftSide = isLeft;

        ApplyAlignment(isLeft);
    }

    private void ApplyAlignment(bool isLeft)
    {
        var halign = AlignmentHelper.GetHorizontalAlignment(isLeft);
        var talign = AlignmentHelper.GetTextAlignment(isLeft);

        CoverContainer.HorizontalAlignment = halign;
        TitleCanvas.HorizontalAlignment = halign;
        SongArtist.TextAlignment = talign;

        if (!_marqueeTimer.IsEnabled && SongTitle.Width == 220)
        {
            SongTitle.TextAlignment = talign;
        }
    }
```

---

**修改 C — `StartOrStopTitleMarquee` 使用 `GetTextAlignment`（修复 Bug③）**

找到 `StartOrStopTitleMarquee` 方法中的 `if` 分支：

```csharp
            SongTitle.TextAlignment = System.Windows.TextAlignment.Left;
```

替换为：

```csharp
            SongTitle.TextAlignment = AlignmentHelper.GetTextAlignment(_isOnLeftSide);
```

找到 `else` 分支：

```csharp
            SongTitle.TextAlignment = System.Windows.TextAlignment.Center;
            SongTitle.Width = 220;
```

替换为：

```csharp
            SongTitle.TextAlignment = AlignmentHelper.GetTextAlignment(_isOnLeftSide);
            SongTitle.Width = 220;
```

---

**修改 D — 重写 `PlaySongSwitchAnimation`（修复 Bug①④）**

将整个 `PlaySongSwitchAnimation` 方法替换为：

```csharp
    // ── 动画 ──────────────────────────────────────────────

    private System.Windows.Media.Animation.Storyboard? _currentStoryboard;

    /// <summary>
    /// 歌曲切换动画：淡出左滑 → 更新内容 → 从右淡入。
    /// 使用 Storyboard.Completed 事件替代 Task.Delay，确保时序精确。
    /// _currentStoryboard 字段防止重入冲突。
    /// </summary>
    private void PlaySongSwitchAnimation(Action onMidpoint)
    {
        _currentStoryboard?.Stop();

        var panel = ContentPanel;

        // ── Phase 1: 淡出 + 左滑（200ms）──
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        var slideLeft = new System.Windows.Media.Animation.ThicknessAnimation(
            new Thickness(0),
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

        _currentStoryboard = sbOut;

        sbOut.Completed += (_, _) =>
        {
            sbOut.Stop();

            onMidpoint();

            panel.Opacity = 0;
            panel.Margin = new Thickness(35, 0, 0, 0);

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

            _currentStoryboard = sbIn;

            sbIn.Completed += (_, _) =>
            {
                sbIn.Stop();
                _currentStoryboard = null;
            };

            sbIn.Begin();
        };

        sbOut.Begin();
    }
```

关键变更：
1. `async void` → 同步 + `Completed` 事件：消除 `Task.Delay` 不确定性
2. `_currentStoryboard` 字段：重入时 `Stop()` 旧动画
3. `sbOut.Completed` → `Stop()` → `onMidpoint()` → `sbIn.Begin()`：无间隙串行
4. `sbIn.Completed` → `Stop()` → `_currentStoryboard = null`：清理 HoldEnd
5. `slideLeft` 的 From 改为 `new Thickness(0)`：不读被污染的 `ContentPanel.Margin`

---

**修改 E — 扩展 `AlignmentHelper` 嵌套类**

找到 `AlignmentHelper` 类，替换为：

```csharp
    public static class AlignmentHelper
    {
        public static bool IsLeftSide(double windowLeft, double screenWidth)
        {
            return windowLeft <= screenWidth / 2.0;
        }

        public static HorizontalAlignment GetHorizontalAlignment(bool? isLeft)
        {
            return isLeft switch
            {
                true => HorizontalAlignment.Left,
                false => HorizontalAlignment.Right,
                null => HorizontalAlignment.Center
            };
        }

        public static System.Windows.TextAlignment GetTextAlignment(bool? isLeft)
        {
            return isLeft switch
            {
                true => System.Windows.TextAlignment.Left,
                false => System.Windows.TextAlignment.Right,
                null => System.Windows.TextAlignment.Center
            };
        }
    }
```

---

- [ ] **Step 1: 运行测试确认 RED**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "AlignmentHelper_Get" 2>&1
```

Expected: 编译失败。

- [ ] **Step 2: 执行修改 A~E**

- [ ] **Step 3: 构建验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: 0 警告, 0 错误。

- [ ] **Step 4: 运行测试验证 GREEN**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "AlignmentHelper" 2>&1
```

Expected: 9 PASS（3 原有 + 6 新增）。

- [ ] **Step 5: 运行全部测试**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 36 PASS，1 预期 FAIL（透明度测试）。

- [ ] **Step 6: Commit**

```bash
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "fix: 4 bugs — alignment init, marquee override, animation Completed events, storyboard cleanup"
```

---

### Task 3: 全量构建 + 全部测试收尾

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

Expected: 36 PASS + 1 预期 FAIL。
