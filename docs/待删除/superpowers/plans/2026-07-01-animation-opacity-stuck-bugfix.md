# 悬浮窗消失 Bug 根因修正方案

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复悬浮窗歌曲播放后瞬间消失、切歌弹出再消失的问题。根因是 `Storyboard.Stop()` 在 Completed 回调中无法立即释放动画时钟，导致 Phase 2 (`sbIn.Begin()`) 静默失败，`ContentPanel.Opacity` 永久卡在 0。

**Architecture:** 用 `panel.BeginAnimation(Property, null)` 替代 `sbOut.Stop()` 来同步清除动画时钟。这是 MSDN 推荐的动画时钟移除方式。同时移除不完整的 `_isAnimating` / `_currentStoryboard` 的 bool 防护方案，回归简单的一次性动画模型。

**Tech Stack:** .NET 9, WPF, xUnit, C#

---

## Bug 根因分析（重新深度分析）

### 症状

- **悬浮窗在歌曲播放后一瞬间消失**：窗口可见 → SMTC 事件触发动画 → 内容面板淡出左滑 → 面板卡在 Opacity=0 → 窗口完全不可见
- **切换歌曲弹出再消失**：切歌触发 `TryReentrantUpdate` → `ResetPanelToVisibleState` 设置 Opacity=1 → 窗口短暂可见 → 残留 HoldEnd 时钟重新将 Opacity 拉回 0 → 窗口再次消失
- **前一轮修复（`_isAnimating` 防重入）无效**：修复没有触及真正的根因——`Storyboard.Stop()` 的不确定性

### 真根因：`Storyboard.Stop()` 在 Completed 回调中无法即时释放动画时钟

```csharp
sbOut.Completed += (_, _) =>
{
    sbOut.Stop();                  // ← WPF 内部：时钟 "Filling"→"Stopped" 不是瞬时的
    onMidpoint();                  // ← 更新内容
    panel.Opacity = 0;            // ← 本地值设为 0（被残留 HoldEnd 覆盖无效）
    panel.Margin = new Thickness(35, 0, 0, 0);
    // 创建 sbIn ...
    sbIn.Begin();                  // ← ★ 静默失败！Opacity 和 Margin 的时钟槽仍被旧时钟占用
};
```

**WPF 动画管道的实际情况：**

1. `sbOut` 完成 Phase 1（200ms），FillBehavior=HoldEnd，动画时钟进入 "Filling" 状态
2. `Completed` 事件触发，回调开始
3. `sbOut.Stop()` 调用：WPF 将时钟从 "Filling" 转为 "Stopped" —— 但这个转换在 WPF 的时钟管理器中是**异步处理的**（通过 Dispatcher 优先级调度）
4. `panel.Opacity = 0` 设置本地值，但 "Filling" 状态的时钟仍然持有该属性，**本地值被动画值覆盖**
5. `sbIn.Begin()` 尝试在 `OpacityProperty` 上建立新动画时钟→ **属性已有活跃时钟（正在从 Filling→Stopped 转换中）→ Begin() 静默失败**
6. Phase 2 从未启动，面板永远卡在 Opacity=0

**为什么 `BeginAnimation(null)` 能解决：**

MSDN 明确指出：`FrameworkElement.BeginAnimation(dp, null)` 会**同步**地停止并移除动画，然后恢复属性到其基值或本地值。它不经过 WPF 时钟管理器的异步调度，而是直接操作属性系统。

### 为什么"前一轮修复"（`_isAnimating` 防重入）无效

`_isAnimating` 只解决了"同一个 Opacity 属性上有两个动画竞争"的问题，但没有解决"同一个 Opacity 属性上被残留的 HoldEnd 时钟占用导致新动画 Begin() 静默失败"的问题。这两个是不同的根因。

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs` | 修改 | 用 BeginAnimation(null) 替代 Stop()，简化动画模型 |
| `Toolbox.Tests/NeteaseMusicToolTests.cs` | 修改 | 新增 BeginAnimation 清除测试 + ResetPanel 恢复测试 |

---

### Task 1: TDD RED — 写 BeginAnimation(null) 清除动画行为的单元测试

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Tests\NeteaseMusicToolTests.cs`

- [ ] **Step 1: 在测试文件末尾追加 2 个测试**

```csharp
    [Fact]
    public void ClearAnimationClock_OnOpacity_RestoresLocalValue()
    {
        // 验证：在 WPF 动画 HoldEnd 后，BeginAnimation(null) 可同步清除时钟并恢复本地值。
        Exception? threadException = null;
        double finalOpacity = -1;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new System.Windows.Controls.StackPanel { Opacity = 1 };
                var animation = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
                panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, animation);
                // 等待动画完成 + HoldEnd 生效
                System.Threading.Thread.Sleep(150);
                // 用 BeginAnimation(null) 清除时钟
                panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                // 立即读取：应该回到本地值 1，而非 HoldEnd 的 0
                finalOpacity = panel.Opacity;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.Equal(1.0, finalOpacity);
    }

    [Fact]
    public void ClearAnimationClock_OnMargin_RestoresLocalValue()
    {
        // 验证：在 WPF 动画 HoldEnd 后，BeginAnimation(null) 可同步清除 Margin 时钟。
        Exception? threadException = null;
        var finalMargin = new System.Windows.Thickness(-999);
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0) };
                var animation = new System.Windows.Media.Animation.ThicknessAnimation(
                    new System.Windows.Thickness(-35, 0, 0, 0), TimeSpan.FromMilliseconds(100));
                panel.BeginAnimation(System.Windows.FrameworkElement.MarginProperty, animation);
                System.Threading.Thread.Sleep(150);
                panel.BeginAnimation(System.Windows.FrameworkElement.MarginProperty, null);
                finalMargin = panel.Margin;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.Equal(new System.Windows.Thickness(0), finalMargin);
    }
```

