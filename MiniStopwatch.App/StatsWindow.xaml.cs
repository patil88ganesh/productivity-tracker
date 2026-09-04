using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MiniStopwatch.Core;

namespace MiniStopwatch.App;

public partial class StatsWindow : Window
{
    private const int MouseActivateMessage = 0x0021;
    private const int DoNotActivate = 3;
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private HwndSource? windowSource;
    private string[] displayedRows = [];

    public StatsWindow()
    {
        InitializeComponent();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("Unable to access the statistics window.");
        windowSource.AddHook(WindowMessageHook);
        TryApplyNonActivatingStyle(handle);
    }

    private static void TryApplyNonActivatingStyle(IntPtr handle)
    {
        Marshal.SetLastPInvokeError(0);
        var extendedStyle = GetWindowLongPtr(handle, ExtendedStyleIndex);
        var error = Marshal.GetLastPInvokeError();
        if (extendedStyle == IntPtr.Zero && error != 0)
        {
            Trace.TraceWarning(
                "Unable to read the statistics window style. Win32 error: {0}.",
                error);
            return;
        }

        Marshal.SetLastPInvokeError(0);
        var previousStyle = SetWindowLongPtr(
            handle,
            ExtendedStyleIndex,
            new IntPtr(extendedStyle.ToInt64() | NoActivateStyle));
        error = Marshal.GetLastPInvokeError();
        if (previousStyle == IntPtr.Zero && error != 0)
        {
            Trace.TraceWarning(
                "Unable to make the statistics window non-activating. Win32 error: {0}.",
                error);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        windowSource?.RemoveHook(WindowMessageHook);
    }

    private static IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != MouseActivateMessage)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return (IntPtr)DoNotActivate;
    }

    public void UpdateRows(IReadOnlyList<DailyStatsEntry> entries)
    {
        var rows = entries.Select(FormatRow).ToArray();
        if (rows.SequenceEqual(displayedRows))
        {
            return;
        }

        displayedRows = rows;
        Rows.ItemsSource = rows;
    }

    private static string FormatRow(DailyStatsEntry entry)
    {
        var date = entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var day = entry.Date.ToDateTime(TimeOnly.MinValue).ToString(
            "ddd",
            CultureInfo.CurrentCulture);
        var hours = entry.TrackedTime.HasValue
            ? FormatTrackedTime(entry.TrackedTime.Value)
            : "NA";
        return $"{date} | {day,-3} | {hours}";
    }

    private static string FormatTrackedTime(TimeSpan trackedTime)
    {
        var totalHours = (long)trackedTime.TotalHours;
        return $"{totalHours:00}:{trackedTime.Minutes:00}";
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr newValue);
}
