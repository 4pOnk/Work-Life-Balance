using System.Diagnostics;
using WorkLifeBalance.Application;

namespace WorkLifeBalance.Infrastructure;

// Anchor UTC once; wall-clock edits cannot reverse or inflate an interval within this run.
public sealed class MonotonicClock : IClock
{
    private readonly long utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly long timestamp = Stopwatch.GetTimestamp();
    public long Now => utc + (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
}
