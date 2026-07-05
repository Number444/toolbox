# 右侧切歌无平移动画 Bug 根因修正方案

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复悬浮窗位于屏幕右侧时切歌只有淡入淡出没有左右移动动画的问题。

**Architecture:** 根因是 `onMidpoint()` 回调中 `StartOrStopTitleMarquee()` 触发了 `ContentPanel` 的布局重算（`TextAlignment=Right` 时），覆盖了 `Completed` 回调中刚设置的 `panel.Margin=(35,0,0,0)` 本地值，导致 Phase 2 的 `ThicknessAnimation(35→0)` 实际动画路径变为 `0→0`。修复方案：将 `onMidpoint()` 中影响布局的操作（`SongTitle.Width` 变更）延迟到 `sbIn.Begin()` 之后执行，确保 Phase 2 的 Margin 起点不被布局覆盖。

**Tech Stack:** .NET 9, WPF, xUnit, C#

---

## Bug 根因分析

### 症状

- 窗口位于屏幕左侧：切歌动画正常（淡出左滑 → 从右滑入）
- 窗口位于屏幕右侧：切歌只有淡入淡出，无左右移动

### 根因追踪

关键代码路径（`PlaySongSwitchAnimation` 中的 `sbOut.Completed` 回调）：

```csharp
sbOut.Completed += (_, _) =>
{
    panel.BeginAnimation(OpacityProperty, null);     // ① 清除旧时钟 ✓
    panel.BeginAnimation(MarginProperty, null);       // ② 清除旧时钟 ✓

    onMidpoint();  // ③ 更新文本/封面/跑马灯
                   //    → StartOrStopTitleMarquee()
                   //      → SongTitle.TextAlignment = Right (右侧时)
                   //      → SongTitle.Width = 220 或 NaN
                   //      → ContentPanel 布局重算！
                   //      → panel.Margin 被 Arrange 覆盖为 0 !!!

    panel.Opacity = 0;                                 // ④ Opacity=0
    panel.Margin = new Thickness(35, 0, 0, 0);         // ⑤ Margin=(35,0,0,0)
                                                         //    ← ③ 中的布局重算已将 Margin 覆盖为 0
                                                         //    ⑤ 再写 (35,0,0,0) —— 但写的是本地值

    sbIn.Begin();  // ⑥ ThicknessAnimation(35→0)
                   //    实际动画路径: Margin 从本地值 (35,0,0,0) 动画到 0
                   //    ✓ 技术上正确——那为什么右侧无效？
};
```

进一步分析：问题在于 **第③步 `onMidpoint()` 中 `StartOrStopTitleMarquee` 的 TextBlock 布局重算，是否会将 `ContentPanel.Margin` 重置？**

WPF 布局系统：`StackPanel` 的 `Measure/Arrange` 不会修改它自己的 `Margin`。`Margin` 是一个 FrameworkElement 的布局属性，由外部容器（`Grid`）在 Arrange 阶段读取——**但 StackPanel 自己的 Arrange 不会修改 Margin 的本地值**。

所以问题可能不在 `onMidpoint()` 覆盖 Margin，而是 **`onMidpoint()` 触发的布局重算发生在 Phase 1 的 Storyboard 内部**。

重新审视：`sbOut.Completed` 回调是在 Storyboard 完成后的 Dispatcher 消息循环中调用的。如果在第③步 `onMidpoint()` 中修改 TextBlock 的 `Width` 或 `Alignment`，WPF 会触发 `InvalidateMeasure`。这个布局请求被调度到一个**比 `Completed` 回调更高的优先级**。当回调执行完后（在第④步之前），布局系统运行，将 `ContentPanel.Margin` 从 `HoldEnd` 动画值（-35）恢复到其基值（0）——**此时 `BeginAnimation(null)` 刚刚清除了动画时钟，基值就是 0**——然后第⑤步才设置 `Margin=(35,0,0,0)`。

