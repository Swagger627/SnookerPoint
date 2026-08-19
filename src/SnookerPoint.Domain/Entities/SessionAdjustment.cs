using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// An append-only, audited correction to a session. Records the reason, the before
/// and after values, the approving user and shift, and (for charge adjustments) the
/// amount. History is never overwritten silently — corrections accumulate here.
/// </summary>
public sealed class SessionAdjustment
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public TableSession? Session { get; set; }

    public SessionAdjustmentType Type { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>The value before the correction (human-readable), preserved for audit.</summary>
    public string? OldValue { get; set; }

    /// <summary>The value after the correction (human-readable).</summary>
    public string? NewValue { get; set; }

    /// <summary>Signed monetary amount for a charge adjustment.</summary>
    public Money? Amount { get; set; }

    public int ApprovedByUserId { get; set; }
    public int ShiftId { get; set; }
    public DateTimeOffset Utc { get; set; }
}
