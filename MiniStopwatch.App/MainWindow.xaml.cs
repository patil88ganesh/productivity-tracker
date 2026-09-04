using System.Media;
using System.Diagnostics;
using System.IO;
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
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcRightButtonDown = 0x00A4;
    private const int WmNcMiddleButtonDown = 0x00A7;
    private const int WmWtsSessionChange = 0x02B1;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int NotifyForThisSession = 0;
    private const int DefaultOpacityPercent = 85;
    private const string SettingsRegistryPath = @"Software\ProductivityTracker";
    private const string LegacySettingsRegistryPath = @"Software\MiniStopwatch";
    private const string OpacityRegistryValue = "OpacityPercent";
    private const string WidthRegistryValue = "WindowWidth";
    private const string HeightRegistryValue = "WindowHeight";
    private const string SocialMediaPauseRegistryValue = "SocialMediaPauseEnabled";
    private const string BrowserSetupShownRegistryValue = "BrowserSetupShown";
    private const string NativeHostName = "com.patil88ganesh.productivity_tracker";
    private const string NativeHostManifestFile = "native-messaging-host.json";
    private const string NativeHostExecutable = "ProductivityTracker.NativeHost.exe";
    private const string ExtensionId = "dhnpejafolnigilfhbbdiaanpfegpggd";
    private const string ChromeNativeHostRegistryPath =
        @"Software\Google\Chrome\NativeMessagingHosts\com.patil88ganesh.productivity_tracker";
    private const string EdgeNativeHostRegistryPath =
        @"Software\Microsoft\Edge\NativeMessagingHosts\com.patil88ganesh.productivity_tracker";
    private const uint FlashWindowAll = 0x00000003;
    private const double DefaultWidth = 184;
    private const double DefaultHeight = 58;
    private const double StatsWindowGap = 4;

    private readonly TrackingController tracker = new(new SystemMonotonicClock());
    private readonly DailyStatsStore dailyStatsStore;
    private readonly DispatcherTimer displayTimer;
    private readonly DispatcherTimer completionFlashTimer;
    private readonly MenuItem[] opacityMenuItems;
    private readonly SocialMediaPauseBridge socialMediaPauseBridge;
    private readonly SolidColorBrush normalBorderBrush =
        new(Color.FromArgb(0x7F, 0x9A, 0xA0, 0xA5));
    private readonly SolidColorBrush hoverBorderBrush =
        new(Color.FromRgb(0x2D, 0x96, 0xE8));
    private readonly SolidColorBrush normalBackgroundBrush = new(Colors.White);
    private readonly SolidColorBrush hoverBackgroundBrush =
        new(Color.FromRgb(0xF1, 0xF9, 0xFF));
    private readonly SolidColorBrush completionBrush =
        new(Color.FromRgb(0xFF, 0x17, 0x44));
    private readonly SolidColorBrush automaticPauseBrush =
        new(Color.FromRgb(0xFF, 0x8F, 0x00));
    private readonly SolidColorBrush runningBrush =
        new(Color.FromRgb(0x00, 0xC8, 0x53));
    private readonly SolidColorBrush pausedBrush =
        new(Color.FromRgb(0x59, 0x63, 0x6E));
    private HwndSource? windowSource;
    private int completionFlashStep;
    private bool isCompletionFlashing;
    private bool isPointerOver;
    private bool isClosing;
    private bool socialMediaPauseEnabled;
    private bool browserReportsDistractingSite;
    private StatsWindow? statsWindow;

    public MainWindow()
    {
        InitializeComponent();
        dailyStatsStore = DailyStatsStore.Load(ReportStatsPersistenceError);

        opacityMenuItems =
        [
            Opacity40MenuItem,
            Opacity55MenuItem,
            Opacity70MenuItem,
            Opacity85MenuItem,
            Opacity100MenuItem,
        ];
        SetOpacity(LoadOpacityPercent(), persist: false);
        LoadWindowSize();
        socialMediaPauseEnabled = LoadBooleanSetting(SocialMediaPauseRegistryValue);
        SocialMediaPauseMenuItem.IsChecked = socialMediaPauseEnabled;
        socialMediaPauseBridge = new SocialMediaPauseBridge(OnBrowserActivityChanged);

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
        isClosing = true;
        dailyStatsStore.Sample(tracker.IsRunning, GetMaximumStatsDuration());
        dailyStatsStore.Save();
        displayTimer.Stop();
        completionFlashTimer.Stop();
        socialMediaPauseBridge.Dispose();
        statsWindow?.Close();
        SaveWindowSize();

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
        if (message is WmNcLeftButtonDown or WmNcRightButtonDown or WmNcMiddleButtonDown)
        {
            HideStatsWindow();
        }

        if (message == WmNcHitTest)
        {
            var resizeResult = GetResizeHitTest(hwnd, lParam);
            if (resizeResult != ResizeRegion.Client)
            {
                handled = true;
                return (IntPtr)(int)resizeResult;
            }
        }

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
        HideStatsWindow();

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

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        HideStatsWindow();
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
        var dialog = new TimerDialog(DurationDialogMode.CountdownTimer)
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

    private void AddAndStartMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TimerDialog(DurationDialogMode.AddAndStart)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        StopCompletionAlert();
        tracker.AddAndStart(dialog.Duration);
        RefreshDisplay();
    }

    private void ExitTimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        StopCompletionAlert();
        tracker.ExitTimer();
        RefreshDisplay();
    }

    private void SocialMediaPauseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        socialMediaPauseEnabled = SocialMediaPauseMenuItem.IsChecked;
        SaveBooleanSetting(SocialMediaPauseRegistryValue, socialMediaPauseEnabled);
        tracker.OnDistractingWebsiteChanged(
            socialMediaPauseEnabled && browserReportsDistractingSite);
        RefreshDisplay();

        if (socialMediaPauseEnabled &&
            !LoadBooleanSetting(BrowserSetupShownRegistryValue))
        {
            SaveBooleanSetting(BrowserSetupShownRegistryValue, enabled: true);
            ShowBrowserExtensionSetup();
        }
    }

    private void BrowserExtensionSetupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowBrowserExtensionSetup();
    }

    private void StatsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        statsWindow ??= new StatsWindow
        {
            Owner = this,
        };
        statsWindow.Opacity = Opacity;
        statsWindow.UpdateRows(dailyStatsStore.GetLastSevenDays());
        PositionStatsWindow(includeHidden: true);
        statsWindow.Show();
    }

    private void HideStatsWindow()
    {
        statsWindow?.Hide();
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
        HideStatsWindow();
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideStatsWindow();
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            HideStatsWindow();
            ShowInTaskbar = false;
            Topmost = true;
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScaleDisplay();
        PositionStatsWindow();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        PositionStatsWindow();
    }

    private void TrackerBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        isPointerOver = true;
        if (!isCompletionFlashing)
        {
            ApplyBaseAppearance();
        }
    }

    private void TrackerBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        isPointerOver = false;
        if (!isCompletionFlashing)
        {
            ApplyBaseAppearance();
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshDisplay()
    {
        dailyStatsStore.Sample(tracker.IsRunning, GetMaximumStatsDuration());
        if (tracker.Update())
        {
            dailyStatsStore.Sample(tracker.IsRunning, GetMaximumStatsDuration());
            StartCompletionAlert();
        }

        var displayTime = tracker.DisplayTime;
        TimeDisplay.Text = ElapsedTimeFormatter.Format(displayTime);
        ExitTimerMenuItem.Visibility = tracker.IsTimerMode
            ? Visibility.Visible
            : Visibility.Collapsed;

        ToggleMenuItem.Header = tracker.IsAutomaticallyPaused
            ? "Remain Paused"
            : tracker.IsRunning
                ? "Pause"
                : tracker.IsTimerCompleted
                    ? "Restart Timer"
                    : displayTime < TimeSpan.FromSeconds(1)
                        ? "Start"
                        : "Resume";

        TimeDisplay.Foreground = tracker.IsTimerCompleted
            ? completionBrush
            : new SolidColorBrush(Color.FromRgb(0x72, 0x7D, 0x86));

        if (!isCompletionFlashing)
        {
            var statusBrush = tracker.IsTimerCompleted
                ? completionBrush
                : tracker.IsAutomaticallyPaused
                    ? automaticPauseBrush
                : tracker.IsRunning
                    ? runningBrush
                    : pausedBrush;
            StatusIndicator.Fill = statusBrush;
            StatusIndicatorShadow.Color = statusBrush.Color;
        }

        StatusIndicator.ToolTip = tracker.IsAutomaticallyPaused
            ? "Paused automatically while a distracting site is active"
            : tracker.IsRunning
                ? "Running"
                : "Paused";

        if (statsWindow?.IsVisible == true)
        {
            statsWindow.UpdateRows(dailyStatsStore.GetLastSevenDays());
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
        if (!isHighlighted)
        {
            ApplyBaseAppearance();
            StatusIndicator.Fill = pausedBrush;
            StatusIndicatorShadow.Color = pausedBrush.Color;
            return;
        }

        TrackerBorder.BorderBrush = completionBrush;
        TrackerBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xF4));
        TrackerBorder.BorderThickness = new Thickness(2);
        TrackerShadow.Color = completionBrush.Color;
        TrackerShadow.BlurRadius = 15;
        TrackerShadow.Opacity = 0.42;
        StatusIndicator.Fill = completionBrush;
        StatusIndicatorShadow.Color = completionBrush.Color;
    }

    private void StopCompletionAlert()
    {
        completionFlashTimer.Stop();
        isCompletionFlashing = false;
        completionFlashStep = 0;
        ApplyBaseAppearance();
    }

    private void ApplyBaseAppearance()
    {
        TrackerBorder.BorderBrush = isPointerOver ? hoverBorderBrush : normalBorderBrush;
        TrackerBorder.Background = isPointerOver
            ? hoverBackgroundBrush
            : normalBackgroundBrush;
        TrackerBorder.BorderThickness = new Thickness(isPointerOver ? 2 : 1);
        TrackerShadow.Color = isPointerOver
            ? hoverBorderBrush.Color
            : Color.FromRgb(0x40, 0x48, 0x50);
        TrackerShadow.BlurRadius = isPointerOver ? 15 : 10;
        TrackerShadow.Opacity = isPointerOver ? 0.38 : 0.24;
    }

    private void SetOpacity(int opacityPercent, bool persist)
    {
        if (!opacityMenuItems.Any(item => item.Tag?.ToString() == opacityPercent.ToString()))
        {
            opacityPercent = DefaultOpacityPercent;
        }

        Opacity = opacityPercent / 100d;
        if (statsWindow != null)
        {
            statsWindow.Opacity = Opacity;
        }
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

    private void LoadWindowSize()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        Width = ReadDimension(key, WidthRegistryValue, DefaultWidth, MinWidth, 10000);
        Height = ReadDimension(key, HeightRegistryValue, DefaultHeight, MinHeight, 10000);
        ScaleDisplay();
    }

    private void SaveWindowSize()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
        key.SetValue(WidthRegistryValue, (int)Math.Round(bounds.Width), RegistryValueKind.DWord);
        key.SetValue(HeightRegistryValue, (int)Math.Round(bounds.Height), RegistryValueKind.DWord);
    }

    private void PositionStatsWindow(bool includeHidden = false)
    {
        if (statsWindow == null ||
            (!includeHidden && !statsWindow.IsVisible) ||
            WindowState == WindowState.Minimized)
        {
            return;
        }

        var trackerWidth = ActualWidth > 0 ? ActualWidth : Width;
        var trackerHeight = ActualHeight > 0 ? ActualHeight : Height;
        statsWindow.Width = Math.Max(236, Math.Min(trackerWidth, 420));

        var workArea = GetCurrentMonitorWorkArea();
        var left = Left + (trackerWidth - statsWindow.Width) / 2;
        left = Math.Clamp(
            left,
            workArea.Left,
            Math.Max(workArea.Left, workArea.Right - statsWindow.Width));

        var below = Top + trackerHeight + StatsWindowGap;
        var above = Top - statsWindow.Height - StatsWindowGap;
        var top = below + statsWindow.Height <= workArea.Bottom
            ? below
            : Math.Max(workArea.Top, above);

        statsWindow.Left = left;
        statsWindow.Top = top;
        statsWindow.Topmost = true;
    }

    private TimeSpan? GetMaximumStatsDuration()
    {
        return tracker.IsRunning && tracker.IsTimerMode
            ? tracker.DisplayTime
            : null;
    }

    private void ReportStatsPersistenceError(Exception exception)
    {
        MessageBox.Show(
            this,
            $"Daily statistics could not be loaded or saved.\n\n{exception.Message}",
            "My stats unavailable",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static double ReadDimension(
        RegistryKey? key,
        string valueName,
        double defaultValue,
        double minimum,
        double maximum)
    {
        return key?.GetValue(valueName) is int savedValue &&
               savedValue >= minimum &&
               savedValue <= maximum
            ? savedValue
            : defaultValue;
    }

    private void OnBrowserActivityChanged(bool active)
    {
        if (isClosing || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (isClosing)
            {
                return;
            }

            browserReportsDistractingSite = active;
            tracker.OnDistractingWebsiteChanged(socialMediaPauseEnabled && active);
            RefreshDisplay();
        });
    }

    private void ShowBrowserExtensionSetup()
    {
        var extensionDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "browser-extension");
        if (!Directory.Exists(extensionDirectory))
        {
            MessageBox.Show(
                this,
                "The browser extension files are missing. Reinstall Productivity Tracker using the latest setup.",
                "Browser extension unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        RegisterNativeMessagingHost();
        Clipboard.SetText(extensionDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = extensionDirectory,
            UseShellExecute = true,
        });

        MessageBox.Show(
            this,
            "The extension folder is open and its path has been copied.\n\n" +
            "Microsoft Edge:\n" +
            "1. Open edge://extensions\n" +
            "2. Enable Developer mode\n" +
            "3. Select Load unpacked\n" +
            "4. Select the browser-extension folder\n\n" +
            "Google Chrome uses the same steps at chrome://extensions.\n\n" +
            "If the extension was already loaded, select Reload on its extension card.\n\n" +
            "The extension requests access only to the selected website domains.",
            "Set up Focus Protection",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void RegisterNativeMessagingHost()
    {
        var executablePath = Path.Combine(
            AppContext.BaseDirectory,
            NativeHostExecutable);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The Focus Protection native host is missing.",
                executablePath);
        }

        var manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            NativeHostManifestFile);
        var escapedExecutablePath = executablePath.Replace("\\", "\\\\");
        var manifest =
            "{\n" +
            $"  \"name\": \"{NativeHostName}\",\n" +
            "  \"description\": \"Productivity Tracker Focus Protection bridge\",\n" +
            $"  \"path\": \"{escapedExecutablePath}\",\n" +
            "  \"type\": \"stdio\",\n" +
            $"  \"allowed_origins\": [\"chrome-extension://{ExtensionId}/\"]\n" +
            "}";
        File.WriteAllText(manifestPath, manifest, new System.Text.UTF8Encoding(false));

        RegisterNativeHostForBrowser(ChromeNativeHostRegistryPath, manifestPath);
        RegisterNativeHostForBrowser(EdgeNativeHostRegistryPath, manifestPath);
    }

    private static void RegisterNativeHostForBrowser(
        string registryPath,
        string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryPath);
        key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
    }

    private static bool LoadBooleanSetting(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        return key?.GetValue(valueName) is int savedValue && savedValue != 0;
    }

    private static void SaveBooleanSetting(string valueName, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
        key.SetValue(valueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    private void ScaleDisplay()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var fontSize = Math.Clamp(Math.Min(height * 0.48, width * 0.16), 20, 96);
        var indicatorSize = Math.Clamp(fontSize * 0.28, 8, 22);

        TimeDisplay.FontSize = fontSize;
        StatusIndicator.Width = indicatorSize;
        StatusIndicator.Height = indicatorSize;
        TrackerBorder.CornerRadius = new CornerRadius(Math.Clamp(height * 0.15, 7, 20));
    }

    private static ResizeRegion GetResizeHitTest(IntPtr windowHandle, IntPtr lParam)
    {
        if (!GetWindowRect(windowHandle, out var windowRect))
        {
            return ResizeRegion.Client;
        }

        var screenX = unchecked((short)((long)lParam & 0xFFFF));
        var screenY = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
        var dpiScale = GetDpiForWindow(windowHandle) / 96d;
        var borderThickness = 8 * dpiScale;

        return ResizeRegionResolver.Resolve(
            screenX - windowRect.Left,
            screenY - windowRect.Top,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top,
            borderThickness);
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return SystemParameters.WorkArea;
        }

        var dpiScale = GetDpiForWindow(handle) / 96d;
        if (dpiScale <= 0)
        {
            dpiScale = 1;
        }

        return new Rect(
            monitorInfo.WorkArea.Left / dpiScale,
            monitorInfo.WorkArea.Top / dpiScale,
            (monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left) / dpiScale,
            (monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top) / dpiScale);
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect windowRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitorHandle,
        ref NativeMonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public WindowRect MonitorArea;
        public WindowRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}