namespace MiniStopwatch.Core;

public sealed record DailyStatsEntry(DateOnly Date, TimeSpan? TrackedTime);

public sealed class DailyStatsAccumulator
{
    private readonly TimeZoneInfo timeZone;
    private readonly Dictionary<DateOnly, TimeSpan> totals;
    private DateTimeOffset lastTimestamp;
    private TimeSpan lastMonotonicTime;
    private TimeSpan? previousMaximumRunningDuration;
    private bool previousRunningState;
    private bool isInitialized;

    public DailyStatsAccumulator(
        TimeZoneInfo timeZone,
        IReadOnlyDictionary<DateOnly, TimeSpan>? initialTotals = null)
    {
        this.timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        totals = initialTotals?
            .Where(pair => pair.Value > TimeSpan.Zero)
            .ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? [];
    }

    public bool HasChanges { get; private set; }

    public void Sample(
        DateTimeOffset timestamp,
        TimeSpan monotonicTime,
        bool isRunning,
        TimeSpan? maximumRunningDuration = null)
    {
        if (maximumRunningDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRunningDuration),
                "Maximum running duration cannot be negative.");
        }

        if (!isInitialized)
        {
            lastTimestamp = timestamp;
            lastMonotonicTime = monotonicTime;
            previousRunningState = isRunning;
            previousMaximumRunningDuration = isRunning
                ? maximumRunningDuration
                : null;
            isInitialized = true;
            return;
        }

        if (monotonicTime < lastMonotonicTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monotonicTime),
                "Monotonic time cannot move backwards.");
        }

        var fullTrackedTime = monotonicTime - lastMonotonicTime;
        var trackedTime = fullTrackedTime;
        var intervalEnd = timestamp;
        if (previousRunningState && trackedTime > TimeSpan.Zero)
        {
            if (previousMaximumRunningDuration.HasValue &&
                trackedTime > previousMaximumRunningDuration.Value)
            {
                trackedTime = previousMaximumRunningDuration.Value;
                var wallTime = timestamp.ToUniversalTime() -
                               lastTimestamp.ToUniversalTime();
                if (wallTime > TimeSpan.Zero && fullTrackedTime > TimeSpan.Zero)
                {
                    intervalEnd = lastTimestamp.AddTicks((long)Math.Round(
                        wallTime.Ticks *
                        (trackedTime.Ticks / (double)fullTrackedTime.Ticks)));
                }
            }
            AddInterval(lastTimestamp, intervalEnd, trackedTime);
            HasChanges |= trackedTime > TimeSpan.Zero;
        }

        lastTimestamp = timestamp;
        lastMonotonicTime = monotonicTime;
        previousRunningState = isRunning;
        previousMaximumRunningDuration = isRunning
            ? maximumRunningDuration
            : null;
    }

    public IReadOnlyList<DailyStatsEntry> GetReport(DateOnly endDate, int dayCount)
    {
        if (dayCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayCount),
                "Report day count must be greater than zero.");
        }

        var entries = new List<DailyStatsEntry>(dayCount);
        for (var offset = 0; offset < dayCount; offset++)
        {
            var date = endDate.AddDays(-offset);
            entries.Add(new DailyStatsEntry(
                date,
                totals.TryGetValue(date, out var trackedTime)
                    ? trackedTime
                    : null));
        }

        return entries;
    }

    public IReadOnlyDictionary<DateOnly, TimeSpan> CreateSnapshot()
    {
        return new Dictionary<DateOnly, TimeSpan>(totals);
    }

    public void MarkSaved()
    {
        HasChanges = false;
    }

    private void AddInterval(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan trackedTime)
    {
        var startUtc = start.ToUniversalTime();
        var endUtc = end.ToUniversalTime();
        var wallDuration = endUtc - startUtc;
        if (wallDuration <= TimeSpan.Zero)
        {
            AddToDate(GetLocalDate(start), trackedTime);
            return;
        }

        var cursor = startUtc;
        var remainingTrackedTime = trackedTime;
        while (cursor < endUtc)
        {
            var localDate = GetLocalDate(cursor);
            var nextLocalMidnight = localDate
                .AddDays(1)
                .ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var nextMidnightUtc = ResolveLocalBoundary(nextLocalMidnight);
            var segmentEnd = nextMidnightUtc > cursor && nextMidnightUtc < endUtc
                ? nextMidnightUtc
                : endUtc;

            TimeSpan segmentTrackedTime;
            if (segmentEnd == endUtc)
            {
                segmentTrackedTime = remainingTrackedTime;
            }
            else
            {
                var segmentWallTime = segmentEnd - cursor;
                var segmentTicks = (long)Math.Round(
                    trackedTime.Ticks *
                    (segmentWallTime.Ticks / (double)wallDuration.Ticks));
                segmentTrackedTime = TimeSpan.FromTicks(
                    Math.Clamp(segmentTicks, 0, remainingTrackedTime.Ticks));
            }

            AddToDate(localDate, segmentTrackedTime);
            remainingTrackedTime -= segmentTrackedTime;
            cursor = segmentEnd;
        }
    }

    private DateTimeOffset ResolveLocalBoundary(DateTime localBoundary)
    {
        while (timeZone.IsInvalidTime(localBoundary))
        {
            localBoundary = localBoundary.AddMinutes(1);
        }

        var offset = timeZone.IsAmbiguousTime(localBoundary)
            ? timeZone.GetAmbiguousTimeOffsets(localBoundary).Max()
            : timeZone.GetUtcOffset(localBoundary);
        return new DateTimeOffset(localBoundary, offset).ToUniversalTime();
    }

    private DateOnly GetLocalDate(DateTimeOffset timestamp)
    {
        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime);
    }

    private void AddToDate(DateOnly date, TimeSpan trackedTime)
    {
        if (trackedTime <= TimeSpan.Zero)
        {
            return;
        }

        totals[date] = totals.GetValueOrDefault(date) + trackedTime;
    }
}
