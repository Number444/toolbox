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