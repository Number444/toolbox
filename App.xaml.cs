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

    private static void LogCrash(string source, Exception? ex)
    {
        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, msg);
        }
        catch { }

        Debug.WriteLine(msg);

        // 弹窗让用户看到崩溃原因
        System.Windows.MessageBox.Show(
            $"{source}\n\n{ex?.Message}\n\n{ex?.StackTrace}",
            "Toolbox 异常捕获",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Helpers.SystemTrayHelper.Instance.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            // 通过窗口标题查找已有实例
            var hwnd = Helpers.Win32Helper.FindWindowByTitle("Toolbox");
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

