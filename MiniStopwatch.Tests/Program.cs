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
    ("Distracting site pauses and resumes tracking", DistractingSitePausesAndResumesTracking),
    ("Continue counting overrides current distracting-site visit", ContinueCountingOverridesCurrentDistractingSiteVisit),
    ("Continue counting survives watch focus handoff", ContinueCountingSurvivesWatchFocusHandoff),
    ("Continue counting survives delayed focus loss", ContinueCountingSurvivesDelayedFocusLoss),
    ("Continue counting remains available after a long automatic pause", ContinueCountingRemainsAvailableAfterLongPause),
    ("Continue counting handoff expires", ContinueCountingHandoffExpires),
    ("Confirmed Continue counting handoff expires", ConfirmedContinueCountingHandoffExpires),
    ("Continue counting handoff includes exact deadline", ContinueCountingHandoffIncludesExactDeadline),
    ("Continue counting status clears while browser is unfocused", ContinueCountingStatusClearsWhileBrowserIsUnfocused),
    ("Continue counting does not transfer to another protected visit", ContinueCountingDoesNotTransferToAnotherProtectedVisit),
    ("Continue counting can be cancelled while active", ContinueCountingCanBeCancelledWhileActive),
    ("Unprotected page clears Continue counting offer", UnprotectedPageClearsContinueCountingOffer),
    ("Continue counting preserves session-lock pause", ContinueCountingPreservesSessionLockPause),
    ("Continue counting requires an interrupted tracker", ContinueCountingRequiresInterruptedTracker),
    ("Remain paused clears Continue counting offer", RemainPausedClearsContinueCountingOffer),
    ("Disabling Focus Protection cancels Continue counting", DisablingFocusProtectionCancelsContinueCounting),
    ("Session lock does not create Continue counting offer", SessionLockDoesNotCreateContinueCountingOffer),
    ("Session lock blocks a pending Continue counting offer", SessionLockBlocksPendingContinueCountingOffer),
    ("Continue counting survives manual pause and resume", ContinueCountingSurvivesManualPauseAndResume),
    ("Continue counting survives a new timer in the same visit", ContinueCountingSurvivesNewTimerInSameVisit),
    ("Continue counting contributes productive statistics", ContinueCountingContributesProductiveStatistics),
    ("Lock and distracting site require both to clear", AutomaticPauseReasonsMustBothClear),
    ("Manual stop during automatic pause prevents resume", ManualStopDuringAutomaticPausePreventsResume),
    ("Daily stats count only active tracking", DailyStatsCountOnlyActiveTracking),
    ("Daily stats split tracking at local midnight", DailyStatsSplitAtLocalMidnight),
    ("Daily stats report missing days as NA", DailyStatsReportMissingDaysAsNa),
    ("Daily stats cap delayed countdown samples", DailyStatsCapDelayedCountdownSamples),
    ("Daily stats cap countdown at midnight deadline", DailyStatsCapCountdownAtMidnightDeadline),
    ("Daily stats use earliest ambiguous midnight", DailyStatsUseEarliestAmbiguousMidnight),
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

static void DistractingSitePausesAndResumesTracking()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    clock.Advance(TimeSpan.FromMinutes(8));
    tracker.OnDistractingWebsiteChanged(isActive: true);
    clock.Advance(TimeSpan.FromMinutes(12));

    True(tracker.IsAutomaticallyPaused);
    Equal(TimeSpan.FromMinutes(8), tracker.DisplayTime);

    tracker.OnDistractingWebsiteChanged(isActive: false);
    clock.Advance(TimeSpan.FromMinutes(2));

    True(tracker.IsRunning);
    Equal(TimeSpan.FromMinutes(10), tracker.DisplayTime);
}

static void ContinueCountingOverridesCurrentDistractingSiteVisit()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    clock.Advance(TimeSpan.FromMinutes(5));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    True(tracker.CanContinueCountingOnDistractingWebsite);
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.OnDistractingWebsiteChanged(isActive: true);
    clock.Advance(TimeSpan.FromMinutes(10));
    clock.Advance(TimeSpan.FromMinutes(5));

    True(tracker.IsRunning);
    False(tracker.IsAutomaticallyPaused);
    Equal(TimeSpan.FromMinutes(20), tracker.DisplayTime);

    tracker.OnDistractingWebsiteChanged(isActive: false);
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.IsRunning);
    True(tracker.CanContinueCountingOnDistractingWebsite);
}

