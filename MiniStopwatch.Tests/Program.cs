using MiniStopwatch.Core;

var tests = new (string Name, Action Run)[]
{
    ("Start and stop preserve elapsed time", StartAndStopPreserveElapsedTime),
    ("Lock pauses and unlock resumes", LockPausesAndUnlockResumes),
    ("Unlock does not start a stopped stopwatch", UnlockDoesNotStartStoppedStopwatch),
    ("Repeated lock notifications are harmless", RepeatedLockNotificationsAreHarmless),
    ("Reset clears elapsed time while running", ResetClearsWhileRunning),
    ("Elapsed time formats digital display", FormatsElapsedTime),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.Error.WriteLine($"FAIL: {test.Name} - {exception.Message}");
    }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"All {tests.Length} tests passed.");

static void StartAndStopPreserveElapsedTime()
{
    var clock = new FakeClock();
    var stopwatch = new StopwatchController(clock);

    stopwatch.Start();
    clock.Advance(TimeSpan.FromSeconds(12));
    stopwatch.Stop();
    clock.Advance(TimeSpan.FromSeconds(20));

    Equal(TimeSpan.FromSeconds(12), stopwatch.Elapsed);
    False(stopwatch.IsRunning);
}

static void LockPausesAndUnlockResumes()
{
    var clock = new FakeClock();
    var stopwatch = new StopwatchController(clock);

    stopwatch.Start();
    clock.Advance(TimeSpan.FromSeconds(8));
    stopwatch.OnSessionLocked();
    clock.Advance(TimeSpan.FromMinutes(5));
    Equal(TimeSpan.FromSeconds(8), stopwatch.Elapsed);

    stopwatch.OnSessionUnlocked();
    clock.Advance(TimeSpan.FromSeconds(4));

    True(stopwatch.IsRunning);
    Equal(TimeSpan.FromSeconds(12), stopwatch.Elapsed);
}

static void UnlockDoesNotStartStoppedStopwatch()
{
    var stopwatch = new StopwatchController(new FakeClock());

    stopwatch.OnSessionLocked();
    stopwatch.OnSessionUnlocked();

    False(stopwatch.IsRunning);
    Equal(TimeSpan.Zero, stopwatch.Elapsed);
}

static void RepeatedLockNotificationsAreHarmless()
{
    var clock = new FakeClock();
    var stopwatch = new StopwatchController(clock);

    stopwatch.Start();
    clock.Advance(TimeSpan.FromSeconds(3));
    stopwatch.OnSessionLocked();
    stopwatch.OnSessionLocked();
    clock.Advance(TimeSpan.FromSeconds(9));
    stopwatch.OnSessionUnlocked();

    Equal(TimeSpan.FromSeconds(3), stopwatch.Elapsed);
    True(stopwatch.IsRunning);
}

static void ResetClearsWhileRunning()
{
    var clock = new FakeClock();
    var stopwatch = new StopwatchController(clock);

    stopwatch.Start();
    clock.Advance(TimeSpan.FromSeconds(7));
    stopwatch.Reset();
    clock.Advance(TimeSpan.FromSeconds(2));

    Equal(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
    True(stopwatch.IsRunning);
}

static void FormatsElapsedTime()
{
    Equal("01:02:03", ElapsedTimeFormatter.Format(new TimeSpan(1, 2, 3)));
    Equal("27:04:05", ElapsedTimeFormatter.Format(new TimeSpan(1, 3, 4, 5)));
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void False(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

file sealed class FakeClock : IMonotonicClock
{
    public TimeSpan Now { get; private set; }

    public void Advance(TimeSpan duration)
    {
        Now += duration;
    }
}
