using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Toolbox.Core.Services;
using Toolbox.Plugins.Services;

namespace Toolbox;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private const string MutexName = "ToolboxSingleInstanceMutex";

    /// <summary>静默驻留唤起事件名（第二实例找不到窗口时 Set，第一个实例据此恢复显示）</summary>
    private const string ShowRequestEventName = "ToolboxShowRequestEvent";
    private static EventWaitHandle? _showRequestEvent;
    private static RegisteredWaitHandle? _showRequestWait;

    /// <summary>
    /// 窗口标题（弹窗标题、托盘提示、单实例窗口查找共用）。
    /// 必须与 MainWindow.xaml 的 Title 完全一致——FindWindow 精确匹配，不一致会导致
    /// 单实例激活永远失败（2026-08-03 审查发现：曾为 "Toolbox" 而 XAML 带 emoji 前缀）。
    /// </summary>
    public const string WindowTitle = "🧰 Toolbox";

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Toolbox", "crash.log");

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // ── 全局异常捕获（三层）──
        // 1. UI 线程未处理异常
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // 2. 非 UI 线程 / 非托管异常
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        // 3. Task 未观察异常
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 尝试创建互斥锁，检测是否已有实例在运行
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例，激活该窗口后退出
            ActivateExistingInstance();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        AppSettings.Instance.Load();
        AudioflowSettings.Instance.Load();

        // 2026-08-10：自启注册表值自动迁移（旧版裸路径 → 当前 exe + --autostart），
        // 升级用户无需手动重新开关自启即可享受静默启动。
        // 后台执行不占启动路径（注册表读 <1ms 且仅升级后首次写；进程极快退出漏写则下次自愈）
        Task.Run(() => AppSettings.Instance.EnsureStartupRegistryValue());

        // 全局替换 TextBox 默认系统右键菜单为主题菜单
        Helpers.TextBoxContextMenuHelper.Register();

        // ── 手动创建主窗口（StartupUri 已移除，2026-08-09 开机自启静默启动）──
        // 注册表自启项带 --autostart 参数；配合 AutoStartSilent（默认开）→ 静默启动：
        // 不显示主界面，后台驻留托盘 + 悬浮窗照常。
        var mainWindow = new MainWindow
        {
            StartSilent = e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase)
                          && AppSettings.Instance.AutoStartSilent
        };
        MainWindow = mainWindow;

        // 命名事件：窗口不可见（静默驻留 / 最小化到托盘）期间第二实例双击 exe，
        // ActivateExistingInstance 找不到可见窗口 → 发信号 → 这里恢复显示。
        // 任何启动路径都注册（含正常显示——窗口可见时第二实例走 FindWindow 置前，事件只是兜底）。
        _showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestEventName);
        _showRequestWait = ThreadPool.RegisterWaitForSingleObject(
            _showRequestEvent,
            (_, _) => mainWindow.Dispatcher.Invoke(mainWindow.RestoreFromTray),
            null, Timeout.Infinite, executeOnlyOnce: false);

        if (mainWindow.StartSilent)
        {
            // 后台服务（托盘/悬浮窗）不依赖窗口显示，直接初始化；失败会回退显示主窗口
            mainWindow.InitializeBackgroundServices();
        }
        else
        {
            mainWindow.Show();
        }
    }

    // ── 异常处理 ──────────────────────────────────────────

    /// <summary>Dispatcher 弹窗节流：同组异常 10s 冷却，连续 5 次判定损坏状态退出（2026-08-03）</summary>
    private static readonly Helpers.CrashThrottle DispatcherThrottle = new(TimeSpan.FromSeconds(10), 5);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);

        if (DispatcherThrottle.IsExcessive)
        {
            // 同异常连续超频 = 损坏状态空转：退出而非无限弹窗（保留 crash.log 供排查）
            System.Diagnostics.Debug.WriteLine("[App] 异常连续出现，判定损坏状态，退出进程");
            ExitAfterRepeatedCrashes();
        }
        else if (DispatcherThrottle.ShouldShow(e.Exception.GetType().Name, e.Exception.Message))
        {
            ShowCrashDialog("DispatcherUnhandledException", e.Exception);
        }
        // 冷却期内：只记日志不弹窗
        e.Handled = true; // 阻止进程崩溃
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        string source = "AppDomain.UnhandledException" + (e.IsTerminating ? " [Terminating]" : "");
        LogCrash(source, ex);
        ShowCrashDialog(source, ex); // 低频事件，直接弹窗
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 2026-08-03：只记日志不弹窗——终结器线程弹模态窗会阻塞终结器且无 UI 上下文
        LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved(); // 阻止进程崩溃
    }

    /// <summary>异常连续出现后的退出：优先 Application.Shutdown（走 OnExit 清理托盘/互斥锁），兜底 Environment.Exit</summary>
    private static void ExitAfterRepeatedCrashes()
    {
        try
        {
            Current?.Shutdown();
            return;
        }
        catch { }
        try { Environment.Exit(1); } catch { }
    }

    /// <summary>crash.log 大小上限，超过则滚动为 crash.1.log（只保留一份旧日志）</summary>
    private const long CrashLogMaxBytes = 2 * 1024 * 1024;

    private static void LogCrash(string source, Exception? ex)
    {
        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            // 追加前滚动：超过上限则把当前日志改名存档，从空文件重新记
            if (File.Exists(CrashLogPath) && new FileInfo(CrashLogPath).Length > CrashLogMaxBytes)
                File.Move(CrashLogPath, CrashLogPath + ".1", overwrite: true);
            File.AppendAllText(CrashLogPath, msg);
        }
        catch { }

        Debug.WriteLine(msg);
    }

    /// <summary>弹窗提示（与日志分离：弹窗按节流/事件频率单独控制，2026-08-03 重构）。
    /// 用原生 MessageBox（非主题弹窗）：崩溃时 WPF 可能处于不稳定状态，原生 Win32 弹窗最安全。</summary>
    private static void ShowCrashDialog(string source, Exception? ex)
    {
        try
        {
            System.Windows.MessageBox.Show(
                $"{source}\n\n{ex?.Message}\n\n{ex?.StackTrace}",
                $"{WindowTitle} 异常捕获",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch (Exception msgEx)
        {
            Debug.WriteLine($"[App] 崩溃弹窗显示失败: {msgEx.Message}");
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        // 退出清理失败（托盘已销毁/互斥锁已释放）不应阻止应用退出
        try
        {
            _showRequestWait?.Unregister(null);
            _showRequestWait = null;
            _showRequestEvent?.Dispose();
            _showRequestEvent = null;

            Helpers.SystemTrayHelper.Instance.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] 退出清理失败: {ex.Message}");
        }
        base.OnExit(e);
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            // 通过窗口标题查找已有实例
            var hwnd = Helpers.Win32Helper.FindWindowByTitle(WindowTitle);
            if (hwnd != IntPtr.Zero && Helpers.Win32Helper.IsWindowVisible(hwnd))
            {
                // 可见：最小化则还原，再置前
                if (Helpers.Win32Helper.IsIconic(hwnd))
                    Helpers.Win32Helper.ShowWindow(hwnd, Helpers.Win32Helper.SW_RESTORE);
                Helpers.Win32Helper.SetForegroundWindow(hwnd);
                return;
            }

            // 无可见窗口 = 已有实例驻留中（静默启动 / 最小化到托盘，窗口隐藏但 hwnd 存在）
            // → 发唤起信号让它恢复显示。SetForegroundWindow 对隐藏窗口无效，必须走事件。
            // 注：若首实例尚在启动（事件未创建）则 OpenExisting 抛异常 → 静默，用户再双击一次即可。
            using var evt = EventWaitHandle.OpenExisting(ShowRequestEventName);
            evt.Set();
        }
        catch
        {
            // 激活失败不影响新实例退出
        }
    }
}

