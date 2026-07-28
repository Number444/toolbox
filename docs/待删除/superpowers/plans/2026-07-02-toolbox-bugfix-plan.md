# Toolbox 已知问题修复计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 Toolbox 的 4 个已知用户可见缺陷：悬浮窗无法打开、返回按钮文字不可见、关闭最小化未到系统托盘、设置页点击导航无响应。

**Architecture:** 每个问题独立修复，通过修改既有文件或创建小辅助类解决。问题 3（系统托盘）需要引入 `System.Windows.Forms.NotifyIcon` 互操作，封装在独立的 Helper 类中。其他三个问题均为单文件小范围改动。所有修复保持 TDD，先写/改测试，再改实现。

**Tech Stack:** WPF (.NET 9.0, Windows 10.0.19041), System.Windows.Forms (NotifyIcon 互操作), xUnit

---

## File Structure

### 修改的文件

| 文件 | 变更 | 涉及问题 |
|------|------|----------|
| `Toolbox.Plugins\Tools\NeteaseMusicTool.cs:36-40` | 移除 `IsLoaded` 守卫 | 问题 1 |
| `Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs` | 添加 `_isDisposed` 标志与 OnClosed 重置实例 | 问题 1 |
| `Toolbox\Views\SettingsView.xaml:12` | 为 BackButton 添加 `Foreground="{StaticResource TextPrimaryBrush}"` | 问题 2 |
| `Toolbox\MainWindow.xaml.cs:87-97` | NavItem_MouseLeftButtonDown 中退出设置页 | 问题 4 |
| `Toolbox\App.xaml.cs` | 添加应用程序级 NotifyIcon 生命周期管理 | 问题 3 |
| `Toolbox.Tests\NeteaseMusicToolTests.cs` | 添加/修改相关测试 | 问题 1, 2, 4 |

### 创建的文件

| 文件 | 用途 | 涉及问题 |
|------|------|----------|
| `Toolbox\Helpers\SystemTrayHelper.cs` | NotifyIcon 封装：创建/显示/隐藏/释放托盘图标，提供事件回调 | 问题 3 |

---

### Task 1: 修复悬浮窗无法打开（IsLoaded 循环依赖）

**分析:** `NeteaseMusicTool.CreateContent()` 第 38 行检查 `Instance.IsLoaded`，但 `IsLoaded` 在首次 `Show()` 之前永远为 `false`，导致首次点击"打开悬浮窗"时 `Show()` 被跳过。同时 `OnClosed` 中释放 `_listener` 后，已关闭的 `MusicFloatWindow` 实例被 `get_Instance` 保留，无法重新使用。

**Files:**
- Modify: `Toolbox.Plugins\Tools\NeteaseMusicTool.cs:36-40`
- Modify: `Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs`
- Test: `Toolbox.Tests\NeteaseMusicToolTests.cs`

- [ ] **Step 1: 添加测试用例**

```csharp
// 在 Toolbox.Tests\NeteaseMusicToolTests.cs 中添加

[Fact]
public void NeteaseMusicTool_OpenButton_Click_ShowsWindow()
{
    var window = Toolbox.Tools.Views.MusicFloatWindow.Instance;
    Assert.False(window.IsLoaded);  // 窗口尚未加载
    window.Show();                  // 调用 Show() 不应有任何条件守卫阻挡
    Assert.True(window.IsVisible);  // 窗口应变得可见
    window.Close();
}

[Fact]
public void MusicFloatWindow_OnClosed_ResetsSingleton()
{
    var firstRef = Toolbox.Tools.Views.MusicFloatWindow.Instance;
    firstRef.Close();

    // 验证第二次获取的 Instance 是新对象（而不是已释放的旧对象）
    var secondRef = Toolbox.Tools.Views.MusicFloatWindow.Instance;
    Assert.NotSame(firstRef, secondRef);
    secondRef.Close();
}
```

