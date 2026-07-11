using System.Windows;
using System.Windows.Threading;

namespace EmailBlaster.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply the persisted theme (dark by default) before the main window is created so it
        // paints in the right theme with no flash.
        ThemeManager.Initialize();

        base.OnStartup(e);

        // Surface otherwise-fatal UI thread exceptions instead of silently crashing.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
