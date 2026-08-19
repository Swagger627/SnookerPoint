using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// An append-only manual cash-drawer movement recorded during a shift. Rows are
/// never updated or deleted; corrections are made by recording further movements.
/// </summary>
public sealed class CashMovement
{
    public int Id { get; set; }

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public CashMovementType Type { get; set; }

    /// <summary>The movement amount (always a positive magnitude; direction is by Type).</summary>
    public Money Amount { get; set; } = Money.Zero;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public int ActorUserId { get; set; }
    public User? Actor { get; set; }

    /// <summary>Optional manager who approved the movement, where policy requires it.</summary>
    public int? ApproverUserId { get; set; }
}