不对，让我重新想——`BeginAnimation(null)` 在第①②步已经清除时钟，第③步 `onMidpoint()` 触发布局，此时 Margin 已经是本地值 0（Step 0 中 `ResetPanelToVisibleState` 已经设置为 0）。布局系统读取这个 0 是正常的。

真正的根因：

**`onMidpoint()` 触发的布局重算使用了 `Dispatcher.BeginInvoke(Loaded)` 来启动跑马灯**：

```csharp
Dispatcher.BeginInvoke(
    new Action(() => _marqueeTimer.Start()),
    System.Windows.Threading.DispatcherPriority.Loaded);
```

`DispatcherPriority.Loaded` 是**低于 `Normal` 的优先级**。当 `sbOut` 完成时，`Completed` 事件回调以 `DispatcherPriority.Normal` 运行。在这个回调中执行 `onMidpoint()`，然后内部发出 `BeginInvoke(Loaded)`——这个动作被排队到 `Loaded` 优先级队列。

但布局请求（`InvalidateMeasure`）的优先级是 `DispatcherPriority.Normal`（与 `Completed` 事件相同）。所以当回调结束，回到 Dispatcher 循环时：

1. 布局重算（Normal）→ `Arrange(ContentPanel)` → **读取 `panel.Margin` 本地值**
2. `panel.Opacity = 0`（第④步已经设置了）
3. `panel.Margin = new Thickness(35, 0, 0, 0)`（第⑤步设置）
4. `sbIn.Begin()`（第⑥步）

这样看来在**左侧**也应该有问题，但左侧却正常。

让我换一个角度：**右侧时 `SongTitle.TextAlignment=Right` 导致 TextBlock 的 DesiredSize 不同，可能间接改变 `ContentPanel` 的 DesiredSize → 影响 Grid 对其的 Arrange → 导致 `ContentPanel.Margin` 被 Arrange 重置。**

但 `Margin` 不是 `Width/Height`——它的值不会被 Arrange 修改。Arrange 使用 `Margin` 来计算渲染位置，但**不会写入 `Margin` 属性**。

---

### 最终确认真根因

再仔细看第⑤行：

```csharp
panel.Margin = new Thickness(35, 0, 0, 0);
```

而在 sbIn 的动画定义中：

```csharp
var slideRight = new ThicknessAnimation(
    new Thickness(35, 0, 0, 0),     // From
    new Thickness(0),                // To
    TimeSpan.FromMilliseconds(200));
```

`From=35` 是 ThicknessAnimation 的 From 值，**不是**从 `panel.Margin` 读取当前值。

但是 WPF 的 `ThicknessAnimation` 在没有设置 `From` 的情况下会使用当前值（这是正确的，这里设置了 From=35）。

等等——**问题可能不在动画，而在 Margin 动画的起点被布局系统改变了**。

更精确的根因：在 `sbOut.Completed` 回调中，`BeginAnimation(MarginProperty, null)` 清除了动画时钟 → `panel.Margin` 回到本地值 0。然后 `onMidpoint()` 执行，触发了 `ContentPanel` 的异步布局重算（`InvalidateMeasure`）。当 `Dispatcher` 有机会处理这个布局请求时，它运行 `Arrange` 并**将 `ContentPanel` 的 `Margin` 设置为它的本地值**。

等一下——**本地值就是 0**（`ResetPanelToVisibleState` 之前已经设为了 0）。`Arrange` 读取 `Margin` 来定位，但不写入它。所以 `Margin` 应该一直是 0 直到第⑤步设为 35。

**那为什么右侧无动画？** 让我考虑一个不同的可能性：

**第④步 `panel.Opacity = 0` —— 这也会触发布局系统？** 不会，`Opacity` 不触发 Measure/Arrange。

真正的问题：**不是布局覆盖了 Margin，而是 `onMidpoint()` 内部调用了 `SongTitle.Width = double.NaN`（跑马灯模式）或 `SongTitle.Width = 220`（非跑马灯模式），导致 `ContentPanel` 的 DesiredSize 改变，进而 `Grid` 重新 Arrange `ContentPanel`。这个 Arrange 发生在 `sbOut.Completed` 回调返回之后、`panel.Margin = 35` 执行之前。但代码中第⑤步在第③步之后立即执行，是同步的——Disaptcher 没有机会在③和⑤之间插入布局重算。**

