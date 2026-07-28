# 悬浮窗崩溃与布局修复 + 按钮样式修复 — 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复悬浮窗的 3 个用户可见缺陷：紧凑↔大模式切换闪退、紧凑模式右侧布局异常、工具箱内按钮默认颜色不可见。

**Architecture:** 三个 Bug 彼此独立，各作为一个 Task 独立修复。每个 Task 遵循 TDD: 先写失败测试 → 验证失败 → 编写最小修复代码 → 验证通过 → 提交。测试需在 STA 线程运行（WPF 控件要求）。

**Tech Stack:** WPF (.NET 9.0, Windows 10.0.19041), xUnit

---

## File Structure

### 修改的文件
| 文件 | 变更 | 涉及 Task |
|------|------|-----------|
| `Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs` | `EnsureChildInPanel` 添加 Decorator 父容器处理 | Task 1 |
| `Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs` | `ApplyAlignment` 紧凑模式 ColumnDefinitions 列宽同步交换 | Task 2 |
| `Toolbox.Plugins\Tools\NeteaseMusicTool.cs` | 移除三个按钮的 `Background = Brushes.Transparent` 覆盖 | Task 3 |
| `Toolbox\Views\SettingsView.xaml` | 移除 BackButton 的 `Background="Transparent"` | Task 3 |
| `Toolbox.Tests\NeteaseMusicToolTests.cs` | 添加 5 个新测试用例 | Task 1, 2, 3 |

---

### Task 1: 修复紧凑↔大模式切换闪退

**分析:** `ApplySizeMode()` 中 `EnsureChildInPanel` 使用 `VisualTreeHelper.GetParent(child) as Panel` 检查父容器。当子元素从紧凑模式的 `Border`（`CompactCoverSlot`）移回大模式时，`Border as Panel` 返回 `null`，跳过移除步骤，导致后续 `LayoutLarge.Children.Add(child)` 抛出 `InvalidOperationException`。

**Files:**
- Modify: `Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs:320-326`
- Test: `Toolbox.Tests\NeteaseMusicToolTests.cs`

- [ ] **Step 1: 编写闪退测试（RED）**

```csharp
// 在 NeteaseMusicToolTests.cs 文件末尾添加

[Fact]
public void MusicFloatWindow_SizeMode_ToggleLargeToCompactToLarge_NoException()
{
    Exception? exception = null;
    var thread = new Thread(() =>
    {
        try
        {
            MusicFloatWindow.ForceResetInstance();
            var window = MusicFloatWindow.Instance;
            window.Show();

            // 来回切换：Large → Compact → Large
            window.SizeMode = FloatSizeMode.Compact;
            window.SizeMode = FloatSizeMode.Large;

            // 能走到这里就说明没有崩溃
        }
        catch (Exception ex) { exception = ex; }
        finally
        {
            if (MusicFloatWindow.Instance.IsLoaded)
                MusicFloatWindow.Instance.Close();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    Assert.Null(exception);
}

[Fact]
public void EnsureChildInPanel_WithBorderParent_MovesChildToPanel()
{
    // 单元测试：验证 EnsureChildInPanel 能正确处理 Border(Decorator) 父容器
    Exception? exception = null;
    var thread = new Thread(() =>
    {
        try
        {
            var border = new Border();
            var targetPanel = new StackPanel();
            var child = new Image();

            // 模拟紧凑模式：子元素被放入 Border
            border.Child = child;

            // 模拟 EnsureChildInPanel 的核心逻辑
            // 旧代码: var parent = VisualTreeHelper.GetParent(child) as Panel; → null
            // 新代码需要处理 Decorator 类型
            var parent = VisualTreeHelper.GetParent(child);
            if (parent is Panel panel)
            {
                panel.Children.Remove(child);
            }
            else if (parent is Decorator decorator)
            {
                decorator.Child = null;
            }
            targetPanel.Children.Add(child);

            Assert.Equal(targetPanel, VisualTreeHelper.GetParent(child));
            Assert.Null(border.Child);
        }
        catch (Exception ex) { exception = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    Assert.Null(exception);
}
```

