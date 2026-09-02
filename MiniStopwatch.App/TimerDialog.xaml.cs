using System.Windows;

namespace MiniStopwatch.App;

public enum DurationDialogMode
{
    CountdownTimer,
    AddAndStart,
}

public partial class TimerDialog : Window
{
    private readonly DurationDialogMode mode;

    public TimerDialog(DurationDialogMode mode)
    {
        InitializeComponent();
        this.mode = mode;

        if (mode == DurationDialogMode.AddAndStart)
        {
            Title = "Add and Start";
            DialogHeading.Text = "Add time and start";
            DialogDescription.Text =
                "Enter time to add to the current stopwatch, then continue counting.";
            MinutesTextBox.Text = "00";
            SecondsPanel.Visibility = Visibility.Collapsed;
            SecondsSpacerColumn.Width = new GridLength(0);
            SecondsColumn.Width = new GridLength(0);
            ConfirmButton.Content = "Add and start";
            ConfirmButton.Width = 108;
        }

        Loaded += (_, _) =>
        {
            var initialField = mode == DurationDialogMode.AddAndStart
                ? HoursTextBox
                : MinutesTextBox;
            initialField.Focus();
            initialField.SelectAll();
        };
    }

    public TimeSpan Duration { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadComponent(HoursTextBox.Text, 99, out var hours) ||
            !TryReadComponent(MinutesTextBox.Text, 59, out var minutes) ||
            !TryReadComponent(
                mode == DurationDialogMode.AddAndStart ? "0" : SecondsTextBox.Text,
                59,
                out var seconds))
        {
            MessageBox.Show(
                this,
                mode == DurationDialogMode.AddAndStart
                    ? "Enter hours from 0-99 and minutes from 0-59."
                    : "Enter hours from 0-99 and minutes or seconds from 0-59.",
                "Invalid duration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Duration = new TimeSpan(hours, minutes, seconds);
        if (Duration <= TimeSpan.Zero)
        {
            MessageBox.Show(
                this,
                "Duration must be greater than zero.",
                "Invalid duration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private static bool TryReadComponent(string text, int maximum, out int value)
    {
        return int.TryParse(text, out value) && value >= 0 && value <= maximum;
    }
}