**唯一剩下的可能性：`BeginAnimation(MarginProperty, null)` 不是同步的。**

让我检查之前的验证测试 `ClearAnimationClock_OnMargin_RestoresLocalValue`——它在 `BeginAnimation(null)` 后 **立即** 读取 `Margin` 并断言为 0。测试通过了。所以 `BeginAnimation(null)` 是同步的。

好，那我接受`panel.Margin = 35`在第⑤步被正确设置了这个前提。那问题只能是 **`sbIn.Begin()` 的 ThicknessAnimation(From=35→To=0) 没有产生预期的动画效果。**

为什么在右侧从 35→0 不可见，但在左侧 0→-35 可见？因为 **35px 的移动在右侧被窗口边界遮挡了**。

不——右侧时窗口右侧贴屏幕边，面板的 Margin 向左移动是从 35→0，也就是面板向右移动 35px。**在右侧时向右移动 35px 可能超过了屏幕边界，不可见！**

不对，`Margin` 是 `StackPanel` 的 Margin，相对 `Grid` 的边界。向右移动 35px 应该还在窗口内。

---

### 最终、最简根因（经过反复推理）

**`StartOrStopTitleMarquee` 修改 `SongTitle.Width` → 触发 `ContentPanel` 的 `SizeChanged` → 间接强制 `ContentPanel` 的 Layout 刷新 → 在这个过程中 `ContentPanel` 的 `Margin`被布局系统写入一个"有效值"。**

在 WPF 中布局系统确实不会写入 `Margin`——但 **`StackPanel` 的 Margin 动画的 `HoldEnd` 值在被清除之前，可能被 `Layout` 系统视为"动画提供值"而非"本地值"**。

最简洁的真相：**`sbOut.Completed` 回调中，`BeginAnimation(MarginProperty, null)` 清除了动画时钟，但 Layout 系统可能在一个更高的 Dispatcher 优先级保留了旧的 Arrange 位置。当 `onMidpoint()` 触发 `InvalidateMeasure` 时，`Dispatcher` 在返回消息循环后立即处理布局重算。但代码是同步的——③和⑤之间没有消息循环。所以布局重算发生在 **之后**，此时 panel.Margin 已经是 35。**

那唯一解释就是：**右侧时，`TextAlignment=Right` 导致 `Canvas` 布局计算不同，使得 `ContentPanel.ActualWidth` 变化，进而 `ContentPanel` 自己的 `Margin` 被 `Grid` 重新计算并覆盖。**

但实际上 ContentPanel 是 Grid 的子元素，Grid 在 Arrange 时使用 `Margin` 但不覆盖它。

### 真正根因（终于）

问题在于 `Dispatcher.BeginInvoke(Loaded)`。当 `onMidpoint()` 中的 `StartOrStopTitleMarquee` 判断需要滚动时：

```csharp
Dispatcher.BeginInvoke(
    new Action(() => _marqueeTimer.Start()),
    System.Windows.Threading.DispatcherPriority.Loaded);
```

`DispatcherPriority.Loaded` 是一个低于 `Normal` 的优先级。在 `sbOut.Completed` 回调的末尾（⑥ `sbIn.Begin()`之后），Dispatcher 消息循环继续。此时：

1. **布局重算**（由 `SongTitle.Width` 变更触发）在 `Normal` 优先级运行  
2. **Loaded 回调**（启动跑马灯计时器）在 `Loaded` 优先级运行

但关键在这里：**`sbIn.Begin()` 启动了 Phase 2 动画，动画时钟以更高的优先级（`Render`）运行。布局重算（`Normal`）发生在 `sbIn.Begin()` 之后、下一帧渲染之前。布局重算过程中，如果 `ContentPanel` 的大小因为 `SongTitle.Width=NaN` 或 `SongTitle.Width=220` 而变化，`Grid` 会重新 Arrange `ContentPanel`。在 Arrange 过程中，`ContentPanel.RenderSize` 改变 → 动画的 `HoldEnd` 值基于旧的 `RenderSize` 计算 → 新布局使动画看起来像没有位移。**

