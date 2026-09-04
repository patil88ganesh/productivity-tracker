using System.Globalization;
using System.IO;
using System.Text.Json;
using MiniStopwatch.Core;

namespace MiniStopwatch.App;

internal sealed class DailyStatsStore
{
    private const int RetentionDays = 366;
    private const double MaximumDailySeconds = 26 * 60 * 60;
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    private readonly string filePath;
    private readonly IMonotonicClock clock;
    private readonly DailyStatsAccumulator accumulator;
    private readonly Action<Exception> reportError;
    private DateTimeOffset lastSaveAttempt;
    private bool? previousRunningState;
    private bool hasReportedError;

    private DailyStatsStore(
        string filePath,
        IMonotonicClock clock,
        DailyStatsAccumulator accumulator,
        Action<Exception> reportError)
    {
        this.filePath = filePath;
        this.clock = clock;
        this.accumulator = accumulator;
        this.reportError = reportError;
    }

    public static DailyStatsStore Load(Action<Exception> reportError)
    {
        ArgumentNullException.ThrowIfNull(reportError);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductivityTracker");
        var filePath = Path.Combine(directory, "daily-stats.json");
        var initialTotals = LoadTotals(filePath, reportError);
        return new DailyStatsStore(
            filePath,
            new SystemMonotonicClock(),
            new DailyStatsAccumulator(TimeZoneInfo.Local, initialTotals),
            reportError);
    }

    public void Sample(bool isRunning, TimeSpan? maximumRunningDuration = null)
    {
        var now = DateTimeOffset.Now;
        accumulator.Sample(
            now,
            clock.Now,
            isRunning,
            maximumRunningDuration);

        var stateChanged = previousRunningState.HasValue &&
                           previousRunningState.Value != isRunning;
        previousRunningState = isRunning;
        if (accumulator.HasChanges &&
            (stateChanged || now - lastSaveAttempt >= SaveInterval))
        {
            Save();
        }
    }

    public IReadOnlyList<DailyStatsEntry> GetLastSevenDays()
    {
        return accumulator.GetReport(
            DateOnly.FromDateTime(DateTime.Now),
            dayCount: 7);
    }

    public void Save()
    {
        if (!accumulator.HasChanges)
        {
            return;
        }

        lastSaveAttempt = DateTimeOffset.Now;
        try
        {
            var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-RetentionDays);
            var data = accumulator
                .CreateSnapshot()
                .Where(pair => pair.Key >= cutoff)
                .ToDictionary(
                    pair => pair.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    pair => pair.Value.TotalSeconds);
            var directory = Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException("Daily statistics path is invalid.");
            Directory.CreateDirectory(directory);
            var temporaryPath = filePath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
            File.Move(temporaryPath, filePath, overwrite: true);
            accumulator.MarkSaved();
            hasReportedError = false;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            if (!hasReportedError)
            {
                hasReportedError = true;
                reportError(exception);
            }
        }
    }

    private static IReadOnlyDictionary<DateOnly, TimeSpan> LoadTotals(
        string filePath,
        Action<Exception> reportError)
    {
        if (!File.Exists(filePath))
        {
            return new Dictionary<DateOnly, TimeSpan>();
        }

        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, double>>(
                File.ReadAllText(filePath)) ?? [];
            var totals = new Dictionary<DateOnly, TimeSpan>();
            foreach (var pair in data)
            {
                if (!double.IsFinite(pair.Value) ||
                    pair.Value <= 0 ||
                    pair.Value > MaximumDailySeconds ||
                    !DateOnly.TryParseExact(
                        pair.Key,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                {
                    throw new InvalidDataException(
                        "The daily statistics file contains an invalid entry.");
                }

                totals[date] = TimeSpan.FromSeconds(pair.Value);
            }

            return totals;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            reportError(exception);
            return new Dictionary<DateOnly, TimeSpan>();
        }
    }
}
