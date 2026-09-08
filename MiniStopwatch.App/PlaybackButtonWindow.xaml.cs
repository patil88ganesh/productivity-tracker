using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MiniStopwatch.App;

public partial class PlaybackButtonWindow : Window
{
    private const int MouseActivateMessage = 0x0021;
    private const int DoNotActivate = 3;
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;

    private static readonly SolidColorBrush PlayBrush =
        new(Color.FromRgb(0x00, 0xB8, 0x4F));
    private static readonly SolidColorBrush PauseBrush =
        new(Color.FromRgb(0xF2, 0x8C, 0x00));
    private static readonly SolidColorBrush DisabledBrush =
        new(Color.FromRgb(0x7A, 0x84, 0x8D));
    private static readonly SolidColorBrush NormalBackground =
        new(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush HoverBackground =
        new(Colors.White);

    private readonly Action toggleTracking;
    private HwndSource? windowSource;
    private bool controlEnabled = true;
    private bool isHovered;
    private bool isPressed;
    private SolidColorBrush accentBrush = PlayBrush;

    public PlaybackButtonWindow(Action toggleTracking)
    {
        this.toggleTracking =
            toggleTracking ?? throw new ArgumentNullException(nameof(toggleTracking));
        InitializeComponent();
        ApplyAppearance();
    }

    public void UpdateState(
        bool isRunning,
        bool isBlockedByAutomaticPause,
        bool isTimerCompleted,
        bool isAtZero)
    {
        controlEnabled = !isBlockedByAutomaticPause;
        accentBrush = isBlockedByAutomaticPause
            ? DisabledBrush
            : isRunning
                ? PauseBrush
                : PlayBrush;
        PlayIcon.Visibility = isRunning
            ? Visibility.Collapsed
            : Visibility.Visible;
        PauseIcon.Visibility = isRunning
            ? Visibility.Visible
            : Visibility.Collapsed;
        ButtonSurface.ToolTip = isBlockedByAutomaticPause
            ? "Unavailable during automatic pause"
            : isRunning
                ? "Pause"
                : isTimerCompleted
                    ? "Restart timer"
                    : isAtZero
                        ? "Start"
                        : "Resume";
        ApplyAppearance();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException(
                "Unable to access the Play/Pause button window.");
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
                "Unable to read the Play/Pause button window style. Win32 error: {0}.",
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
                "Unable to make the Play/Pause button non-activating. Win32 error: {0}.",
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

    private void ButtonSurface_MouseEnter(object sender, MouseEventArgs e)
    {
        isHovered = true;
        ApplyAppearance();
    }

    private void ButtonSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        isHovered = false;
        isPressed = false;
        ButtonSurface.ReleaseMouseCapture();
        ApplyAppearance();
    }

    private void ButtonSurface_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!controlEnabled)
        {
            return;
        }

        isPressed = true;
        Mouse.Capture(ButtonSurface);
        ApplyAppearance();
        e.Handled = true;
    }

    private void ButtonSurface_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!isPressed)
        {
            return;
        }

        isPressed = false;
        ButtonSurface.ReleaseMouseCapture();
        ApplyAppearance();
        e.Handled = true;
        if (controlEnabled && ButtonSurface.IsMouseOver)
        {
            toggleTracking();
        }
    }

    private void ApplyAppearance()
    {
        ButtonSurface.BorderBrush = accentBrush;
        ButtonSurface.Background = isHovered
            ? HoverBackground
            : NormalBackground;
        ButtonSurface.Opacity = controlEnabled
            ? isPressed ? 0.72 : 1
            : 0.58;
        PlayIcon.Fill = accentBrush;
        foreach (var rectangle in PauseIcon.Children.OfType<System.Windows.Shapes.Rectangle>())
        {
            rectangle.Fill = accentBrush;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr newValue);
}