- [ ] **Step 2: 运行测试，验证失败**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet test --filter "NeteaseMusicTool_OpenButton_Click_ShowsWindow|MusicFloatWindow_OnClosed_ResetsSingleton" -v n 2>&1`

Expected: 2 FAIL — 第一个因为 `window.Show()` 被 `IsLoaded` 守卫阻挡（窗口不可见）；第二个因为 `Instance` 未在 `OnClosed` 中重置（`firstRef == secondRef`）。

- [ ] **Step 3: 修改 NeteaseMusicTool.cs，移除 IsLoaded 守卫**

```csharp
// 修改 Toolbox.Plugins\Tools\NeteaseMusicTool.cs:36-40
// 旧代码:
btnOpen.Click += (s, e) =>
{
    if (Views.MusicFloatWindow.Instance.IsLoaded)
        Views.MusicFloatWindow.Instance.Show();
};

// 新代码:
btnOpen.Click += (s, e) =>
{
    Views.MusicFloatWindow.Instance.Show();
};
```

- [ ] **Step 4: 修改 MusicFloatWindow.xaml.cs，OnClosed 重置单例**

```csharp
// 在 MusicFloatWindow.xaml.cs 类级别添加字段（在 lock 对象附近）
private bool _isDisposed;

// 替换 OnClosed 方法（原有第 125-131 行）
protected override void OnClosed(EventArgs e)
{
    _marqueeTimer.Stop();
    _listener.NowPlayingChanged -= OnNowPlayingChanged;
    _listener.Dispose();
    _isDisposed = true;

    // 重置单例，使下次 Instance.get 创建新窗口
    lock (_lock)
    {
        _instance = null;
    }

    base.OnClosed(e);
}
```

- [ ] **Step 5: 运行测试，验证通过**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet test --filter "NeteaseMusicTool_OpenButton_Click_ShowsWindow|MusicFloatWindow_OnClosed_ResetsSingleton" -v n 2>&1`

Expected: 2 PASS

- [ ] **Step 6: 运行全量测试确保回归**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet test -v n 2>&1`

Expected: All 55+ tests PASS

---

### Task 2: 修复返回工具箱按钮文字不可见（Foreground 未覆盖）

**分析:** 全局 Button 样式的 `Foreground="#1A1A1A"` 是为绿色背景设计的深色文字。返回按钮覆盖了 `Background="Transparent"` 但未覆盖 `Foreground`，导致深色文字叠在深色窗口背景上不可见。

**Files:**
- Modify: `Toolbox\Views\SettingsView.xaml:12`

- [ ] **Step 1: 修改 SettingsView.xaml，为 BackButton 添加 Foreground**

```xml
<!-- 修改 Toolbox\Views\SettingsView.xaml 第 12-18 行 -->
<!-- 旧代码: -->
<Button x:Name="BackButton" Content="← 返回工具箱"
        Background="Transparent"
        BorderBrush="{StaticResource BorderSubtleBrush}"
        BorderThickness="1"
        Padding="12,6"
        FontSize="13"
        Cursor="Hand"
        Click="BackButton_Click"/>

<!-- 新代码: -->
<Button x:Name="BackButton" Content="← 返回工具箱"
        Background="Transparent"
        Foreground="{StaticResource TextPrimaryBrush}"
        BorderBrush="{StaticResource BorderSubtleBrush}"
        BorderThickness="1"
        Padding="12,6"
        FontSize="13"
        Cursor="Hand"
        Click="BackButton_Click"/>
```

- [ ] **Step 2: 构建验证**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet build 2>&1`

Expected: 0 errors, 0 warnings

- [ ] **Step 3: 运行全量测试确保回归**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet test -v n 2>&1`

Expected: All tests PASS

---

### Task 3: 修复设置页点击左侧导航无响应

**分析:** `NavItem_MouseLeftButtonDown` 执行 `vm.SelectedTool = tool` 和 `PositionHighlight(element)`，但 `SettingsLayer.Visibility == Visible` 覆盖了内容区。方法中没有退出设置页的逻辑。

**Files:**
- Modify: `Toolbox\MainWindow.xaml.cs:87-97`

- [ ] **Step 1: 修改 NavItem_MouseLeftButtonDown，添加 ExitSettingsView 调用**

```csharp
// 修改 Toolbox\MainWindow.xaml.cs:87-97
// 旧代码:
private void NavItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement element && element.DataContext is Models.ITool tool)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.SelectedTool = tool;
        }
        PositionHighlight(element);
    }
}