static void ContinueCountingSurvivesWatchFocusHandoff()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);

    True(tracker.IsRunning);
    True(tracker.CanContinueCountingOnDistractingWebsite);

    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.OnDistractingWebsiteChanged(isActive: true);
    clock.Advance(TimeSpan.FromMinutes(10));

    True(tracker.IsRunning);
    False(tracker.IsAutomaticallyPaused);
    Equal(TimeSpan.FromMinutes(10), tracker.DisplayTime);

    tracker.OnDistractingWebsiteChanged(isActive: false);
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.IsRunning);
}

static void ContinueCountingSurvivesDelayedFocusLoss()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.OnDistractingWebsiteChanged(isActive: true);
    clock.Advance(TimeSpan.FromMinutes(10));

    True(tracker.IsRunning);
    False(tracker.IsAutomaticallyPaused);
    Equal(TimeSpan.FromMinutes(10), tracker.DisplayTime);

    tracker.OnDistractingWebsiteChanged(isActive: false);
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.IsRunning);
}

static void ContinueCountingRemainsAvailableAfterLongPause()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    clock.Advance(TimeSpan.FromMinutes(10));
    tracker.OnDistractingWebsiteChanged(isActive: false);

    True(tracker.CanContinueCountingOnDistractingWebsite);
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnDistractingWebsiteChanged(isActive: true);

    True(tracker.IsRunning);
    False(tracker.IsAutomaticallyPaused);
}

static void ContinueCountingHandoffExpires()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.ContinueCountingOnDistractingWebsite();
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
}

static void ConfirmedContinueCountingHandoffExpires()
{
    var clock = new FakeClock();
    var tracker = CreateConfirmedContinueCountingTrackerWithClock(clock);

    tracker.OnDistractingWebsiteChanged(isActive: false);
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
    False(tracker.IsContinuingCountingOnDistractingWebsite);
}

static void ContinueCountingHandoffIncludesExactDeadline()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.ContinueCountingOnDistractingWebsite();
    clock.Advance(TimeSpan.FromSeconds(30));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    True(tracker.IsRunning);
    True(tracker.IsContinuingCountingOnDistractingWebsite);
}

static void ContinueCountingStatusClearsWhileBrowserIsUnfocused()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true, visitToken: "work-visit");
    tracker.ContinueCountingOnDistractingWebsite();

    True(tracker.IsContinuingCountingOnDistractingWebsite);
    True(tracker.HasContinueCountingOverride);

    tracker.OnDistractingWebsiteChanged(isActive: false, visitToken: "work-visit");

    False(tracker.IsContinuingCountingOnDistractingWebsite);
    True(tracker.HasContinueCountingOverride);
}

static void ContinueCountingDoesNotTransferToAnotherProtectedVisit()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true, visitToken: "work-visit");
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnDistractingWebsiteChanged(isActive: true, visitToken: "other-visit");

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
    False(tracker.HasContinueCountingOverride);
    True(tracker.CanContinueCountingOnDistractingWebsite);
}

static void ContinueCountingCanBeCancelledWhileActive()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true, visitToken: "work-visit");
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.CancelContinueCountingOnDistractingWebsite();

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
    False(tracker.HasContinueCountingOverride);
}

static void UnprotectedPageClearsContinueCountingOffer()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true, visitToken: "work-visit");
    tracker.OnDistractingWebsiteChanged(isActive: false, visitToken: "work-visit");

    True(tracker.CanContinueCountingOnDistractingWebsite);

    tracker.OnDistractingWebsiteChanged(isActive: false, visitToken: null);

    False(tracker.CanContinueCountingOnDistractingWebsite);
    tracker.ContinueCountingOnDistractingWebsite();
    False(tracker.HasContinueCountingOverride);
}

static void ContinueCountingPreservesSessionLockPause()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnSessionLocked();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.ContinueCountingOnDistractingWebsite();

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
    False(tracker.CanContinueCountingOnDistractingWebsite);

    tracker.OnSessionUnlocked();

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);

    tracker.OnDistractingWebsiteChanged(isActive: false);

    True(tracker.IsRunning);
}

static void ContinueCountingRequiresInterruptedTracker()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.CanContinueCountingOnDistractingWebsite);
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.Toggle();

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
    True(tracker.CanContinueCountingOnDistractingWebsite);
}

static void RemainPausedClearsContinueCountingOffer()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: false);

    False(tracker.IsRunning);
    False(tracker.IsAutomaticallyPaused);
    False(tracker.CanContinueCountingOnDistractingWebsite);
}

static void DisablingFocusProtectionCancelsContinueCounting()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.CancelContinueCountingOnDistractingWebsite();

    False(tracker.CanContinueCountingOnDistractingWebsite);

    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
}

