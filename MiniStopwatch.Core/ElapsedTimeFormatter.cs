using System.Globalization;

namespace MiniStopwatch.Core;

public static class ElapsedTimeFormatter
{
    public static string Format(TimeSpan elapsed)
    {
        var totalHours = Math.Max(0, (long)elapsed.TotalHours);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }
}
