using System.Reflection;
using Toolbox.Core.Models;
using Toolbox.Models;

namespace Toolbox.Services;

/// <summary>
/// 工具注册中心 —— 自动扫描 Toolbox.Plugins 程序集中的 ITool 实现。
/// 加载策略唯一：Assembly.Load（默认加载上下文）。
/// 主程序对插件是 ProjectReference + 编译期静态绑定，插件 DLL 必然在主输出目录
/// 且登记于 deps.json（单文件发布时由宿主提取到默认加载上下文），故 Assembly.Load
/// 恒成功；若此路径失败则主程序自身已无法启动，其他降级路径无意义（旧策略 2/3 已删）。
/// </summary>
public class ToolRegistry
{
    public List<ITool> Tools { get; } = [];

    /// <summary>加载插件程序集，自动发现所有实现了 ITool 的类</summary>
    public void DiscoverTools()
    {
        var toolType = typeof(ITool);

        // 唯一加载策略：通过程序集名称从默认加载上下文加载
        Assembly? pluginAssembly = TryLoadFromDefaultContext();

        if (pluginAssembly == null)
        {
            // 所有加载方式均失败时优雅降级
            return;
        }

        // 注册悬浮窗控制器（反射获取插件实现，保持 ToolRegistry 只编译期依赖 Core）
        var controllerType = pluginAssembly.GetType("Toolbox.Tools.Views.MusicFloatWindowManager");
        if (controllerType?.GetProperty("Instance")?.GetValue(null) is IMusicFloatController controller)
            MusicFloatControllerHost.Register(controller);

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
            // 单个工具实例化失败（构造函数异常/缺依赖）只跳过该工具，继续发现其余工具
            try
            {
                if (Activator.CreateInstance(type) is ITool tool)
                    Tools.Add(tool);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolRegistry] 工具 {type.FullName} 实例化失败，已跳过: {ex.Message}");
            }
        }

        // 按名称排序
        Tools.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    /// <summary>通过程序集名称从默认加载上下文加载（单文件发布模式有效）</summary>
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
}