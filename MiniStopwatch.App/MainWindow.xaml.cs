using System.Media;
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
    private const uint FlashWindowAll = 0x00000003;

    private readonly TrackingController tracker = new(new SystemMonotonicClock());
    private readonly DispatcherTimer displayTimer;
    private readonly DispatcherTimer completionFlashTimer;
    private readonly MenuItem[] opacityMenuItems;
    private readonly SolidColorBrush normalBorderBrush =
        new(Color.FromArgb(0x7F, 0x9A, 0xA0, 0xA5));
    private readonly SolidColorBrush completionBrush =
        new(Color.FromRgb(0xE5, 0x39, 0x35));
    private HwndSource? windowSource;
    private int completionFlashStep;
    private bool isCompletionFlashing;

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

        completionFlashTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        completionFlashTimer.Tick += CompletionFlashTimer_Tick;
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
        completionFlashTimer.Stop();

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
                tracker.OnSessionLocked();
                RefreshDisplay();
                break;
            case WtsSessionUnlock:
                tracker.OnSessionUnlocked();
                RefreshDisplay();
                break;
        }

        return IntPtr.Zero;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            ToggleTracking();
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
        ToggleTracking();
    }

    private void ResetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        StopCompletionAlert();
        tracker.Reset();
        RefreshDisplay();
    }

    private void TimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TimerDialog
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        StopCompletionAlert();
        tracker.StartTimer(dialog.Duration);
        RefreshDisplay();
    }

    private void ExitTimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        StopCompletionAlert();
        tracker.ExitTimer();
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
        if (tracker.Update())
        {
            StartCompletionAlert();
        }

        TimeDisplay.Text = ElapsedTimeFormatter.Format(tracker.DisplayTime);
        ExitTimerMenuItem.Visibility = tracker.IsTimerMode
            ? Visibility.Visible
            : Visibility.Collapsed;

        ToggleMenuItem.Header = tracker.IsTimerMode
            ? tracker.IsRunning
                ? "Pause Timer"
                : tracker.IsTimerCompleted
                    ? "Restart Timer"
                    : "Resume Timer"
            : tracker.IsRunning
                ? "Stop"
                : "Start";

        TimeDisplay.Foreground = tracker.IsTimerCompleted
            ? completionBrush
            : new SolidColorBrush(Color.FromRgb(0x72, 0x7D, 0x86));

        if (!isCompletionFlashing)
        {
            StatusIndicator.Fill = tracker.IsTimerCompleted
                ? completionBrush
                : tracker.IsRunning
                    ? new SolidColorBrush(Color.FromRgb(67, 160, 71))
                    : new SolidColorBrush(Color.FromRgb(139, 150, 158));
        }
    }

    private void ToggleTracking()
    {
        if (tracker.IsTimerCompleted)
        {
            StopCompletionAlert();
        }

        tracker.Toggle();
        RefreshDisplay();
    }

    private void StartCompletionAlert()
    {
        SystemSounds.Exclamation.Play();
        isCompletionFlashing = true;
        completionFlashStep = 0;
        ApplyCompletionFlash(isHighlighted: true);
        completionFlashTimer.Start();

        var flashInfo = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            WindowHandle = new WindowInteropHelper(this).Handle,
            Flags = FlashWindowAll,
            Count = 5,
            Timeout = 0,
        };
        FlashWindowEx(ref flashInfo);
    }

    private void CompletionFlashTimer_Tick(object? sender, EventArgs e)
    {
        completionFlashStep++;
        if (completionFlashStep >= 8)
        {
            StopCompletionAlert();
            RefreshDisplay();
            return;
        }

        ApplyCompletionFlash(completionFlashStep % 2 == 0);
    }

    private void ApplyCompletionFlash(bool isHighlighted)
    {
        TrackerBorder.BorderBrush = isHighlighted ? completionBrush : normalBorderBrush;
        StatusIndicator.Fill = isHighlighted
            ? completionBrush
            : new SolidColorBrush(Color.FromRgb(139, 150, 158));
    }

    private void StopCompletionAlert()
    {
        completionFlashTimer.Stop();
        isCompletionFlashing = false;
        completionFlashStep = 0;
        TrackerBorder.BorderBrush = normalBorderBrush;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo flashInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}