static void SessionLockDoesNotCreateContinueCountingOffer()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnSessionLocked();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.OnSessionUnlocked();

    True(tracker.IsRunning);
    False(tracker.CanContinueCountingOnDistractingWebsite);
}

static void SessionLockBlocksPendingContinueCountingOffer()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.OnSessionLocked();

    False(tracker.CanContinueCountingOnDistractingWebsite);
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnSessionUnlocked();
    tracker.OnDistractingWebsiteChanged(isActive: true);

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);
}

static void ContinueCountingSurvivesManualPauseAndResume()
{
    var tracker = CreateConfirmedContinueCountingTracker();

    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.Toggle();
    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);

    True(tracker.IsRunning);
    True(tracker.IsContinuingCountingOnDistractingWebsite);
}

static void ContinueCountingSurvivesNewTimerInSameVisit()
{
    var tracker = CreateConfirmedContinueCountingTracker();

    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.StartTimer(TimeSpan.FromMinutes(25));
    tracker.OnDistractingWebsiteChanged(isActive: true);

    True(tracker.IsRunning);
    True(tracker.IsTimerMode);
    True(tracker.IsContinuingCountingOnDistractingWebsite);
}

static TrackingController CreateConfirmedContinueCountingTracker()
{
    return CreateConfirmedContinueCountingTrackerWithClock(new FakeClock());
}

static TrackingController CreateConfirmedContinueCountingTrackerWithClock(
    FakeClock clock)
{
    var tracker = new TrackingController(clock);
    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnDistractingWebsiteChanged(isActive: false);
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    return tracker;
}

static void ContinueCountingContributesProductiveStatistics()
{
    var clock = new FakeClock();
    var tracker = new TrackingController(clock);
    var stats = new DailyStatsAccumulator(TimeZoneInfo.Utc);
    var timestamp = new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);
    var monotonic = TimeSpan.Zero;

    stats.Sample(timestamp, monotonic, isRunning: false);
    tracker.Toggle();
    stats.Sample(timestamp, monotonic, tracker.IsRunning);

    Advance(TimeSpan.FromMinutes(5));
    tracker.OnDistractingWebsiteChanged(isActive: true);
    stats.Sample(timestamp, monotonic, tracker.IsRunning);

    Advance(TimeSpan.FromMinutes(7));
    tracker.OnDistractingWebsiteChanged(isActive: false);
    stats.Sample(timestamp, monotonic, tracker.IsRunning);
    tracker.ContinueCountingOnDistractingWebsite();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    stats.Sample(timestamp, monotonic, tracker.IsRunning);

    Advance(TimeSpan.FromMinutes(4));
    tracker.Toggle();
    stats.Sample(timestamp, monotonic, tracker.IsRunning);

    var report = stats.GetReport(new DateOnly(2026, 9, 5), 1);
    Equal(TimeSpan.FromMinutes(9), report[0].TrackedTime);

    void Advance(TimeSpan duration)
    {
        clock.Advance(duration);
        timestamp += duration;
        monotonic += duration;
    }
}

static void AutomaticPauseReasonsMustBothClear()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnSessionLocked();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.OnSessionUnlocked();

    False(tracker.IsRunning);
    True(tracker.IsAutomaticallyPaused);

    tracker.OnDistractingWebsiteChanged(isActive: false);

    True(tracker.IsRunning);
}

static void ManualStopDuringAutomaticPausePreventsResume()
{
    var tracker = new TrackingController(new FakeClock());

    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: true);
    tracker.Toggle();
    tracker.OnDistractingWebsiteChanged(isActive: false);

    False(tracker.IsRunning);
    False(tracker.IsAutomaticallyPaused);
}

static void DailyStatsCountOnlyActiveTracking()
{
    var stats = new DailyStatsAccumulator(TimeZoneInfo.Utc);
    var timestamp = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);
    var monotonic = TimeSpan.Zero;

    stats.Sample(timestamp, monotonic, isRunning: false);
    stats.Sample(timestamp, monotonic, isRunning: true);

    timestamp += TimeSpan.FromMinutes(45);
    monotonic += TimeSpan.FromMinutes(45);
    stats.Sample(timestamp, monotonic, isRunning: false);

    timestamp += TimeSpan.FromHours(2);
    monotonic += TimeSpan.FromHours(2);
    stats.Sample(timestamp, monotonic, isRunning: true);

    timestamp += TimeSpan.FromMinutes(30);
    monotonic += TimeSpan.FromMinutes(30);
    stats.Sample(timestamp, monotonic, isRunning: false);

    var report = stats.GetReport(new DateOnly(2026, 9, 4), 1);
    Equal(TimeSpan.FromMinutes(75), report[0].TrackedTime);
}

