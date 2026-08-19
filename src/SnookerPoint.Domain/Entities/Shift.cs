using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A cashier shift. Opening cash and the manual cash movements recorded against the
/// shift determine the expected drawer total at close. In Phase 1 there is no
/// sales cash yet, so expected cash is derived purely from the movements
/// (see <see cref="ValueObjects.Money"/> amounts).
/// </summary>
public sealed class Shift
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public DateTimeOffset OpenedUtc { get; set; }

    public Money OpeningCash { get; set; } = Money.Zero;

    public string? OpeningNote { get; set; }

    public ShiftStatus Status { get; set; } = ShiftStatus.Open;

    public DateTimeOffset? ClosedUtc { get; set; }

    /// <summary>Expected drawer cash computed and frozen at close.</summary>
    public Money? ExpectedCash { get; set; }

    /// <summary>Cash counted by the user at close.</summary>
    public Money? CountedCash { get; set; }

    /// <summary>CountedCash − ExpectedCash, frozen at close.</summary>
    public Money? Variance { get; set; }

    public string? ClosingNote { get; set; }

    public bool IsOpen => Status == ShiftStatus.Open;
}
