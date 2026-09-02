namespace MiniStopwatch.Core;

public sealed class StopwatchController
{
    private readonly IMonotonicClock clock;
    private TimeSpan accumulated;
    private TimeSpan startedAt;
    private bool resumeAfterUnlock;

    public StopwatchController(IMonotonicClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool IsRunning { get; private set; }

    public TimeSpan Elapsed =>
        IsRunning ? accumulated + (clock.Now - startedAt) : accumulated;

    public void Toggle()
    {
        if (IsRunning)
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
        if (IsRunning)
        {
            return;
        }

        startedAt = clock.Now;
        IsRunning = true;
        resumeAfterUnlock = false;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        accumulated = Elapsed;
        IsRunning = false;
        resumeAfterUnlock = false;
    }

    public void Reset()
    {
        accumulated = TimeSpan.Zero;
        if (IsRunning)
        {
            startedAt = clock.Now;
        }
    }

    public void OnSessionLocked()
    {
        if (!IsRunning)
        {
            return;
        }

        accumulated = Elapsed;
        IsRunning = false;
        resumeAfterUnlock = true;
    }

    public void OnSessionUnlocked()
    {
        if (!resumeAfterUnlock)
        {
            return;
        }

        startedAt = clock.Now;
        IsRunning = true;
        resumeAfterUnlock = false;
    }
}
