using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniStopwatch.Core;

namespace MiniStopwatch.App;

public partial class MainWindow : Window
{
    private const int WmWtsSessionChange = 0x02B1;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int NotifyForThisSession = 0;
    private const int DefaultOpacityPercent = 85;
    private const string SettingsRegistryPath = @"Software\ProductivityTracker";
    private const string LegacySettingsRegistryPath = @"Software\MiniStopwatch";
    private const string OpacityRegistryValue = "OpacityPercent";

    private readonly StopwatchController stopwatch = new(new SystemMonotonicClock());
    private readonly DispatcherTimer displayTimer;
    private readonly MenuItem[] opacityMenuItems;
    private HwndSource? windowSource;

    public MainWindow()
    {
        InitializeComponent();

        opacityMenuItems =
        [
            Opacity40MenuItem,
            Opacity55MenuItem,
            Opacity70MenuItem,
            Opacity85MenuItem,
            Opacity100MenuItem,
        ];
        SetOpacity(LoadOpacityPercent(), persist: false);

        displayTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        displayTimer.Tick += (_, _) => RefreshDisplay();
        displayTimer.Start();
        RefreshDisplay();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(handle);
        windowSource?.AddHook(WindowMessageHook);

        if (!WTSRegisterSessionNotification(handle, NotifyForThisSession))
        {
            throw new InvalidOperationException("Unable to register for Windows session notifications.");
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        displayTimer.Stop();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            WTSUnRegisterSessionNotification(handle);
        }

        windowSource?.RemoveHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmWtsSessionChange)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case WtsSessionLock:
                stopwatch.OnSessionLocked();
                RefreshDisplay();
                break;
            case WtsSessionUnlock:
                stopwatch.OnSessionUnlocked();
                RefreshDisplay();
                break;
        }

        return IntPtr.Zero;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            stopwatch.Toggle();
            RefreshDisplay();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void ToggleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        stopwatch.Toggle();
        RefreshDisplay();
    }

    private void ResetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        stopwatch.Reset();
        RefreshDisplay();
    }

    private void OpacityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } ||
            !int.TryParse(tag, out var opacityPercent))
        {
            throw new InvalidOperationException("The selected transparency value is invalid.");
        }

        SetOpacity(opacityPercent, persist: true);
    }

    private void MinimizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            ShowInTaskbar = false;
            Topmost = true;
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshDisplay()
    {
        TimeDisplay.Text = ElapsedTimeFormatter.Format(stopwatch.Elapsed);
        ToggleMenuItem.Header = stopwatch.IsRunning ? "Stop" : "Start";
        StatusIndicator.Fill = stopwatch.IsRunning
            ? new SolidColorBrush(Color.FromRgb(67, 160, 71))
            : new SolidColorBrush(Color.FromRgb(139, 150, 158));
    }

    private void SetOpacity(int opacityPercent, bool persist)
    {
        if (!opacityMenuItems.Any(item => item.Tag?.ToString() == opacityPercent.ToString()))
        {
            opacityPercent = DefaultOpacityPercent;
        }

        Opacity = opacityPercent / 100d;
        foreach (var item in opacityMenuItems)
        {
            item.IsChecked = item.Tag?.ToString() == opacityPercent.ToString();
        }

        if (persist)
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
            key.SetValue(OpacityRegistryValue, opacityPercent, RegistryValueKind.DWord);
        }
    }

    private static int LoadOpacityPercent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        if (key?.GetValue(OpacityRegistryValue) is int savedOpacity)
        {
            return savedOpacity;
        }

        using var legacyKey = Registry.CurrentUser.OpenSubKey(LegacySettingsRegistryPath);
        return legacyKey?.GetValue(OpacityRegistryValue) is int legacyOpacity
            ? legacyOpacity
            : DefaultOpacityPercent;
    }

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(
        IntPtr windowHandle,
        int flags);

    [DllImport("Wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr windowHandle);
}