- [ ] **Step 2: 运行测试验证 RED**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "ClearAnimationClock_" 2>&1
```

Expected: **关键验证**——如果这 2 个测试 PASS，说明 `BeginAnimation(null)` 确实能同步清除 HoldEnd 时钟，验证了修复方案的前提；如果 FAIL（Opacity 仍为 0 / Margin 仍为 -35），说明 WPF 的 `BeginAnimation(null)` 也有异步问题，需要换方案。

**注意：这是验证性测试，不是 TDD 功能缺失测试。它们的目的是验证修复方案的前提假设。**

---

### Task 2: TDD GREEN — 修复 `PlaySongSwitchAnimation` 的动画时钟清除

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`

**修改内容：替换第 230~333 行的整个动画区域**

- [ ] **Step 1: 将 `// ── 动画 ──` 区域（`_currentStoryboard` 字段 + `ResetPanelToVisibleState` + `TryReentrantUpdate` + `PlaySongSwitchAnimation`）全部替换为如下代码**

```csharp
    // ── 动画 ──────────────────────────────────────────────

    /// <summary>
    /// 将面板重置为可见状态（Opacity=1, Margin=0）并清除所有残留动画时钟。
    /// 使用 BeginAnimation(null)（非 Storyboard.Stop()）确保同步移除。
    /// </summary>
    internal static void ResetPanelToVisibleState(System.Windows.Controls.StackPanel panel)
    {
        if (panel == null) return;
        // 用 BeginAnimation(null) 同步清除残留动画时钟
        panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
        panel.BeginAnimation(FrameworkElement.MarginProperty, null);
        // 设置本地值
        panel.Opacity = 1;
        panel.Margin = new Thickness(0);
    }

    /// <summary>
    /// 歌曲切换动画：淡出左滑 → 更新内容 → 从右淡入。
    /// 用 BeginAnimation(null) 替代 Storyboard.Stop() 同步清除动画时钟，
    /// 避免 HoldEnd 残留导致 Phase 2 Begin() 静默失败。
    /// </summary>
    private void PlaySongSwitchAnimation(Action onMidpoint)
    {
        var panel = ContentPanel;

        // Step 0: 清除可能残留的动画时钟 + 设置干净起点
        ResetPanelToVisibleState(panel);

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

        sbOut.Completed += (_, _) =>
        {
            // 关键修复：用 BeginAnimation(null) 替代 sbOut.Stop()
            // 同步清除 Phase 1 的 HoldEnd 时钟，确保 Phase 2 Begin() 能正常启动
            panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
            panel.BeginAnimation(FrameworkElement.MarginProperty, null);

            onMidpoint();

            panel.Opacity = 0;
            panel.Margin = new Thickness(35, 0, 0, 0);

            // ── Phase 2: 从右淡入（200ms）──
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

            sbIn.Completed += (_, _) =>
            {
                // Phase 2 结束：清除 HoldEnd，恢复本地值
                panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                panel.BeginAnimation(FrameworkElement.MarginProperty, null);
                panel.Opacity = 1;
                panel.Margin = new Thickness(0);
            };

            sbIn.Begin();
        };

        sbOut.Begin();
    }
```

关键变更：
1. **删除 `_currentStoryboard`**：不再需要（不再用 Stop() 来取消）
2. **删除 `_isAnimating`**：不再需要（BeginAnimation(null) 在每次调用入口同步清除所有残留）
3. **删除 `TryReentrantUpdate`**：不再需要（入口 ResetPanelToVisibleState 自动处理所有重入场景）
4. `sbOut.Completed` 回调中：`sbOut.Stop()` → `panel.BeginAnimation(Property, null)` × 2
5. `sbIn.Completed` 回调中：`sbIn.Stop()` → `panel.BeginAnimation(Property, null)` × 2 + 显式恢复本地值
6. 入口：添加 `ResetPanelToVisibleState(panel)` 确保每次动画起点干净

- [ ] **Step 2: 构建验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: 0 警告, 0 错误。

- [ ] **Step 3: 运行全部测试验证 GREEN + 无回归**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 40 PASS（39 原有 + 2 新的 ClearAnimationClock_ 测试 - 1 现有预期 FAIL）即 39 PASS + 1 预期 FAIL。
如果 ClearAnimationClock_ 测试 FAIL（Opacity 仍为 0），说明 BeginAnimation(null) 也不能同步清除 —— 需换备选方案。

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
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 39 PASS + 1 预期 FAIL（透明度测试）。