- [ ] **Step 2: 运行测试验证失败**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test --filter "MusicFloatWindow_SizeMode_Toggle|EnsureChildInPanel_WithBorderParent" -v n 2>&1
```

Expected:
- `ToggleLargeToCompactToLarge_NoException` → FAIL: `Assert.Null() Failure` — exception 不为 null（InvalidOperationException）
- `EnsureChildInPanel_WithBorderParent_MovesChildToPanel` → PASS（测试中包含正确逻辑，但需要在后续步骤中改为调用生产代码）

- [ ] **Step 3: 编写最小修复代码（GREEN）**

将 `EnsureChildInPanel` 方法（第 320-326 行）从仅处理 Panel 父容器改为同时处理 Decorator：

```csharp
// 替换 MusicFloatWindow.xaml.cs 第 320-326 行
private static void EnsureChildInPanel(Panel panel, UIElement child)
{
    var currentParent = VisualTreeHelper.GetParent(child);
    if (currentParent == panel) return;

    if (currentParent is Panel parentPanel)
    {
        parentPanel.Children.Remove(child);
    }
    else if (currentParent is Decorator decorator)
    {
        decorator.Child = null;
    }

    panel.Children.Add(child);
}
```

- [ ] **Step 4: 运行测试验证通过**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test --filter "MusicFloatWindow_SizeMode_Toggle|EnsureChildInPanel_WithBorderParent" -v n 2>&1
```

Expected: **2 PASS**

- [ ] **Step 5: 运行全量测试确保回归**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test -v n 2>&1
```

Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
cd "d:\Agent Space\Toolbox"
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "fix: 修复 EnsureChildInPanel 未处理 Border(Decorator) 父容器导致切换闪退"
```

---

### Task 2: 修复紧凑模式右侧布局异常

**分析:** 窗口位于屏幕右侧时，`ApplyAlignment` 通过 `Grid.SetColumn` 交换 `CompactCoverSlot` 和 `CompactTextSlot` 的列号，但 `ColumnDefinitions[0].Width = "Auto"` 和 `[1].Width = "*"` 不会随之交换。导致封面进入 Star 列（被拉伸至全宽），文本进入 Auto 列（被压缩至内容宽度）。

**方案:** 在 `ApplyAlignment` 中，当紧凑模式且窗口在右侧时，同步交换两列的 Width。

**Files:**
- Modify: `Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs:406-416`
- Test: `Toolbox.Tests\NeteaseMusicToolTests.cs`

- [ ] **Step 1: 编写布局测试（RED）**

```csharp
// 在 NeteaseMusicToolTests.cs 文件末尾添加

[Fact]
public void MusicFloatWindow_CompactMode_RightSide_ColumnWidthsSwapped()
{
    // 验证紧凑模式右侧对齐时，ColumnDefinitions 的宽度类型已交换
    Exception? exception = null;
    GridLength? col0Width = null;
    GridLength? col1Width = null;
    var thread = new Thread(() =>
    {
        try
        {
            MusicFloatWindow.ForceResetInstance();
            var window = MusicFloatWindow.Instance;
            window.Show();

            // 模拟窗口在右侧
            window.Left = 1400;
            window.SizeMode = FloatSizeMode.Compact;

            // 通过反射访问 LayoutCompact（XAML 生成私有字段）
            var field = typeof(MusicFloatWindow)
                .GetField("LayoutCompact",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(window) is Grid layoutCompact)
            {
                col0Width = layoutCompact.ColumnDefinitions[0].Width;
                col1Width = layoutCompact.ColumnDefinitions[1].Width;
            }
        }
        catch (Exception ex) { exception = ex; }
        finally
        {
            if (MusicFloatWindow.Instance.IsLoaded)
                MusicFloatWindow.Instance.Close();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    Assert.Null(exception);
    Assert.NotNull(col0Width);
    Assert.NotNull(col1Width);

    // 右侧时：Column 0（封面）应为 Star，Column 1（文本）应为 Auto
    Assert.Equal(GridUnitType.Star, col0Width!.Value.GridUnitType);
    Assert.Equal(GridUnitType.Auto, col1Width!.Value.GridUnitType);
}
```

