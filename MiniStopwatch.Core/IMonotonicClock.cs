namespace MiniStopwatch.Core;

public interface IMonotonicClock
{
    TimeSpan Now { get; }
}
