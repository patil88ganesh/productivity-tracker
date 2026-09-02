namespace MiniStopwatch.Core;

public enum AutomaticPauseReason
{
    SessionLocked,
    DistractingWebsite,
}

public sealed class StopwatchController
{
    private readonly IMonotonicClock clock;
    private readonly HashSet<AutomaticPauseReason> automaticPauseReasons = [];
    private TimeSpan accumulated;
    private TimeSpan startedAt;
    private bool resumeAfterAutomaticPause;

    public StopwatchController(IMonotonicClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool IsRunning { get; private set; }

    public bool IsAutomaticallyPaused =>
        resumeAfterAutomaticPause && automaticPauseReasons.Count > 0;

    public TimeSpan Elapsed =>
        IsRunning ? accumulated + (clock.Now - startedAt) : accumulated;

    public void Toggle()
    {
        if (IsRunning || resumeAfterAutomaticPause)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Start()
    {
        if (IsRunning || resumeAfterAutomaticPause)
        {
            return;
        }

        if (automaticPauseReasons.Count > 0)
        {
            resumeAfterAutomaticPause = true;
            return;
        }

        startedAt = clock.Now;
        IsRunning = true;
    }

    public void Stop()
    {
        if (IsRunning)
        {
            accumulated = Elapsed;
        }

        IsRunning = false;
        resumeAfterAutomaticPause = false;
    }

    public void Reset()
    {
        accumulated = TimeSpan.Zero;
        if (IsRunning)
        {
            startedAt = clock.Now;
        }
    }

    public void Add(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Added time cannot be negative.");
        }

        if (IsRunning)
        {
            accumulated = Elapsed + duration;
            startedAt = clock.Now;
            return;
        }

        accumulated += duration;
    }

    public void OnSessionLocked()
    {
        SetAutomaticPause(AutomaticPauseReason.SessionLocked, isActive: true);
    }

    public void OnSessionUnlocked()
    {
        SetAutomaticPause(AutomaticPauseReason.SessionLocked, isActive: false);
    }

    public void OnDistractingWebsiteChanged(bool isActive)
    {
        SetAutomaticPause(AutomaticPauseReason.DistractingWebsite, isActive);
    }

    private void SetAutomaticPause(AutomaticPauseReason reason, bool isActive)
    {
        if (isActive)
        {
            if (!automaticPauseReasons.Add(reason))
            {
                return;
            }

            if (IsRunning)
            {
                accumulated = Elapsed;
                IsRunning = false;
                resumeAfterAutomaticPause = true;
            }

            return;
        }

        if (!automaticPauseReasons.Remove(reason) ||
            automaticPauseReasons.Count > 0 ||
            !resumeAfterAutomaticPause)
        {
            return;
        }

        startedAt = clock.Now;
        IsRunning = true;
        resumeAfterAutomaticPause = false;
    }
}