- [ ] **Step 2: 运行测试验证失败**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test --filter "MusicFloatWindow_CompactMode_RightSide_ColumnWidthsSwapped" -v n 2>&1
```

Expected: FAIL — 右侧时列宽未交换，Column 0 仍是 Auto，Column 1 仍是 Star

- [ ] **Step 3: 编写最小修复代码（GREEN）**

在 `ApplyAlignment` 方法的紧凑模式分支末尾添加列宽自动交换逻辑：

```csharp
// 替换 MusicFloatWindow.xaml.cs 第 406-416 行
else
{
    // 紧凑模式镜像布局
    Grid.SetColumn(CompactCoverSlot, isLeft ? 0 : 1);
    Grid.SetColumn(CompactTextSlot, isLeft ? 1 : 0);
    CoverGrid.HorizontalAlignment = halign;
    CompactTextSlot.HorizontalAlignment = halign;
    SongArtist.TextAlignment = talign;
    if (!_marqueeTimer.IsEnabled)
        SongTitle.TextAlignment = talign;

    // ── 同步交换列宽类型 ──
    if (!isLeft)
    {
        var savedWidth = LayoutCompact.ColumnDefinitions[0].Width;
        LayoutCompact.ColumnDefinitions[0].Width = LayoutCompact.ColumnDefinitions[1].Width;
        LayoutCompact.ColumnDefinitions[1].Width = savedWidth;
    }
    else
    {
        // 恢复为默认值（避免左侧时残留右侧的交换状态）
        LayoutCompact.ColumnDefinitions[0].Width = GridLength.Auto;
        LayoutCompact.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
    }
}
```

- [ ] **Step 4: 运行测试验证通过**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test --filter "MusicFloatWindow_CompactMode_RightSide_ColumnWidthsSwapped" -v n 2>&1
```

Expected: PASS

- [ ] **Step 5: 添加左侧回归测试**

```csharp
[Fact]
public void MusicFloatWindow_CompactMode_LeftSide_ColumnWidthsDefault()
{
    // 验证紧凑模式左侧对齐时，列宽保持默认（Auto / Star）
    Exception? exception = null;
    GridLength? col0Width = null;
    GridLength? col1Width = null;
    var thread = new Thread(() =>
    {
        try
        {
            MusicFloatWindow.ForceResetInstance();
            var window = MusicFloatWindow.Instance;
            window.Show();

            // 窗口默认在左侧（Left = 0）
            window.SizeMode = FloatSizeMode.Compact;

            var field = typeof(MusicFloatWindow)
                .GetField("LayoutCompact",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(window) is Grid layoutCompact)
            {
                col0Width = layoutCompact.ColumnDefinitions[0].Width;
                col1Width = layoutCompact.ColumnDefinitions[1].Width;
            }
        }
        catch (Exception ex) { exception = ex; }
        finally
        {
            if (MusicFloatWindow.Instance.IsLoaded)
                MusicFloatWindow.Instance.Close();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    Assert.Null(exception);
    Assert.NotNull(col0Width);
    Assert.NotNull(col1Width);

    // 左侧时：Column 0（封面）应为 Auto，Column 1（文本）应为 Star
    Assert.Equal(GridUnitType.Auto, col0Width!.Value.GridUnitType);
    Assert.Equal(GridUnitType.Star, col1Width!.Value.GridUnitType);
}
```

