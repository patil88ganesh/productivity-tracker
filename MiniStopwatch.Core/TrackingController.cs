namespace MiniStopwatch.Core;

public sealed class TrackingController
{
    private readonly StopwatchController stopwatch;

    public TrackingController(IMonotonicClock clock)
    {
        stopwatch = new StopwatchController(clock);
    }

    public bool IsRunning => stopwatch.IsRunning;

    public bool IsTimerMode => TimerDuration.HasValue;

    public bool IsTimerCompleted { get; private set; }

    public TimeSpan? TimerDuration { get; private set; }

    public TimeSpan DisplayTime
    {
        get
        {
            if (!TimerDuration.HasValue)
            {
                return stopwatch.Elapsed;
            }

            var remaining = TimerDuration.Value - stopwatch.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public void Toggle()
    {
        if (IsTimerCompleted)
        {
            stopwatch.Reset();
            stopwatch.Start();
            IsTimerCompleted = false;
            return;
        }

        stopwatch.Toggle();
    }

    public void Reset()
    {
        stopwatch.Reset();
        IsTimerCompleted = false;
    }

    public void StartTimer(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Timer duration must be greater than zero.");
        }

        stopwatch.Stop();
        stopwatch.Reset();
        TimerDuration = duration;
        IsTimerCompleted = false;
        stopwatch.Start();
    }

    public void AddAndStart(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Added time must be greater than zero.");
        }

        if (IsTimerMode)
        {
            ExitTimer();
        }

        stopwatch.Add(duration);
        stopwatch.Start();
    }

    public void ExitTimer()
    {
        stopwatch.Stop();
        stopwatch.Reset();
        TimerDuration = null;
        IsTimerCompleted = false;
    }

    public bool Update()
    {
        if (!TimerDuration.HasValue ||
            !stopwatch.IsRunning ||
            stopwatch.Elapsed < TimerDuration.Value)
        {
            return false;
        }

        stopwatch.Stop();
        IsTimerCompleted = true;
        return true;
    }

    public void OnSessionLocked()
    {
        stopwatch.OnSessionLocked();
    }

    public void OnSessionUnlocked()
    {
        stopwatch.OnSessionUnlocked();
    }
}
