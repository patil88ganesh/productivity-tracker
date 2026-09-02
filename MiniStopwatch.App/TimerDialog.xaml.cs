using System.Windows;

namespace MiniStopwatch.App;

public partial class TimerDialog : Window
{
    public TimerDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            MinutesTextBox.Focus();
            MinutesTextBox.SelectAll();
        };
    }

    public TimeSpan Duration { get; private set; }

    private void StartTimer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadComponent(HoursTextBox.Text, 99, out var hours) ||
            !TryReadComponent(MinutesTextBox.Text, 59, out var minutes) ||
            !TryReadComponent(SecondsTextBox.Text, 59, out var seconds))
        {
            MessageBox.Show(
                this,
                "Enter hours from 0-99 and minutes or seconds from 0-59.",
                "Invalid timer duration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Duration = new TimeSpan(hours, minutes, seconds);
        if (Duration <= TimeSpan.Zero)
        {
            MessageBox.Show(
                this,
                "Timer duration must be greater than zero.",
                "Invalid timer duration",
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
