using SnookerPoint.Application.Abstractions;

namespace SnookerPoint.Infrastructure.Time;

/// <summary>Default <see cref="IClock"/> backed by the machine clock, in UTC.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
