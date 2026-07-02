using Toolbox.Core.Services;

namespace Toolbox;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        AppSettings.Instance.Load();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Helpers.SystemTrayHelper.Instance.Dispose();
        base.OnExit(e);
    }
}