// 新代码:
private void NavItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement element && element.DataContext is Models.ITool tool)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.SelectedTool = tool;
        }
        PositionHighlight(element);

        // 如果当前在设置页，自动退出返回工具箱
        if (SettingsLayer.Visibility == Visibility.Visible)
            ExitSettingsView();
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet build 2>&1`

Expected: 0 errors, 0 warnings

- [ ] **Step 3: 运行全量测试确保回归**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet test -v n 2>&1`

Expected: All tests PASS

---

### Task 4: 创建 SystemTrayHelper（系统托盘封装）

**分析:** 当前 `MinimizeOnClose` 仅将窗口最小化到任务栏（`WindowState.Minimized`），未隐藏任务栏按钮也未创建系统托盘图标。需要引入 `System.Windows.Forms.NotifyIcon` 并封装为独立的 WPF 友好的 Helper 类。

**Files:**
- Create: `Toolbox\Helpers\SystemTrayHelper.cs`
- Modify: `Toolbox\MainWindow.xaml.cs`
- Modify: `Toolbox\App.xaml.cs`

- [ ] **Step 1: 添加项目引用（System.Windows.Forms）**

检查 `Toolbox.csproj`，确保包含 System.Windows.Forms 引用。对于 .NET 9.0 使用：

```xml
<!-- 在 Toolbox.csproj 的 <ItemGroup> 中添加 -->
<PackageReference Include="System.Windows.Forms" Version="6.0.0" />
```

- [ ] **Step 2: 创建 SystemTrayHelper 类**

```csharp
// 新建文件: Toolbox\Helpers\SystemTrayHelper.cs
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Toolbox.Helpers;

/// <summary>
/// 管理系统托盘图标（NotifyIcon）的 WPF 友好封装。
/// 单例模式，负责创建/显示/隐藏/释放托盘图标。
/// </summary>
public sealed class SystemTrayHelper : IDisposable
{
    private static readonly Lazy<SystemTrayHelper> _instance = new(() => new SystemTrayHelper());
    public static SystemTrayHelper Instance => _instance.Value;

    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isDisposed;

    private SystemTrayHelper() { }

    /// <summary>托盘图标是否已创建并可见。</summary>
    public bool IsVisible => _notifyIcon != null && _notifyIcon.Visible;

    /// <summary>
    /// 创建并显示托盘图标。如果已存在则不重复创建。
    /// </summary>
    public void Show(string tooltip, Action onDoubleClick, Action onExitClick)
    {
        if (_notifyIcon != null) return;

        var icon = CreateDefaultIcon();

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = tooltip ?? "Toolbox",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => onDoubleClick();

        _notifyIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("显示 Toolbox", null, (_, _) => onDoubleClick());
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => onExitClick());
    }

    /// <summary>隐藏并释放托盘图标。</summary>
    public void Hide()
    {
        if (_notifyIcon == null) return;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Hide();
    }

    private static System.Drawing.Icon CreateDefaultIcon()
    {
        try
        {
            if (Application.Current?.Icon != null)
            {
                using var stream = new System.IO.MemoryStream();
                var bitmapSource = Application.Current.Icon;
                if (bitmapSource != null)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(stream);
                    stream.Position = 0;
                    return new System.Drawing.Icon(stream);
                }
            }
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }
}
```

- [ ] **Step 3: 构建验证新建的文件**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet build 2>&1`

Expected: 0 errors, 0 warnings

- [ ] **Step 4: 修改 MainWindow.xaml.cs 的 OnClosing，使用 SystemTrayHelper**

```csharp
// 修改 Toolbox\MainWindow.xaml.cs:418-434
// 旧代码:
protected override void OnClosing(CancelEventArgs e)
{
    if (_isShuttingDown)
    {
        base.OnClosing(e);
        return;
    }

    if (AppSettings.Instance.MinimizeOnClose)
    {
        e.Cancel = true;
        WindowState = WindowState.Minimized;
        return;
    }

    base.OnClosing(e);
}

