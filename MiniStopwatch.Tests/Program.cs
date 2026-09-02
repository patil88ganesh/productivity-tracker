using MiniStopwatch.Core;

var tests = new (string Name, Action Run)[]
{
    ("Start and stop preserve elapsed time", StartAndStopPreserveElapsedTime),
    ("Lock pauses and unlock resumes", LockPausesAndUnlockResumes),
    ("Unlock does not start a stopped stopwatch", UnlockDoesNotStartStoppedStopwatch),
    ("Repeated lock notifications are harmless", RepeatedLockNotificationsAreHarmless),
    ("Reset clears elapsed time while running", ResetClearsWhileRunning),
    ("Elapsed time formats digital display", FormatsElapsedTime),
    ("Countdown timer displays remaining time", CountdownDisplaysRemainingTime),
    ("Countdown completion occurs once", CountdownCompletionOccursOnce),
    ("Countdown pauses during session lock", CountdownPausesDuringSessionLock),
    ("Completed countdown restarts on toggle", CompletedCountdownRestartsOnToggle),
    ("Exit timer returns to stopwatch mode", ExitTimerReturnsToStopwatchMode),
    ("Resize hit test detects edges and corners", ResizeHitTestDetectsEdgesAndCorners),
    ("Resize hit test keeps center draggable", ResizeHitTestKeepsCenterDraggable),
    ("Added time continues from current stopwatch", AddedTimeContinuesFromCurrentStopwatch),
    ("Added time preserves a running stopwatch", AddedTimePreservesRunningStopwatch),
    ("Add and start exits countdown mode", AddAndStartExitsCountdownMode),
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

static void CountdownDisplaysRemainingTime()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.StartTimer(TimeSpan.FromMinutes(25));
    clock.Advance(TimeSpan.FromMinutes(4));

    True(tracker.IsTimerMode);
    True(tracker.IsRunning);
    Equal(TimeSpan.FromMinutes(21), tracker.DisplayTime);
}

static void CountdownCompletionOccursOnce()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.StartTimer(TimeSpan.FromSeconds(3));
    clock.Advance(TimeSpan.FromSeconds(3));

    True(tracker.Update());
    False(tracker.Update());
    True(tracker.IsTimerCompleted);
    False(tracker.IsRunning);
    Equal(TimeSpan.Zero, tracker.DisplayTime);
}

static void CountdownPausesDuringSessionLock()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.StartTimer(TimeSpan.FromSeconds(20));
    clock.Advance(TimeSpan.FromSeconds(5));
    tracker.OnSessionLocked();
    clock.Advance(TimeSpan.FromMinutes(2));
    Equal(TimeSpan.FromSeconds(15), tracker.DisplayTime);

    tracker.OnSessionUnlocked();
    clock.Advance(TimeSpan.FromSeconds(4));

    Equal(TimeSpan.FromSeconds(11), tracker.DisplayTime);
    True(tracker.IsRunning);
}

static void CompletedCountdownRestartsOnToggle()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.StartTimer(TimeSpan.FromSeconds(5));
    clock.Advance(TimeSpan.FromSeconds(5));
    tracker.Update();
    tracker.Toggle();

    True(tracker.IsRunning);
    False(tracker.IsTimerCompleted);
    Equal(TimeSpan.FromSeconds(5), tracker.DisplayTime);
}

static void ExitTimerReturnsToStopwatchMode()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.StartTimer(TimeSpan.FromMinutes(10));
    tracker.ExitTimer();
    tracker.Toggle();
    clock.Advance(TimeSpan.FromSeconds(7));

    False(tracker.IsTimerMode);
    Equal(TimeSpan.FromSeconds(7), tracker.DisplayTime);
}

static void ResizeHitTestDetectsEdgesAndCorners()
{
    Equal(
        ResizeRegion.TopLeft,
        ResizeRegionResolver.Resolve(2, 2, 200, 80, 8));
    Equal(
        ResizeRegion.TopRight,
        ResizeRegionResolver.Resolve(198, 2, 200, 80, 8));
    Equal(
        ResizeRegion.BottomLeft,
        ResizeRegionResolver.Resolve(2, 78, 200, 80, 8));
    Equal(
        ResizeRegion.BottomRight,
        ResizeRegionResolver.Resolve(198, 78, 200, 80, 8));
    Equal(
        ResizeRegion.Left,
        ResizeRegionResolver.Resolve(2, 40, 200, 80, 8));
    Equal(
        ResizeRegion.Right,
        ResizeRegionResolver.Resolve(198, 40, 200, 80, 8));
}

static void ResizeHitTestKeepsCenterDraggable()
{
    Equal(
        ResizeRegion.Client,
        ResizeRegionResolver.Resolve(100, 40, 200, 80, 8));
}

static void AddedTimeContinuesFromCurrentStopwatch()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    clock.Advance(TimeSpan.FromMinutes(10));
    tracker.Toggle();
    tracker.AddAndStart(TimeSpan.FromHours(1));
    clock.Advance(TimeSpan.FromMinutes(5));

    True(tracker.IsRunning);
    Equal(TimeSpan.FromMinutes(75), tracker.DisplayTime);
}

static void AddedTimePreservesRunningStopwatch()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    clock.Advance(TimeSpan.FromMinutes(5));
    tracker.AddAndStart(TimeSpan.FromMinutes(30));
    clock.Advance(TimeSpan.FromMinutes(2));

    True(tracker.IsRunning);
    Equal(TimeSpan.FromMinutes(37), tracker.DisplayTime);
}

static void AddAndStartExitsCountdownMode()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.StartTimer(TimeSpan.FromMinutes(25));
    tracker.AddAndStart(TimeSpan.FromMinutes(30));

    False(tracker.IsTimerMode);
    True(tracker.IsRunning);
    Equal(TimeSpan.FromMinutes(30), tracker.DisplayTime);
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
