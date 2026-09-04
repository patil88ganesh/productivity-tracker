using System.Globalization;
using System.Windows;
using MiniStopwatch.Core;

namespace MiniStopwatch.App;

public partial class StatsWindow : Window
{
    private string[] displayedRows = [];

    public StatsWindow()
    {
        InitializeComponent();
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
}
