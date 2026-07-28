# Toolbox 单文件可执行发布实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Toolbox 发布为单个 .exe 文件，可在其他 Windows 11 电脑上直接运行，无需安装 .NET 9 运行时。

**架构：** 使用 .NET 9 内置的 `PublishSingleFile` + `SelfContained` 将整个应用（含 .NET 运行时、所有引用程序集、NuGet 依赖）打包到一个 exe 中。关键修改点：`ToolRegistry.cs` 的插件加载方式需适配单文件模式——从 `Assembly.LoadFrom(path)` 改为 `Assembly.Load("Toolbox.Plugins")`，因为单文件发布后 DLL 不在磁盘上，只能通过程序集名称由默认加载上下文解析。

**Tech Stack:** .NET 9 / WPF / dotnet publish / self-contained single-file

**涉及文件：**
- Modify: `Toolbox.csproj`（添加发布属性）
- Modify: `Services/ToolRegistry.cs`（适配单文件模式下的程序集加载）
- Create: 发布脚本（可选）

---

### Task 1: 修改 ToolRegistry.cs，适配单文件模式下的程序集加载

**Files:**
- Modify: `Services/ToolRegistry.cs`（全部，约 64 行）

**修改内容：**
- `DiscoverTools()` 当前用 `Assembly.LoadFrom(pluginPath)`（行 33），单文件发布后 plugins/Toolbox.Plugins.dll 不在磁盘上
- 改为：先尝试 `Assembly.Load("Toolbox.Plugins")`（利用默认加载上下文，单文件模式下有效），失败后回退到 `Assembly.LoadFrom`（调试/常规构建模式）
- 同时保留 `pluginsDir` 存在性检查的前置降级逻辑

- [ ] **Step 1: 替换 ToolRegistry.cs**

将以下内容：

```csharp
using System.Reflection;
using Toolbox.Models;

namespace Toolbox.Services;

/// <summary>
/// 工具注册中心 —— 自动扫描 Toolbox.Plugins 程序集中的 ITool 实现。
/// 添加新工具只需在插件项目中新增类文件，无需修改主程序。
/// </summary>
public class ToolRegistry
{
    public List<ITool> Tools { get; } = [];

    /// <summary>从 plugins 目录加载插件程序集，自动发现所有实现了 ITool 的类</summary>
    public void DiscoverTools()
    {
        var toolType = typeof(ITool);

        // 从运行目录下的 plugins 子目录加载插件 DLL（热插拔部署）
        string pluginsDir = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "plugins");
        string pluginPath = System.IO.Path.Combine(pluginsDir, "Toolbox.Plugins.dll");

        if (!System.IO.File.Exists(pluginPath))
        {
            // 插件不存在时优雅降级（不崩溃，无工具显示）
            return;
        }

        Assembly pluginAssembly;
        try
        {
            pluginAssembly = Assembly.LoadFrom(pluginPath);
        }
        catch
        {
            // 插件加载失败时优雅降级
            return;
        }

        // GetTypes() 遇到旧版 DLL（缺少 Category 实现等）会抛出 ReflectionTypeLoadException
        // 取已成功加载的类型子集继续扫描，失败的自动跳过
        Type[] allTypes;
        try
        {
            allTypes = pluginAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray()!;
        }

        var toolTypes = allTypes
            .Where(t => toolType.IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

        foreach (var type in toolTypes)
        {
            if (Activator.CreateInstance(type) is ITool tool)
                Tools.Add(tool);
        }

        // 按名称排序
        Tools.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }
}
```

替换为：

