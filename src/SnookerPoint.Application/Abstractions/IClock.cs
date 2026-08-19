namespace SnookerPoint.Application.Abstractions;

/// <summary>
/// Abstraction over the system clock. All timestamps in the system are stored in
/// UTC; sessions, shifts and licensing all depend on a single, injectable time
/// source so behaviour is testable and clock handling is centralised.
/// </summary>
public interface IClock
{
    /// <summary>The current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