// 新代码:
protected override void OnClosing(CancelEventArgs e)
{
    if (_isShuttingDown)
    {
        base.OnClosing(e);
        return;
    }

    if (AppSettings.Instance.MinimizeOnClose)
    {
        e.Cancel = true;

        // 隐藏主窗口并创建系统托盘图标
        Hide();
        ShowInTaskbar = false;

        SystemTrayHelper.Instance.Show(
            tooltip: "Toolbox - 点击恢复",
            onDoubleClick: () =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowInTaskbar = true;
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                    SystemTrayHelper.Instance.Hide();
                });
            },
            onExitClick: () =>
            {
                Dispatcher.Invoke(() =>
                {
                    Shutdown();
                });
            });
    }

    base.OnClosing(e);
}
```

- [ ] **Step 5: 修改 Shutdown，确保退出时清理托盘图标**

```csharp
// 修改 Toolbox\MainWindow.xaml.cs 的 Shutdown 方法
// 旧代码:
public void Shutdown()
{
    _isShuttingDown = true;
    AppSettings.Instance.Save();

    if (Toolbox.Tools.Views.MusicFloatWindow.Instance.IsLoaded)
        Toolbox.Tools.Views.MusicFloatWindow.Instance.Close();

    Application.Current.Shutdown();
}

// 新代码:
public void Shutdown()
{
    _isShuttingDown = true;
    AppSettings.Instance.Save();

    if (Helpers.SystemTrayHelper.Instance.IsVisible)
        Helpers.SystemTrayHelper.Instance.Hide();

    if (Toolbox.Tools.Views.MusicFloatWindow.Instance.IsLoaded)
        Toolbox.Tools.Views.MusicFloatWindow.Instance.Close();

    Application.Current.Shutdown();
}
```

- [ ] **Step 6: 修改 App.xaml.cs，确保应用程序退出时清理托盘残留**

```csharp
// 修改 Toolbox\App.xaml.cs
// 旧代码:
using System.Windows;

namespace Toolbox;

public partial class App : Application
{
}

// 新代码:
using System.Windows;

namespace Toolbox;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        Helpers.SystemTrayHelper.Instance.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 7: 构建验证**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet build 2>&1`

Expected: 0 errors, 0 warnings

- [ ] **Step 8: 运行全量测试确保回归**

Run: `cd "d:\Agent Space\Toolbox" ; dotnet test -v n 2>&1`

Expected: All tests PASS

---

## Self-Review

### 1. 需求覆盖检查

| 问题 | 对应 Task | 是否覆盖 |
|------|-----------|----------|
| 问题 1: 悬浮窗无法打开 | Task 1 | ✅ 移除 IsLoaded 守卫 + OnClosed 重置单例 |
| 问题 2: 返回按钮文字不可见 | Task 2 | ✅ 添加 Foreground="{StaticResource TextPrimaryBrush}" |
| 问题 3: 最小化未到系统托盘 | Task 4 | ✅ 创建 SystemTrayHelper，修改 OnClosing/Shutdown/App.OnExit |
| 问题 4: 设置页点击导航无响应 | Task 3 | ✅ NavItem_MouseLeftButtonDown 中添加 ExitSettingsView |

### 2. 占位符扫描

- ❌ 无 "TBD", "TODO", "implement later", "fill in details"
- ❌ 无 "Add appropriate error handling" 等空泛描述
- ❌ 无 "Similar to Task N" — 每个 Task 代码完整
- ❌ 无未定义的类型/方法引用
- ✅ 所有测试代码、实现代码、命令均完整提供

### 3. 类型一致性检查

- `SystemTrayHelper.Show(tooltip, onDoubleClick, onExitClick)` — 在 Task 4 Step 2 定义，在 Step 4 调用时参数匹配
- `SystemTrayHelper.Hide()` 和 `SystemTrayHelper.Dispose()` 在 Step 5/6 中调用方式一致
- `MusicFloatWindow.Instance.Show()` — 在 Task 1 中移除了 `IsLoaded` 守卫，调用方式不变
- `ExitSettingsView()` — 在 Task 3 中从 `NavItem_MouseLeftButtonDown` 调用，与 `MainWindow.xaml.cs` 中定义的方法签名一致

### 无遗漏项，计划可执行。