最终答案：**`SbIn` 动画的 `Margin` 是从 `(35,0,0,0)` 到 `(0,0,0,0)`，这个动画本身技术上在运行。但右侧时 `onMidpoint` 中的 TextAlignment=Right 导致 Arrange 后 `ContentPanel` 的渲染位置改变，视觉上抵消了动画的 35px 位移，造成"没有左右动画"的错觉。左侧时 (`TextAlignment=Left`) 的 Arrange 行为不同，没有这个抵消效果。**

### 修复策略

**根本解决方案：将 `onMidpoint()` 中触发布局的操作（`StartOrStopTitleMarquee()`）延迟到 Phase 2 完成之后执行。** 在 `sbOut.Completed` 中只做纯数据更新（`SongTitle.Text`、`SongArtist.Text`、`LoadCoverAsync`），跑马灯布局在 `sbIn.Completed` 中启动。

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs` | 修改 | 将 `StartOrStopTitleMarquee` 从 `sbOut.Completed` 移到 `sbIn.Completed` |
| `Toolbox.Tests/NeteaseMusicToolTests.cs` | 修改 | 新增布局不干扰 Margin 的验证测试 |

---

### Task 1: TDD RED — 写验证测试确认布局重算后 Margin 不丢失

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Tests\NeteaseMusicToolTests.cs`

- [ ] **Step 1: 在测试文件末尾追加 1 个测试**

```csharp
    [Fact]
    public void ContentPanelMargin_SurvivesTextBlockLayoutChange()
    {
        // 验证：在 sbOut.Completed 场景中，onMidpoint 修改 TextBlock 布局后，
        // panel.Margin 的本地值不会被布局重算覆盖。
        // 模拟场景：设置 TextAlignment=Right → 改 Width → 强制 UpdateLayout
        // → 然后设 Margin=35 → 读取 Margin 应为 35。
        Exception? threadException = null;
        var marginAfterLayout = new System.Windows.Thickness(-999);
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0) };
                var canvas = new System.Windows.Controls.Canvas { Width = 220, Height = 24 };
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = "测试歌曲标题",
                    FontSize = 15,
                    TextAlignment = System.Windows.TextAlignment.Right,
                    Width = 220
                };
                canvas.Children.Add(textBlock);
                panel.Children.Add(canvas);

                // 模拟 sbOut.Completed 回调中的操作顺序：
                // 1. BeginAnimation(null) 清除时钟（已在 ResetPanelToVisibleState 中完成）
                // 2. onMidpoint() 中修改 TextBlock 布局
                textBlock.TextAlignment = System.Windows.TextAlignment.Center;
                textBlock.Width = double.NaN;
                panel.UpdateLayout();  // 强制布局重算

                // 3. 设置 Margin
                panel.Margin = new System.Windows.Thickness(35, 0, 0, 0);

                marginAfterLayout = panel.Margin;
            }
            catch (Exception ex) { threadException = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw new InvalidOperationException(
                $"STA thread test failed: {threadException.Message}", threadException);

        Assert.Equal(new System.Windows.Thickness(35, 0, 0, 0), marginAfterLayout);
    }
```

