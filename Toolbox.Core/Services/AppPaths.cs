using System;
using System.IO;

namespace Toolbox.Core.Services;

/// <summary>
/// 应用级路径/命名常量。Debug 构建与 Release 构建完全隔离
/// （数据目录、单实例互斥名、唤起事件名、远程默认端口、自启注册表值名），
/// 使开发调试版可与正式安装版同时运行互不干扰；Release 产物行为与历史版本一致
/// （正式版之间仍保持单实例互斥）。
/// </summary>
public static class AppPaths
{
    /// <summary>数据目录名（%LocalAppData% 下）：Debug 构建用独立目录，避免与正式版互相覆盖配置</summary>
#if DEBUG
    public const string DataFolderName = "Toolbox-Debug";
#else
    public const string DataFolderName = "Toolbox";
#endif

    /// <summary>单实例互斥名（各自独立 → Debug 与正式版可同时运行；同构版本间仍互斥）</summary>
#if DEBUG
    public const string SingleInstanceMutexName = "ToolboxSingleInstanceMutexDebug";
#else
    public const string SingleInstanceMutexName = "ToolboxSingleInstanceMutex";
#endif

    /// <summary>唤起事件名（静默/托盘驻留期间第二实例唤醒窗口的信号；Debug/Release 隔离防串扰）</summary>
#if DEBUG
    public const string ShowRequestEventName = "ToolboxShowRequestEventDebug";
#else
    public const string ShowRequestEventName = "ToolboxShowRequestEvent";
#endif

    /// <summary>远程控制默认端口：Debug 用 8091，避免与正式版 8090 撞端口</summary>
#if DEBUG
    public const string DefaultRemotePort = "8091";
#else
    public const string DefaultRemotePort = "8090";
#endif

    /// <summary>数据目录绝对路径（%LocalAppData%/{DataFolderName}）</summary>
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
}
