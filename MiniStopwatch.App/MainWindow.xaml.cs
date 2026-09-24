using System.Media;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmWtsSessionChange = 0x02B1;
    private const int WmDpiChanged = 0x02E0;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int NotifyForThisSession = 0;
    private const int DefaultOpacityPercent = 85;
    private const string SettingsRegistryPath = @"Software\ProductivityTracker";
    private const string LegacySettingsRegistryPath = @"Software\MiniStopwatch";
    private const string ExplorerAdvancedRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string WidgetsTaskbarRegistryValue = "TaskbarDa";
    private const string WidgetsPolicyRegistryPath =
        @"SOFTWARE\Policies\Microsoft\Dsh";
    private const string WidgetsPolicyRegistryValue = "AllowNewsAndInterests";
    private const string DisableWidgetsBoardPolicyRegistryValue =
        "DisableWidgetsBoard";
    private const string WebExperiencePackageFamily =
        "MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy";
    private const int ErrorInsufficientBuffer = 122;
    private const string OpacityRegistryValue = "OpacityPercent";
    private const string WidthRegistryValue = "WindowWidth";
    private const string HeightRegistryValue = "WindowHeight";
    private const string SocialMediaPauseRegistryValue = "SocialMediaPauseEnabled";
    private const string ContinueOnYouTubeRegistryValue = "ContinueOnYouTube";
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
    private const double DockedWidth = 123.2;
    private const double DockedHeight = 39.6;
    private const double DockedMinimumWidth = 105.6;
    private const double DockedMinimumHeight = 35.2;
    private const int DockGap = 6;
    private const double StatsWindowGap = 4;
    private static readonly Thickness NormalTrackerPadding = new(9, 5, 9, 5);
    private static readonly Thickness DockedTrackerPadding = new(6, 2, 6, 2);
    private static readonly IntPtr HwndTopmost = new(-1);
    private const long DockVisibilityIntervalMilliseconds = 500;
    private const int DockReflowIntervalMilliseconds = 250;
    private const int DockReflowAttempts = 8;
    private const long BrowserForegroundCheckIntervalMilliseconds = 500;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint AbmGetState = 0x00000004;
    private const uint AbmGetTaskbarPos = 0x00000005;
    private const uint AbsAutoHide = 0x00000001;

    private readonly TrackingController tracker = new(new SystemMonotonicClock());
    private readonly DailyStatsStore dailyStatsStore;
    private readonly DispatcherTimer displayTimer;
    private readonly DispatcherTimer completionFlashTimer;
    private readonly DispatcherTimer dockReflowTimer;
    private readonly MenuItem[] opacityMenuItems;
    private readonly SocialMediaPauseBridge socialMediaPauseBridge;
    private readonly SolidColorBrush idleBorderBrush =
        new(Color.FromArgb(0x7F, 0x9A, 0xA0, 0xA5));
    private readonly SolidColorBrush runningBorderBrush =
        new(Color.FromArgb(0xB5, 0x00, 0xC8, 0x53));
    private readonly SolidColorBrush stoppedBorderBrush =
        new(Color.FromArgb(0xB5, 0xE5, 0x39, 0x35));
    private readonly SolidColorBrush normalBackgroundBrush = new(Colors.White);
    private readonly SolidColorBrush runningHoverBackgroundBrush =
        new(Color.FromRgb(0xF1, 0xFF, 0xF6));
    private readonly SolidColorBrush stoppedHoverBackgroundBrush =
        new(Color.FromRgb(0xFF, 0xF4, 0xF4));
    private readonly SolidColorBrush idleHoverBackgroundBrush =
        new(Color.FromRgb(0xF5, 0xF6, 0xF7));
    private readonly SolidColorBrush completionBrush =
        new(Color.FromRgb(0xFF, 0x17, 0x44));
    private readonly SolidColorBrush automaticPauseBrush =
        new(Color.FromRgb(0xFF, 0x8F, 0x00));
    private readonly SolidColorBrush runningBrush =
        new(Color.FromRgb(0x00, 0xC8, 0x53));
    private readonly SolidColorBrush stoppedBrush =
        new(Color.FromRgb(0xE5, 0x39, 0x35));
    private readonly SolidColorBrush pausedBrush =
        new(Color.FromRgb(0x59, 0x63, 0x6E));
    private HwndSource? windowSource;
    private int completionFlashStep;
    private bool isCompletionFlashing;
    private bool isPointerOver;
    private bool isClosing;
    private bool isDockedToTaskbar;
    private bool isDockedInsideTaskbar;
    private bool isApplyingDockLayout;
    private bool socialMediaPauseEnabled;
    private bool continueOnYouTube;
    private BrowserActivityKind browserActivity;
    private StatsWindow? statsWindow;
    private Rect undockedBounds;
    private WindowRect undockedPhysicalBounds;
    private bool hasUndockedPhysicalBounds;
    private IntPtr dockedTaskbarHandle;
    private WindowRect dockedTaskbarRect;
    private uint dockedTaskbarDpi;
    private int remainingDockReflowAttempts;
    private long nextDockVisibilityCheck;
    private long nextBrowserForegroundCheck;

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
        continueOnYouTube = LoadBooleanSetting(ContinueOnYouTubeRegistryValue);
        ContinueOnYouTubeMenuItem.IsChecked = continueOnYouTube;
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

        dockReflowTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DockReflowIntervalMilliseconds),
        };
        dockReflowTimer.Tick += DockReflowTimer_Tick;
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

        DockToTaskbar();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        isClosing = true;
        dailyStatsStore.Sample(tracker.IsRunning, GetMaximumStatsDuration());
        dailyStatsStore.Save();
        displayTimer.Stop();
        completionFlashTimer.Stop();
        dockReflowTimer.Stop();
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

        if (message == WmNcHitTest && !isDockedToTaskbar)
        {
            var resizeResult = GetResizeHitTest(hwnd, lParam);
            if (resizeResult != ResizeRegion.Client)
            {
                handled = true;
                return (IntPtr)(int)resizeResult;
            }
        }

        if (message is WmDisplayChange or WmSettingChange or WmDpiChanged)
        {
            ScheduleDockReflow();
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

        if (e.ChangedButton == MouseButton.Left &&
            InlinePlaybackButton.IsMouseOver)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left && !isDockedToTaskbar)
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
        ApplyBrowserPauseState();
        RefreshDisplay();

        if (socialMediaPauseEnabled &&
            !LoadBooleanSetting(BrowserSetupShownRegistryValue))
        {
            SaveBooleanSetting(BrowserSetupShownRegistryValue, enabled: true);
            ShowBrowserExtensionSetup();
        }
    }

    private void ContinueOnYouTubeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        continueOnYouTube = ContinueOnYouTubeMenuItem.IsChecked;
        SaveBooleanSetting(ContinueOnYouTubeRegistryValue, continueOnYouTube);
        ApplyBrowserPauseState();
        RefreshDisplay();
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

    private void DockToTaskbarMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (isDockedToTaskbar)
        {
            UndockFromTaskbar();
        }
        else
        {
            DockToTaskbar();
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideStatsWindow();
            return;
        }

        HideStatsWindow();
        ShowInTaskbar = false;
        Topmost = true;
        if (isDockedToTaskbar)
        {
            Dispatcher.BeginInvoke(PositionDockedWindow, DispatcherPriority.Loaded);
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScaleDisplay();
        PositionStatsWindow();
        if (isDockedToTaskbar && !isApplyingDockLayout)
        {
            Dispatcher.BeginInvoke(PositionDockedWindow, DispatcherPriority.Loaded);
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        PositionStatsWindow();
    }

    private void DockToTaskbar()
    {
        HideStatsWindow();
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        undockedBounds = GetCurrentWindowBounds();
        var handle = new WindowInteropHelper(this).Handle;
        hasUndockedPhysicalBounds =
            handle != IntPtr.Zero &&
            GetWindowRect(handle, out undockedPhysicalBounds);
        isDockedToTaskbar = true;
        DockToTaskbarMenuItem.Header = "Undock from taskbar";
        ResizeMode = ResizeMode.NoResize;
        MinWidth = DockedMinimumWidth;
        MinHeight = DockedMinimumHeight;
        TrackerBorder.Padding = DockedTrackerPadding;
        PositionDockedWindow();
    }

    private void UndockFromTaskbar()
    {
        isDockedToTaskbar = false;
        isDockedInsideTaskbar = false;
        dockReflowTimer.Stop();
        remainingDockReflowAttempts = 0;
        dockedTaskbarHandle = IntPtr.Zero;
        dockedTaskbarRect = default;
        dockedTaskbarDpi = 0;
        DockToTaskbarMenuItem.Header = "Dock to taskbar";
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 140;
        MinHeight = 48;
        TrackerBorder.Padding = NormalTrackerPadding;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && hasUndockedPhysicalBounds)
        {
            var restoreBounds = KeepOnVisibleMonitor(undockedPhysicalBounds);
            if (!SetWindowPos(
                    handle,
                    HwndTopmost,
                    restoreBounds.Left,
                    restoreBounds.Top,
                    0,
                    0,
                    SwpNoSize | SwpNoActivate | SwpShowWindow))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Productivity Tracker could not restore its floating monitor.");
            }

            if (!SetWindowPos(
                    handle,
                    HwndTopmost,
                    restoreBounds.Left,
                    restoreBounds.Top,
                    restoreBounds.Right - restoreBounds.Left,
                    restoreBounds.Bottom - restoreBounds.Top,
                    SwpNoActivate | SwpShowWindow))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Productivity Tracker could not restore its floating position.");
            }
        }
        else if (!undockedBounds.IsEmpty)
        {
            Left = undockedBounds.Left;
            Top = undockedBounds.Top;
            Width = Math.Max(MinWidth, undockedBounds.Width);
            Height = Math.Max(MinHeight, undockedBounds.Height);
        }
        else
        {
            Width = DefaultWidth;
            Height = DefaultHeight;
        }

        Topmost = true;
        ScaleDisplay();
    }

    private static WindowRect KeepOnVisibleMonitor(WindowRect bounds)
    {
        var monitor = MonitorFromRect(ref bounds, MonitorDefaultToNearest);
        var monitorInfo = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return bounds;
        }

        var workArea = monitorInfo.WorkArea;
        var width = Math.Min(
            bounds.Right - bounds.Left,
            workArea.Right - workArea.Left);
        var height = Math.Min(
            bounds.Bottom - bounds.Top,
            workArea.Bottom - workArea.Top);
        var left = Math.Clamp(
            bounds.Left,
            workArea.Left,
            workArea.Right - width);
        var top = Math.Clamp(
            bounds.Top,
            workArea.Top,
            workArea.Bottom - height);
        return new WindowRect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };
    }

    private void PositionDockedWindow()
    {
        if (!isDockedToTaskbar || isApplyingDockLayout)
        {
            return;
        }

        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle == IntPtr.Zero ||
            !TryGetStableTaskbarRect(
                taskbarHandle,
                out var taskbarRect,
                out var actualTaskbarRect))
        {
            return;
        }

        var taskbarMonitorRect = taskbarRect;
        var taskbarMonitor = MonitorFromRect(
            ref taskbarMonitorRect,
            MonitorDefaultToNearest);
        var taskbarMonitorInfo = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
        };
        if (taskbarMonitor == IntPtr.Zero ||
            !GetMonitorInfo(taskbarMonitor, ref taskbarMonitorInfo))
        {
            return;
        }

        var taskbarDpi = GetEffectiveMonitorDpi(
            taskbarMonitor,
            taskbarHandle);
        var dpiScale = taskbarDpi / 96d;
        if (dpiScale <= 0)
        {
            dpiScale = 1;
        }

        var desiredWidth = (int)Math.Round(DockedWidth * dpiScale);
        var desiredHeight = (int)Math.Round(DockedHeight * dpiScale);
        var gap = (int)Math.Round(DockGap * dpiScale);
        var taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        var isHorizontal = taskbarWidth >= taskbarHeight;
        int left;
        int top;

        if (isHorizontal)
        {
            desiredHeight = Math.Min(
                desiredHeight,
                Math.Max(
                    (int)Math.Round(DockedMinimumHeight * dpiScale),
                    taskbarHeight - gap));
            left = GetLeftmostAvailableTaskbarPosition(
                taskbarHandle,
                taskbarRect,
                actualTaskbarRect,
                desiredWidth,
                gap);
            isDockedInsideTaskbar = left != int.MinValue;
            top = taskbarRect.Top + (taskbarHeight - desiredHeight) / 2;
            if (left == int.MinValue)
            {
                var monitorArea = taskbarMonitorInfo.MonitorArea;
                var distanceFromTop = Math.Abs(
                    taskbarRect.Top - monitorArea.Top);
                var distanceFromBottom = Math.Abs(
                    monitorArea.Bottom - taskbarRect.Bottom);
                var taskbarOnTop = distanceFromTop <= distanceFromBottom;
                left = monitorArea.Left + gap;
                top = taskbarOnTop
                    ? taskbarRect.Bottom + gap
                    : taskbarRect.Top - desiredHeight - gap;
            }
        }
        else
        {
            isDockedInsideTaskbar = false;
            var monitorArea = taskbarMonitorInfo.MonitorArea;
            var distanceFromLeft = Math.Abs(
                taskbarRect.Left - monitorArea.Left);
            var distanceFromRight = Math.Abs(
                monitorArea.Right - taskbarRect.Right);
            var taskbarOnLeft = distanceFromLeft <= distanceFromRight;
            left = taskbarOnLeft
                ? taskbarRect.Right + gap
                : taskbarRect.Left - desiredWidth - gap;
            top = taskbarRect.Bottom - desiredHeight - gap;
        }

        var owningMonitorArea = taskbarMonitorInfo.MonitorArea;
        left = Math.Clamp(
            left,
            owningMonitorArea.Left,
            owningMonitorArea.Right - desiredWidth);
        top = Math.Clamp(
            top,
            owningMonitorArea.Top,
            owningMonitorArea.Bottom - desiredHeight);

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        isApplyingDockLayout = true;
        try
        {
            if (!SetWindowPos(
                    handle,
                    HwndTopmost,
                    left,
                    top,
                    desiredWidth,
                    desiredHeight,
                    SwpNoActivate | SwpShowWindow))
            {
                throw new InvalidOperationException(
                    "Productivity Tracker could not be docked to the taskbar.");
            }

            dockedTaskbarHandle = taskbarHandle;
            dockedTaskbarRect = taskbarRect;
            dockedTaskbarDpi = taskbarDpi;
            nextDockVisibilityCheck =
                Environment.TickCount64 + DockVisibilityIntervalMilliseconds;
            Topmost = true;
            ScaleDisplay();
        }
        finally
        {
            isApplyingDockLayout = false;
        }
    }

    private static int GetLeftmostAvailableTaskbarPosition(
        IntPtr taskbarHandle,
        WindowRect stableTaskbarRect,
        WindowRect actualTaskbarRect,
        int desiredWidth,
        int gap)
    {
        if (IsWidgetsTaskbarSurfaceEnabled())
        {
            return int.MinValue;
        }

        if (!TryGetTaskbarChildRect(
            taskbarHandle,
            "Start",
            stableTaskbarRect,
            actualTaskbarRect,
            out var startRect) ||
            !TryGetTaskbarChildRect(
            taskbarHandle,
            "TrayNotifyWnd",
            stableTaskbarRect,
            actualTaskbarRect,
            out var trayRect))
        {
            return int.MinValue;
        }

        var left = stableTaskbarRect.Left + gap;
        var right = left + desiredWidth;
        foreach (var reservedArea in new[] { startRect, trayRect })
        {
            if (right <= reservedArea.Left - gap ||
                left >= reservedArea.Right + gap)
            {
                continue;
            }

            return int.MinValue;
        }

        var candidate = new WindowRect
        {
            Left = left,
            Top = stableTaskbarRect.Top,
            Right = right,
            Bottom = stableTaskbarRect.Bottom,
        };
        return right <= stableTaskbarRect.Right - gap &&
            IsTaskbarSlotClear(
                taskbarHandle,
                stableTaskbarRect,
                actualTaskbarRect,
                candidate)
                ? left
                : int.MinValue;
    }

    private static bool IsWidgetsTaskbarSurfaceEnabled()
    {
        using var policyKey = Registry.LocalMachine.OpenSubKey(
            WidgetsPolicyRegistryPath);
        if (policyKey?.GetValue(WidgetsPolicyRegistryValue) is int policyValue &&
            policyValue == 0)
        {
            return false;
        }

        if (policyKey?.GetValue(DisableWidgetsBoardPolicyRegistryValue) is
                int disableBoardValue &&
            disableBoardValue != 0)
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(
            ExplorerAdvancedRegistryPath);
        if (key?.GetValue(WidgetsTaskbarRegistryValue) is int configuredValue)
        {
            return configuredValue != 0;
        }

        if (Environment.OSVersion.Version.Build < 22000)
        {
            return false;
        }

        uint packageCount = 0;
        uint bufferLength = 0;
        var result = GetPackagesByPackageFamily(
            WebExperiencePackageFamily,
            ref packageCount,
            IntPtr.Zero,
            ref bufferLength,
            IntPtr.Zero);
        return result == ErrorInsufficientBuffer && packageCount > 0;
    }

    private static bool IsTaskbarSlotClear(
        IntPtr taskbarHandle,
        WindowRect stableTaskbarRect,
        WindowRect actualTaskbarRect,
        WindowRect candidate)
    {
        var horizontalOffset =
            stableTaskbarRect.Left - actualTaskbarRect.Left;
        var verticalOffset =
            stableTaskbarRect.Top - actualTaskbarRect.Top;
        var taskbarWidth = stableTaskbarRect.Right - stableTaskbarRect.Left;
        var taskbarHeight = stableTaskbarRect.Bottom - stableTaskbarRect.Top;
        var isClear = true;
        _ = EnumChildWindows(
            taskbarHandle,
            (childHandle, _) =>
            {
                if (!IsWindowVisible(childHandle) ||
                    !GetWindowRect(childHandle, out var childRect))
                {
                    return true;
                }

                childRect.Left += horizontalOffset;
                childRect.Right += horizontalOffset;
                childRect.Top += verticalOffset;
                childRect.Bottom += verticalOffset;
                var childWidth = childRect.Right - childRect.Left;
                var childHeight = childRect.Bottom - childRect.Top;
                var isShellContainer =
                    childWidth >= taskbarWidth * 0.8 &&
                    childHeight >= taskbarHeight * 0.8;
                if (!isShellContainer &&
                    RectanglesOverlap(candidate, childRect))
                {
                    isClear = false;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);
        return isClear;
    }

    private static bool RectanglesOverlap(
        WindowRect left,
        WindowRect right) =>
        left.Left < right.Right &&
        left.Right > right.Left &&
        left.Top < right.Bottom &&
        left.Bottom > right.Top;

    private static bool TryGetTaskbarChildRect(
        IntPtr taskbarHandle,
        string className,
        WindowRect stableTaskbarRect,
        WindowRect actualTaskbarRect,
        out WindowRect childRect)
    {
        var childHandle = FindWindowEx(
            taskbarHandle,
            IntPtr.Zero,
            className,
            null);
        if (childHandle == IntPtr.Zero ||
            !GetWindowRect(childHandle, out childRect))
        {
            childRect = default;
            return false;
        }

        var horizontalOffset =
            stableTaskbarRect.Left - actualTaskbarRect.Left;
        var verticalOffset =
            stableTaskbarRect.Top - actualTaskbarRect.Top;
        childRect.Left += horizontalOffset;
        childRect.Right += horizontalOffset;
        childRect.Top += verticalOffset;
        childRect.Bottom += verticalOffset;
        return true;
    }

    private static bool TryGetStableTaskbarRect(
        IntPtr taskbarHandle,
        out WindowRect stableRect,
        out WindowRect actualRect)
    {
        if (!GetWindowRect(taskbarHandle, out actualRect))
        {
            stableRect = default;
            return false;
        }

        stableRect = actualRect;
        var appBarData = new AppBarData
        {
            Size = (uint)Marshal.SizeOf<AppBarData>(),
            WindowHandle = taskbarHandle,
        };
        var autoHideEnabled =
            (SHAppBarMessage(AbmGetState, ref appBarData) & AbsAutoHide) != 0;
        if (!autoHideEnabled)
        {
            return true;
        }

        appBarData = new AppBarData
        {
            Size = (uint)Marshal.SizeOf<AppBarData>(),
            WindowHandle = taskbarHandle,
        };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref appBarData) == 0)
        {
            return true;
        }

        stableRect = appBarData.Rectangle;
        return true;
    }

    private static uint GetEffectiveMonitorDpi(
        IntPtr monitorHandle,
        IntPtr fallbackWindowHandle)
    {
        if (monitorHandle != IntPtr.Zero &&
            GetDpiForMonitor(
                monitorHandle,
                0,
                out var horizontalDpi,
                out _) == 0 &&
            horizontalDpi > 0)
        {
            return horizontalDpi;
        }

        return GetDpiForWindow(fallbackWindowHandle);
    }

    private void ScheduleDockReflow()
    {
        if (!isDockedToTaskbar || isClosing)
        {
            return;
        }

        remainingDockReflowAttempts = DockReflowAttempts;
        nextDockVisibilityCheck = 0;
        Dispatcher.BeginInvoke(PositionDockedWindow, DispatcherPriority.Loaded);
        if (!dockReflowTimer.IsEnabled)
        {
            dockReflowTimer.Start();
        }
    }

    private void DockReflowTimer_Tick(object? sender, EventArgs e)
    {
        if (!isDockedToTaskbar ||
            isClosing ||
            remainingDockReflowAttempts <= 0)
        {
            dockReflowTimer.Stop();
            remainingDockReflowAttempts = 0;
            return;
        }

        remainingDockReflowAttempts--;
        PositionDockedWindow();
    }

    private void EnsureDockedWindowVisible()
    {
        if (!isDockedToTaskbar ||
            isApplyingDockLayout ||
            WindowState != WindowState.Normal ||
            Environment.TickCount64 < nextDockVisibilityCheck)
        {
            return;
        }

        nextDockVisibilityCheck =
            Environment.TickCount64 + DockVisibilityIntervalMilliseconds;
        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle == IntPtr.Zero ||
            !TryGetStableTaskbarRect(
                taskbarHandle,
                out var taskbarRect,
                out var actualTaskbarRect))
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var taskbarMonitorRect = taskbarRect;
        var taskbarMonitor = MonitorFromRect(
            ref taskbarMonitorRect,
            MonitorDefaultToNearest);
        var taskbarDpi = GetEffectiveMonitorDpi(
            taskbarMonitor,
            taskbarHandle);
        if (taskbarHandle != dockedTaskbarHandle ||
            !AreEqual(taskbarRect, dockedTaskbarRect) ||
            taskbarDpi != dockedTaskbarDpi)
        {
            ScheduleDockReflow();
            return;
        }

        if (isDockedInsideTaskbar &&
            GetWindowRect(handle, out var dockedWindowRect))
        {
            var gap = (int)Math.Round(DockGap * taskbarDpi / 96d);
            var expectedLeft = GetLeftmostAvailableTaskbarPosition(
                taskbarHandle,
                taskbarRect,
                actualTaskbarRect,
                dockedWindowRect.Right - dockedWindowRect.Left,
                gap);
            if (expectedLeft == int.MinValue ||
                dockedWindowRect.Left != expectedLeft)
            {
                ScheduleDockReflow();
                return;
            }
        }

        if (!SetWindowPos(
                handle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove |
                SwpNoSize |
                SwpNoActivate |
                SwpShowWindow))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Productivity Tracker could not remain above the taskbar.");
        }
    }

    private static bool AreEqual(WindowRect left, WindowRect right) =>
        left.Left == right.Left &&
        left.Top == right.Top &&
        left.Right == right.Right &&
        left.Bottom == right.Bottom;

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
        EnsureDockedWindowVisible();
        RefreshBrowserPauseForForeground();
        dailyStatsStore.Sample(tracker.IsRunning, GetMaximumStatsDuration());
        if (tracker.Update())
        {
            dailyStatsStore.Sample(tracker.IsRunning, GetMaximumStatsDuration());
            StartCompletionAlert();
        }

        var displayTime = tracker.DisplayTime;
        var displayText = ElapsedTimeFormatter.Format(displayTime);
        var displayLengthChanged = TimeDisplay.Text.Length != displayText.Length;
        TimeDisplay.Text = displayText;
        if (displayLengthChanged)
        {
            ScaleDisplay();
        }
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
            : tracker.IsTimerCompleted
                ? "Timer complete"
                : tracker.IsRunning
                    ? "Running"
                    : "Paused";
        InlinePlaybackButton.IsEnabled = !tracker.IsPlaybackControlBlocked;
        InlinePlaybackButton.Foreground = GetVisualStateBrush();
        InlinePlaybackButton.ToolTip = tracker.IsPlaybackControlBlocked
            ? "Unavailable during automatic pause"
            : tracker.IsRunning
                ? "Pause"
                : tracker.IsTimerCompleted
                    ? "Restart timer"
                    : displayTime < TimeSpan.FromSeconds(1)
                        ? "Start"
                        : "Resume";
        InlinePlayIcon.Visibility = tracker.IsRunning
            ? Visibility.Collapsed
            : Visibility.Visible;
        InlinePauseIcon.Visibility = tracker.IsRunning
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!isCompletionFlashing)
        {
            ApplyBaseAppearance();
        }

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
        var stateBrush = GetVisualStateBrush();
        var stateBorderBrush = tracker.IsRunning
            ? runningBorderBrush
            : IsNotStarted()
                ? idleBorderBrush
                : stoppedBorderBrush;
        TrackerBorder.BorderBrush = isPointerOver ? stateBrush : stateBorderBrush;
        TrackerBorder.Background = isPointerOver
            ? tracker.IsRunning
                ? runningHoverBackgroundBrush
                : IsNotStarted()
                    ? idleHoverBackgroundBrush
                    : stoppedHoverBackgroundBrush
            : normalBackgroundBrush;
        TrackerBorder.BorderThickness = new Thickness(
            isPointerOver ? 2 : tracker.IsRunning ? 1.5 : 1);
        TrackerShadow.Color = stateBrush.Color;
        TrackerShadow.BlurRadius = isPointerOver ? 15 : tracker.IsRunning ? 12 : 10;
        TrackerShadow.Opacity = isPointerOver ? 0.34 : tracker.IsRunning ? 0.22 : 0.18;
    }

    private SolidColorBrush GetVisualStateBrush()
    {
        if (tracker.IsRunning)
        {
            return runningBrush;
        }

        return IsNotStarted() ? pausedBrush : stoppedBrush;
    }

    private bool IsNotStarted()
    {
        return !tracker.IsTimerCompleted &&
            tracker.DisplayTime < TimeSpan.FromSeconds(1);
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
        var bounds = isDockedToTaskbar && !undockedBounds.IsEmpty
            ? undockedBounds
            : WindowState == WindowState.Normal
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

        var trackerHandle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(trackerHandle, MonitorDefaultToNearest);
        var monitorInfo = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
        };
        if (trackerHandle == IntPtr.Zero ||
            !GetWindowRect(trackerHandle, out var trackerBounds) ||
            monitor == IntPtr.Zero ||
            !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var dpiScale = GetDpiForWindow(trackerHandle) / 96d;
        if (dpiScale <= 0)
        {
            dpiScale = 1;
        }

        var trackerWidth = (trackerBounds.Right - trackerBounds.Left) / dpiScale;
        var statsWidth = Math.Max(236, Math.Min(trackerWidth, 420));
        var statsWidthPixels = (int)Math.Round(statsWidth * dpiScale);
        var statsHeightPixels = (int)Math.Round(statsWindow.Height * dpiScale);
        var layout = CompanionWindowLayout.ResolveStatsWindow(
            new LayoutRect(
                trackerBounds.Left,
                trackerBounds.Top,
                trackerBounds.Right - trackerBounds.Left,
                trackerBounds.Bottom - trackerBounds.Top),
            new LayoutRect(
                monitorInfo.WorkArea.Left,
                monitorInfo.WorkArea.Top,
                monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left,
                monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top),
            statsWidthPixels,
            statsHeightPixels,
            StatsWindowGap * dpiScale);

        statsWindow.PositionPhysical(
            (int)Math.Round(layout.Left),
            (int)Math.Round(layout.Top),
            statsWidthPixels,
            statsHeightPixels);
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

    private void OnBrowserActivityChanged(BrowserActivityKind activity)
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

            browserActivity = activity;
            ApplyBrowserPauseState();
            RefreshDisplay();
        });
    }

    private void ApplyBrowserPauseState()
    {
        var effectiveActivity = BrowserPausePolicy.ResolveActivity(
            browserActivity,
            continueOnYouTube,
            IsForegroundYouTubeWindow());
        var shouldPause = BrowserPausePolicy.ShouldPause(
            socialMediaPauseEnabled,
            continueOnYouTube,
            effectiveActivity);
        tracker.OnDistractingWebsiteChanged(shouldPause);
    }

    private void RefreshBrowserPauseForForeground()
    {
        if (!continueOnYouTube ||
            browserActivity != BrowserActivityKind.UnknownDistracting ||
            Environment.TickCount64 < nextBrowserForegroundCheck)
        {
            return;
        }

        nextBrowserForegroundCheck =
            Environment.TickCount64 + BrowserForegroundCheckIntervalMilliseconds;
        ApplyBrowserPauseState();
    }

    private static bool IsForegroundYouTubeWindow()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0)
        {
            return false;
        }

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }

        var title = new StringBuilder(512);
        _ = GetWindowText(foregroundWindow, title, title.Capacity);
        return BrowserPausePolicy.IsForegroundYouTubeWindow(
            processName,
            title.ToString());
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
        var displayWidth = Math.Max(0, width - 18);
        var fontSize = Math.Clamp(
            Math.Min(height * 0.48, displayWidth * 0.16),
            20,
            96);
        var availableTextWidth = TimerGrid.ColumnDefinitions[1].ActualWidth;
        if (availableTextWidth <= 0)
        {
            availableTextWidth = Math.Max(1, width - 48);
        }
        if (availableTextWidth > 0)
        {
            var typeface = new Typeface(
                TimeDisplay.FontFamily,
                TimeDisplay.FontStyle,
                TimeDisplay.FontWeight,
                TimeDisplay.FontStretch);
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var formattedText = new FormattedText(
                    TimeDisplay.Text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.Black,
                    null,
                    TextFormattingMode.Display,
                    pixelsPerDip);
                if (formattedText.WidthIncludingTrailingWhitespace <= availableTextWidth)
                {
                    break;
                }

                fontSize = Math.Max(
                    12,
                    fontSize * availableTextWidth /
                    formattedText.WidthIncludingTrailingWhitespace * 0.98);
            }
        }
        var indicatorSize = Math.Clamp(fontSize * 0.28, 8, 22);

        TimeDisplay.FontSize = fontSize;
        StatusIndicator.Width = indicatorSize;
        StatusIndicator.Height = indicatorSize;
        TrackerBorder.CornerRadius = new CornerRadius(Math.Clamp(height * 0.15, 7, 20));
    }

    private void InlinePlaybackButton_Click(object sender, RoutedEventArgs e)
    {
        HideStatsWindow();
        ToggleTracking();
        e.Handled = true;
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

    private Rect GetCurrentWindowBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var windowRect))
        {
            return new Rect(
                Left,
                Top,
                ActualWidth > 0 ? ActualWidth : Width,
                ActualHeight > 0 ? ActualHeight : Height);
        }

        var dpiScale = GetDpiForWindow(handle) / 96d;
        if (dpiScale <= 0)
        {
            dpiScale = 1;
        }

        return new Rect(
            windowRect.Left / dpiScale,
            windowRect.Top / dpiScale,
            (windowRect.Right - windowRect.Left) / dpiScale,
            (windowRect.Bottom - windowRect.Top) / dpiScale);
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
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder text,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parentWindow,
        IntPtr childAfter,
        string className,
        string? windowName);

    private delegate bool EnumWindowsProcedure(
        IntPtr windowHandle,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parentWindow,
        EnumWindowsProcedure callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitorHandle,
        uint dpiType,
        out uint horizontalDpi,
        out uint verticalDpi);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(
        string packageFamilyName,
        ref uint packageCount,
        IntPtr packageFullNames,
        ref uint bufferLength,
        IntPtr buffer);

    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(
        ref WindowRect rectangle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitorHandle,
        ref NativeMonitorInfo monitorInfo);

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(
        uint message,
        ref AppBarData data);

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
    private struct AppBarData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint CallbackMessage;
        public uint Edge;
        public WindowRect Rectangle;
        public IntPtr Parameter;
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