```csharp
using System.Reflection;
using Toolbox.Models;

namespace Toolbox.Services;

/// <summary>
/// 工具注册中心 —— 自动扫描 Toolbox.Plugins 程序集中的 ITool 实现。
/// 适配单文件发布模式：先用 Assembly.Load（默认加载上下文）
/// 不满足时回退到 Assembly.LoadFrom（常规构建/调试模式）。
/// </summary>
public class ToolRegistry
{
    public List<ITool> Tools { get; } = [];

    /// <summary>加载插件程序集，自动发现所有实现了 ITool 的类</summary>
    public void DiscoverTools()
    {
        var toolType = typeof(ITool);

        // 尝试三种加载策略，按优先级递减
        Assembly? pluginAssembly = TryLoadFromDefaultContext()
                                  ?? TryLoadFromPluginsDir()
                                  ?? TryLoadFromBaseDir();

        if (pluginAssembly == null)
        {
            // 所有加载方式均失败时优雅降级
            return;
        }

        // GetTypes() 遇到旧版 DLL（缺少 Category 实现等）会抛出 ReflectionTypeLoadException
        // 取已成功加载的类型子集继续扫描，失败的自动跳过
        Type[] allTypes;
        try
        {
            allTypes = pluginAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray()!;
        }

        var toolTypes = allTypes
            .Where(t => toolType.IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

        foreach (var type in toolTypes)
        {
            if (Activator.CreateInstance(type) is ITool tool)
                Tools.Add(tool);
        }

        // 按名称排序
        Tools.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    /// <summary>策略 1：通过程序集名称从默认加载上下文加载（单文件发布模式有效）</summary>
    private static Assembly? TryLoadFromDefaultContext()
    {
        try
        {
            // Toolbox.csproj 有 ProjectReference 引用，单文件发布后
            // .NET 宿主将嵌入式程序集提取到 temp 目录并注册到默认加载上下文。
            // Assembly.Load 使用程序集名称通过已注册上下文解析。
            return Assembly.Load("Toolbox.Plugins");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>策略 2：从运行目录下的 plugins/ 子目录加载（常规构建/调试模式）</summary>
    private static Assembly? TryLoadFromPluginsDir()
    {
        string pluginsDir = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "plugins");
        string pluginPath = System.IO.Path.Combine(pluginsDir, "Toolbox.Plugins.dll");

        if (!System.IO.File.Exists(pluginPath))
            return null;

        try
        {
            return Assembly.LoadFrom(pluginPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>策略 3：从运行目录（BaseDirectory）直接加载（IDE 调试等无 plugins 子目录的场景）</summary>
    private static Assembly? TryLoadFromBaseDir()
    {
        string fallbackPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Toolbox.Plugins.dll");

        if (!System.IO.File.Exists(fallbackPath))
            return null;

        try
        {
            return Assembly.LoadFrom(fallbackPath);
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 2: 验证编译通过**

```powershell
cd "d:\Agent Space\Toolbox"
dotnet build -c Debug --no-restore 2>&1 | Select-String -Pattern "error|Error|CS"
```

Expected: 无错误输出。

- [ ] **Step 3: 提交**

```bash
git add Services/ToolRegistry.cs
git commit -m "fix: adapt plugin loading for single-file publish
- 新增三层加载策略：Assembly.Load → plugins/LoadFrom → baseDir/LoadFrom
- 单文件模式下 Assembly.Load('Toolbox.Plugins') 利用默认加载上下文解析
- 常规模式回退 Assembly.LoadFrom 保持向后兼容"
```

---

### Task 2: 添加单文件发布配置到 Toolbox.csproj

**Files:**
- Modify: `Toolbox.csproj`（全部，约 32 行）

**修改内容：**
- 添加 `.csproj.user` 文件中通常定义的发布属性直接到 csproj 的 PropertyGroup
- 添加 `RuntimeIdentifier`、`SelfContained`、`PublishSingleFile`、`IncludeAllContentForSelfExtract`
- 不启用 `PublishTrimmed`（WPF + 反射代码在 trimming 下会崩，且节省空间有限）
- 注意：`RuntimeIdentifier` 不能写死到默认 PropertyGroup（会影响 IDE 调试），通过添加独立的 Release 条件 PropertyGroup 或使用发布时命令行参数来避免。最佳实践：csproj 只在特定条件（IsPublishing）下设置 RID

- [ ] **Step 1: 修改 Toolbox.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <NoWarn>CA1416</NoWarn>
  </PropertyGroup>

  <!-- 发布配置：仅在 dotnet publish 时生效，不影响 dotnet build / IDE 调试 -->
  <PropertyGroup Condition="'$(Configuration)' == 'Release' And '$(PublishProfile)' != ''">
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
    <!-- 不启用 PublishTrimmed：WPF + ToolRegistry 的反射 GetTypes() + Activator.CreateInstance 在 trimming 下会崩 -->
  </PropertyGroup>

  <!-- 排除子项目目录中的源代码，避免重复编译导致运行时类型冲突 -->
  <ItemGroup>
    <Compile Remove="Toolbox.Core\**\*.cs" />
    <Compile Remove="Toolbox.Plugins\**\*.cs" />
    <Compile Remove="Toolbox.Tests\**\*.cs" />
    <Page Remove="Toolbox.Core\**\*.xaml" />
    <Page Remove="Toolbox.Plugins\**\*.xaml" />
  </ItemGroup>

  <!-- 但保留子项目的 .xaml 文件可能需要的资源 -->
  <ItemGroup>
    <None Remove="Toolbox.Core\**\*.cs" />
    <None Remove="Toolbox.Plugins\**\*.cs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="Toolbox.Core\Toolbox.Core.csproj" />
    <ProjectReference Include="Toolbox.Plugins\Toolbox.Plugins.csproj" />
  </ItemGroup>

</Project>
```

注意：这应该通过 `SearchReplace` 或直接编辑来完成。先读取确认当前内容，然后替换。

- [ ] **Step 2: 验证 csproj 语法正确**