static void DailyStatsSplitAtLocalMidnight()
{
    var stats = new DailyStatsAccumulator(TimeZoneInfo.Utc);
    var timestamp = new DateTimeOffset(2026, 9, 3, 23, 30, 0, TimeSpan.Zero);

    stats.Sample(timestamp, TimeSpan.Zero, isRunning: true);
    stats.Sample(
        timestamp.AddHours(1),
        TimeSpan.FromHours(1),
        isRunning: false);

    var report = stats.GetReport(new DateOnly(2026, 9, 4), 2);
    Equal(new DateOnly(2026, 9, 4), report[0].Date);
    Equal(TimeSpan.FromMinutes(30), report[0].TrackedTime);
    Equal(new DateOnly(2026, 9, 3), report[1].Date);
    Equal(TimeSpan.FromMinutes(30), report[1].TrackedTime);
}

static void DailyStatsReportMissingDaysAsNa()
{
    var stats = new DailyStatsAccumulator(
        TimeZoneInfo.Utc,
        new Dictionary<DateOnly, TimeSpan>
        {
            [new DateOnly(2026, 9, 2)] = TimeSpan.FromMinutes(90),
        });

    var report = stats.GetReport(new DateOnly(2026, 9, 4), 3);

    Equal(new DateOnly(2026, 9, 4), report[0].Date);
    Equal<TimeSpan?>(null, report[0].TrackedTime);
    Equal(new DateOnly(2026, 9, 3), report[1].Date);
    Equal<TimeSpan?>(null, report[1].TrackedTime);
    Equal(new DateOnly(2026, 9, 2), report[2].Date);
    Equal(TimeSpan.FromMinutes(90), report[2].TrackedTime);
}

static void DailyStatsCapDelayedCountdownSamples()
{
    var stats = new DailyStatsAccumulator(TimeZoneInfo.Utc);
    var timestamp = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    stats.Sample(
        timestamp,
        TimeSpan.Zero,
        isRunning: true,
        maximumRunningDuration: TimeSpan.FromMinutes(5));
    stats.Sample(
        timestamp.AddMinutes(10),
        TimeSpan.FromMinutes(10),
        isRunning: false);

    var report = stats.GetReport(new DateOnly(2026, 9, 4), 1);
    Equal(TimeSpan.FromMinutes(5), report[0].TrackedTime);
}

static void DailyStatsCapCountdownAtMidnightDeadline()
{
    var stats = new DailyStatsAccumulator(TimeZoneInfo.Utc);
    var timestamp = new DateTimeOffset(2026, 9, 4, 23, 58, 0, TimeSpan.Zero);

    stats.Sample(
        timestamp,
        TimeSpan.Zero,
        isRunning: true,
        maximumRunningDuration: TimeSpan.FromMinutes(5));
    stats.Sample(
        timestamp.AddMinutes(10),
        TimeSpan.FromMinutes(10),
        isRunning: false);

    var report = stats.GetReport(new DateOnly(2026, 9, 5), 2);
    Equal(TimeSpan.FromMinutes(3), report[0].TrackedTime);
    Equal(TimeSpan.FromMinutes(2), report[1].TrackedTime);
}

static void DailyStatsUseEarliestAmbiguousMidnight()
{
    var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Cuba Standard Time");
    var stats = new DailyStatsAccumulator(timeZone);
    var start = new DateTimeOffset(
        2026,
        10,
        31,
        23,
        30,
        0,
        TimeSpan.FromHours(-4));
    var end = new DateTimeOffset(
        2026,
        11,
        1,
        1,
        30,
        0,
        TimeSpan.FromHours(-5));

    stats.Sample(start, TimeSpan.Zero, isRunning: true);
    stats.Sample(end, TimeSpan.FromHours(3), isRunning: false);

    var report = stats.GetReport(new DateOnly(2026, 11, 1), 2);
    Equal(TimeSpan.FromMinutes(150), report[0].TrackedTime);
    Equal(TimeSpan.FromMinutes(30), report[1].TrackedTime);
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

file static class TrackingControllerTestExtensions
{
    public static void OnDistractingWebsiteChanged(
        this TrackingController tracker,
        bool isActive)
    {
        tracker.OnDistractingWebsiteChanged(
            isActive,
            "test-protected-visit");
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
