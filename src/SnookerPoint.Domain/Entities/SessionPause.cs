namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A pause period within a session. Billable time does not accrue between
/// <see cref="PausedUtc"/> and <see cref="ResumedUtc"/>. A null resume time means the
/// session is currently paused.
/// </summary>
public sealed class SessionPause
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public TableSession? Session { get; set; }

    public DateTimeOffset PausedUtc { get; set; }
    public DateTimeOffset? ResumedUtc { get; set; }

    public int PausedByUserId { get; set; }
    public int? ResumedByUserId { get; set; }
    public int ShiftId { get; set; }

    public bool IsOpen => ResumedUtc is null;
}
