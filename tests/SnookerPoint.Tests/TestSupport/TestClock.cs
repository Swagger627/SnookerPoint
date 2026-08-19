using SnookerPoint.Application.Abstractions;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>A controllable clock for deterministic tests.</summary>
public sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset? start = null)
    {
        UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow += by;
}
