using System.Diagnostics;

namespace MiniStopwatch.Core;

public sealed class SystemMonotonicClock : IMonotonicClock
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    public TimeSpan Now => stopwatch.Elapsed;
}
