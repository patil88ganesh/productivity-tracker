using System.Threading;
using System.Windows;

namespace MiniStopwatch.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName =
        @"Local\ProductivityTracker.SingleInstance";
    private Mutex? singleInstanceMutex;
    private bool ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var isFirstInstance);
        ownsSingleInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Productivity Tracker is already running.",
                "Productivity Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (singleInstanceMutex != null)
        {
            if (ownsSingleInstanceMutex)
            {
                singleInstanceMutex.ReleaseMutex();
            }
            singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