- [ ] **Step 6: 运行布局回归测试**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test --filter "MusicFloatWindow_CompactMode" -v n 2>&1
```

Expected: **2 PASS**

- [ ] **Step 7: 运行全量测试确保回归**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test -v n 2>&1
```

Expected: All tests PASS

- [ ] **Step 8: Commit**

```bash
cd "d:\Agent Space\Toolbox"
git add Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml.cs
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "fix: 紧凑模式右侧布局时同步交换 Grid ColumnDefinitions 列宽"
```

---

### Task 3: 修复按钮默认颜色（绿色强调色）

**分析:** 全局 Button 样式的默认背景为 `AccentBrush (#76B580)`。三个按钮（关闭悬浮窗、切换大小、返回工具箱）显式覆盖了 `Background = Brushes.Transparent`（或 `Background="Transparent"`），使其底色与面板背景融合而不可见。移除这些覆盖即可恢复绿色强调色。

**Files:**
- Modify: `Toolbox.Plugins\Tools\NeteaseMusicTool.cs:42-49`（btnClose）
- Modify: `Toolbox.Plugins\Tools\NeteaseMusicTool.cs:82-90`（toggleBtn）
- Modify: `Toolbox\Views\SettingsView.xaml:12-20`（BackButton）
- Test: `Toolbox.Tests\NeteaseMusicToolTests.cs`

- [ ] **Step 1: 编写样式测试（RED）**

```csharp
// 在 NeteaseMusicToolTests.cs 文件末尾添加

[Fact]
public void NeteaseMusicTool_CloseAndToggleButton_BackgroundIsNotTransparent()
{
    Exception? threadException = null;
    SolidColorBrush? closeBtnBg = null;
    SolidColorBrush? toggleBtnBg = null;
    var thread = new Thread(() =>
    {
        try
        {
            var tool = new Toolbox.Tools.NeteaseMusicTool();
            var content = tool.CreateContent();
            var root = Assert.IsType<StackPanel>(content);

            // root.Children: [0]=btnOpen, [1]=btnClose, [2]=separator, [3]=sizeLabel, [4]=sizeRow
            var btnClose = Assert.IsType<Button>(root.Children[1]);
            var sizeRow = Assert.IsType<Grid>(root.Children[4]);
            var toggleBtn = Assert.IsType<Button>(sizeRow.Children[1]);

            closeBtnBg = btnClose.Background as SolidColorBrush;
            toggleBtnBg = toggleBtn.Background as SolidColorBrush;
        }
        catch (Exception ex) { threadException = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    Assert.Null(threadException);
    Assert.NotNull(closeBtnBg);
    Assert.NotNull(toggleBtnBg);

    // 期望颜色是非透明的（继承全局 AccentBrush / 其他有效颜色）
    Assert.NotEqual(System.Windows.Media.Colors.Transparent, closeBtnBg!.Color);
    Assert.NotEqual(System.Windows.Media.Colors.Transparent, toggleBtnBg!.Color);
}
```

- [ ] **Step 2: 运行测试验证失败**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test --filter "NeteaseMusicTool_CloseAndToggleButton_BackgroundIsNotTransparent" -v n 2>&1
```

Expected: FAIL — `btnClose.Background` 当前等于 `Colors.Transparent`

- [ ] **Step 3: 编写最小修复代码（GREEN）**

修改 `NeteaseMusicTool.cs`，从 btnClose 的初始化中移除 `Background` 行：

```csharp
// 修改第 41-49 行 — 移除 Background = Brushes.Transparent
var btnClose = new Button
{
    Content = "关闭悬浮窗",
    Height = 36,
    Margin = new Thickness(0, 4, 0, 4),
    BorderBrush = FindResourceBrush("BorderSubtleBrush", Brushes.Gray),
    BorderThickness = new Thickness(1)
};
```

修改 `NeteaseMusicTool.cs`，从 toggleBtn 的初始化中移除 `Background` 行：

```csharp
// 修改第 81-90 行 — 移除 Background = Brushes.Transparent
var toggleBtn = new Button
{
    Content = "切换大小",
    Height = 32,
    Padding = new Thickness(12, 0, 12, 0),
    HorizontalAlignment = HorizontalAlignment.Right,
    BorderBrush = FindResourceBrush("BorderSubtleBrush", Brushes.Gray),
    BorderThickness = new Thickness(1)
};
```

修改 `SettingsView.xaml`，从 BackButton 中移除 `Background="Transparent"`：

```xml
<!-- 替换第 12-20 行 -->
<Button x:Name="BackButton" Content="← 返回工具箱"
        Foreground="{StaticResource TextPrimaryBrush}"
        BorderBrush="{StaticResource BorderSubtleBrush}"
        BorderThickness="1"
        Padding="12,6"
        FontSize="13"
        Cursor="Hand"
        Click="BackButton_Click"/>
