using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A span of a session spent at one table at one hourly rate. A new segment begins
/// on transfer; the previous segment is closed. The rate is snapshotted here.
/// </summary>
public sealed class SessionSegment
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public TableSession? Session { get; set; }

    public int TableId { get; set; }

    public int SegmentIndex { get; set; }

    /// <summary>Hourly rate snapshot for this table/segment.</summary>
    public Money HourlyRate { get; set; } = Money.Zero;

    public DateTimeOffset StartUtc { get; set; }

    /// <summary>Null while this is the current segment.</summary>
    public DateTimeOffset? EndUtc { get; set; }

    /// <summary>Why the segment ended (e.g. "Transfer", "Finish").</summary>
    public string? EndReason { get; set; }
}