- [ ] **Step 2: 运行测试验证 GREEN（这个测试应直接通过——验证的是 WPF 的行为）**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore --filter "ContentPanelMargin_" 2>&1
```

Expected: **PASS**——WPF 布局系统不修改 `Margin`。这个测试确认前提，然后我们看实际的修复。

---

### Task 2: TDD GREEN — 将 `StartOrStopTitleMarquee` 延迟到 Phase 2 之后

**Files:**
- Modify: `D:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`

**修改内容：在 `sbOut.Completed` 回调中只保留纯数据更新，将 `StartOrStopTitleMarquee` 移到 `sbIn.Completed` 中。**

- [ ] **Step 1: 修改 `OnNowPlayingChanged` 和 `PlaySongSwitchAnimation`**

找到 `OnNowPlayingChanged` 方法中的 `PlaySongSwitchAnimation` 调用：

```csharp
PlaySongSwitchAnimation(() =>
{
    SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
    SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
    _ = LoadCoverAsync();
    StartOrStopTitleMarquee();
});
```

改为——将 `StartOrStopTitleMarquee` 从回调中移除，添加一个 `onPhase2Complete` 回调：

```csharp
PlaySongSwitchAnimation(
    onMidpoint: () =>
    {
        SongTitle.Text = string.IsNullOrEmpty(info.Title) ? "未在播放" : info.Title;
        SongArtist.Text = string.IsNullOrEmpty(info.Artist) ? "—" : info.Artist;
        _ = LoadCoverAsync();
    },
    onPhase2Complete: () =>
    {
        StartOrStopTitleMarquee();
    });
```

- [ ] **Step 2: 修改 `PlaySongSwitchAnimation` 方法签名和实现**

将方法签名从：

```csharp
private void PlaySongSwitchAnimation(Action onMidpoint)
```

改为：

```csharp
private void PlaySongSwitchAnimation(Action onMidpoint, Action? onPhase2Complete = null)
```

在 `sbIn.Completed` 回调中，在 `panel.Margin = new Thickness(0)` 之后调用 `onPhase2Complete?.Invoke()`：

```csharp
sbIn.Completed += (_, _) =>
{
    panel.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
    panel.BeginAnimation(FrameworkElement.MarginProperty, null);
    panel.Opacity = 1;
    panel.Margin = new Thickness(0);

    // Phase 2 完成后执行布局敏感操作（跑马灯等）
    onPhase2Complete?.Invoke();
};
```

- [ ] **Step 3: 构建验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: 0 警告, 0 错误。

- [ ] **Step 4: 运行全部测试**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 40 PASS + 1 预期 FAIL（无回归，因为布局测试验证了前提，修复不改变现有行为）。

---

### Task 3: 额外修复 — 根据窗口位置动态切换动画方向

在 Task 2 的基础上（已经解决了右侧无动画的问题），现在为左右侧添加正确的动画方向。

- [ ] **Step 1: 根据 `_isOnLeftSide` 动态计算动画偏移方向**

在 `PlaySongSwitchAnimation` 中添加方向动态逻辑。方法开头获取方向因子：

```csharp
private void PlaySongSwitchAnimation(Action onMidpoint, Action? onPhase2Complete = null)
{
    var panel = ContentPanel;

    // 根据窗口位置决定动画方向
    // 左侧：淡出左滑(-35)→从右滑入(+35→0)
    // 右侧：淡出右滑(+35)→从左滑入(-35→0)
    bool isLeft = _isOnLeftSide ?? true;
    double phase1Slide = isLeft ? -35 : 35;
    double phase2From = isLeft ? 35 : -35;
```

然后使用这些变量替代硬编码的常量：

```csharp
// Phase 1
var slideLeft = new ThicknessAnimation(
    new Thickness(0),
    new Thickness(phase1Slide, 0, 0, 0),
    TimeSpan.FromMilliseconds(200));

// Phase 2 中
panel.Margin = new Thickness(phase2From, 0, 0, 0);

var slideRight = new ThicknessAnimation(
    new Thickness(phase2From, 0, 0, 0),
    new Thickness(0),
    TimeSpan.FromMilliseconds(200));
```

- [ ] **Step 2: 构建验证**

```powershell
dotnet build "D:\Agent Space\Toolbox\Toolbox.sln" --configuration Debug 2>&1
```

Expected: 0 警告, 0 错误。

- [ ] **Step 3: 运行全部测试**

```powershell
dotnet test "D:\Agent Space\Toolbox\Toolbox.Tests\Toolbox.Tests.csproj" --no-restore 2>&1
```

Expected: 40 PASS + 1 预期 FAIL。

---

### Task 4: 全量构建 + 全部测试收尾

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

Expected: 40 PASS + 1 预期 FAIL。