```powershell
Select-String -Path "d:\Agent Space\Toolbox\Toolbox.csproj" -Pattern "PublishSingleFile|SelfContained|RuntimeIdentifier|IncludeAllContent"
```

Expected: 4 行全部匹配，无拼写错误。

- [ ] **Step 3: 确认 Debug 构建仍正常**

```powershell
cd "d:\Agent Space\Toolbox"
dotnet build -c Debug --no-restore 2>&1 | Select-String -Pattern "error|Error|CS"
```

Expected: 无错误（`RuntimeIdentifier`、`PublishSingleFile` 等属性仅在 `Release+IsPublishing` 条件下激活，不影响 Debug build）。

- [ ] **Step 4: 提交**

```bash
git add Toolbox.csproj
git commit -m "build: add self-contained single-file publish config
- RuntimeIdentifier=win-x64, SelfContained=true, PublishSingleFile=true
- IncludeAllContentForSelfExtract=true 确保所有程序集嵌入单 exe
- 仅在 Release+IsPublishing 条件下激活，不影响 IDE 调试
- PublishTrimmed=false（WPF + 反射不兼容 trimming）"
```

---

### Task 3: 执行发布并验证输出

**文件：**
- 输出目录：`bin/Release/net9.0-windows10.0.19041.0/win-x64/publish/`

- [ ] **Step 1: 执行 Release 发布**

```powershell
cd "d:\Agent Space\Toolbox"
dotnet publish -c Release 2>&1
```

Expected: 成功后输出 `Toolbox.exe → bin/Release/net9.0-windows10.0.19041.0/win-x64/publish/Toolbox.exe`

- [ ] **Step 2: 验证发布产物**

```powershell
# 检查是否只有一个 .exe 文件（可能仍有一些 .pdb 等辅助文件）
Get-ChildItem "bin/Release/net9.0-windows10.0.19041.0/win-x64/publish/" -Name

# 检查 exe 大小（正常应在 50-100MB 左右，包含 .NET 运行时）
Get-Item "bin/Release/net9.0-windows10.0.19041.0/win-x64/publish/Toolbox.exe" | Select-Object Length
```

Expected:
- 主要输出文件是 `Toolbox.exe`（单个 exe）
- `.pdb` 文件可能出现，可忽略或删除
- exe 大小 > 40MB（自包含运行时）

- [ ] **Step 3: 确认无额外的 DLL**

```powershell
$files = Get-ChildItem "bin/Release/net9.0-windows10.0.19041.0/win-x64/publish/" -Filter "*.dll"
if ($files.Count -eq 0) { "✅ 无 DLL 残留" } else { "⚠️ 发现 $($files.Count) 个 DLL: $($files.Name -join ', ')" }
```

Expected: 由于 `IncludeAllContentForSelfExtract=true`，所有 DLL 应在首次启动时由宿主程序提取，publish 目录下不应有插件相关的 `.dll` 文件。但是 System.\* 等运行时 DLL 可能仍然出现——这是正常的。

实际上，单文件发布后 `.exe` 是同目录下唯一需要的文件，但 .NET SDK 仍可能输出一些辅助文件（.pdb、.config 等）。关键是没有 `Toolbox.Plugins.dll`、`Toolbox.Core.dll`、`QRCoder.dll` 等应用 DLL。

- [ ] **Step 4: 复制到目标机器测试**

用户自行将 `Toolbox.exe` 复制到另一台 Windows 11 电脑，双击运行验证。

预期表现：
- 程序正常启动
- 所有工具功能正常（包括从 Toolbox.Plugins 加载的工具）
- 音乐悬浮窗功能正常

如果目标机器缺少 VC++ 运行时，可能需要在目标机器安装 [vc_redist.x64.exe](https://aka.ms/vs/17/release/vc_redist.x64.exe)（.NET 9 自包含发布仍依赖 VC++ 运行时）。这一步应告知用户。

- [ ] **Step 5: 提交**

```bash
# 添加 .gitignore 排除 publish 输出
git add Toolbox.csproj Services/ToolRegistry.cs
git commit -m "feat: enable single-file publish for portable deployment"
```

---

### 部署后注意事项

1. **首次启动较慢**（10-30 秒）：单文件 exe 首次运行时会自解压到临时目录，后续启动恢复正常速度。
2. **vc_redist 依赖**：.NET 9 自包含发布仍依赖 VC++ 运行时。如果目标机器是"干净"的 Win11 系统，需要先安装 [vc_redist.x64.exe](https://aka.ms/vs/17/release/vc_redist.x64.exe)。
3. **Windows Defender 可能误报**：单文件 exe 可能被 Defender 标记为可疑（因为内含嵌入的 DLL）。这属于正常现象，提交给 Microsoft 的合法软件无需处理。