using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Services;

namespace Toolbox.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        // 触发一个可路由的 BackRequested 事件
        RaiseEvent(new RoutedEventArgs(BackRequestedEvent));
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow main)
            main.Shutdown();
    }

    public static readonly RoutedEvent BackRequestedEvent =
        EventManager.RegisterRoutedEvent("BackRequested", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(SettingsView));

    public event RoutedEventHandler BackRequested
    {
        add => AddHandler(BackRequestedEvent, value);
        remove => RemoveHandler(BackRequestedEvent, value);
    }
}