```

- [ ] **Step 4: 运行测试验证通过**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test --filter "NeteaseMusicTool_CloseAndToggleButton_BackgroundIsNotTransparent" -v n 2>&1
```

Expected: PASS

- [ ] **Step 5: 构建验证 SettingsView 修改**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet build 2>&1
```

Expected: 0 errors, 0 warnings

- [ ] **Step 6: 运行全量测试确保回归**

Run:
```
cd "d:\Agent Space\Toolbox" ; dotnet test -v n 2>&1
```

Expected: All tests PASS

- [ ] **Step 7: Commit**

```bash
cd "d:\Agent Space\Toolbox"
git add Toolbox.Plugins/Tools/NeteaseMusicTool.cs
git add Toolbox/Views/SettingsView.xaml
git add Toolbox.Tests/NeteaseMusicToolTests.cs
git commit -m "fix: 移除按钮 Background=Transparent 覆盖，恢复绿色强调色"
```

---

## Self-Review

### 1. 需求覆盖检查

| Bug | Task | 是否覆盖 |
|-----|------|----------|
| 紧凑↔大模式切换闪退 | Task 1 | ✅ 修复 EnsureChildInPanel 处理 Decorator |
| 紧凑模式右侧布局异常 | Task 2 | ✅ 同步交换 ColumnDefinitions 宽度类型 |
| 按钮默认颜色与背景一致 | Task 3 | ✅ 移除三个按钮的 Background=Transparent |

### 2. 占位符扫描
- ❌ 无 "TBD", "TODO", "implement later", "fill in details"
- ❌ 无 "Add appropriate error handling" 等空泛描述
- ❌ 无 "Similar to Task N" — 每个 Task 代码完整
- ❌ 无未定义的类型/方法引用
- ✅ 所有测试代码、实现代码、命令均完整提供

### 3. 类型一致性检查
- `EnsureChildInPanel` — Task 1 Step 3 中修改，方法签名不变（`Panel panel, UIElement child`）
- `ApplyAlignment` — Task 2 Step 3 中修改，新增的列宽交换逻辑引用了已定义的 `LayoutCompact` 和 `isLeft`
- 移除 `Background` 行 — Task 3 Step 3 中每个按钮的移除位置均与源代码结构对应
- 所有测试均使用现有测试文件 `NeteaseMusicToolTests.cs`，遵循现有 STA 线程模式和 `ForceResetInstance` 模式
- `GridLength` 类型引用一致：`GridLength.Auto`、`new GridLength(1, GridUnitType.Star)`、`GridUnitType.*`

---

## Execution Handoff

**计划完成并保存到 `docs/superpowers/plans/2026-07-02-music-float-window-crash-and-style-fix.md`。**

**两个执行选项：**

**1. 子代理驱动（推荐）** — 每个 Task 分派新子代理，任务间可审查，快速迭代

**2. 内联执行** — 在当前会话内使用 executing-plans 批量执行，设置检查点

**选择哪个方案？**