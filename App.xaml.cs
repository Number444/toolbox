using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Toolbox.Core.Services;
using Toolbox.Services;

namespace Toolbox;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private const string MutexName = "ToolboxSingleInstanceMutex";

    /// <summary>窗口标题（弹窗标题、托盘提示、单实例窗口查找共用）</summary>
    public const string WindowTitle = "Toolbox";

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

        // 全局替换 TextBox 默认系统右键菜单为主题菜单
        Helpers.TextBoxContextMenuHelper.Register();
    }

    // ── 异常处理 ──────────────────────────────────────────

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);
        e.Handled = true; // 阻止进程崩溃
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogCrash("AppDomain.UnhandledException" + (e.IsTerminating ? " [Terminating]" : ""), ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved(); // 阻止进程崩溃
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

        // 用原生 MessageBox（非主题弹窗）：崩溃时 WPF 可能处于不稳定状态，
        // 创建 WPF Window 可能二次崩溃，原生 Win32 弹窗最安全
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
            if (hwnd != IntPtr.Zero)
            {
                // 如果最小化则还原
                if (Helpers.Win32Helper.IsIconic(hwnd))
                    Helpers.Win32Helper.ShowWindow(hwnd, Helpers.Win32Helper.SW_RESTORE);
                Helpers.Win32Helper.SetForegroundWindow(hwnd);
            }
        }
        catch
        {
            // 激活失败不影响新实例退出
        }
    }
}

