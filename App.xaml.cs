using System.Threading;
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

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
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

