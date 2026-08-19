using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// An append-only inventory movement. Stock is always recomputed from these records —
/// balances are never overwritten directly. A correction is expressed as a new reversing
/// movement that references the original; the original is never edited or deleted.
/// </summary>
public sealed class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Signed change to stock (positive for stock-in, negative for waste/return, etc.).</summary>
    public decimal QuantityDelta { get; set; }

    /// <summary>The calculated balance immediately before this movement.</summary>
    public decimal PreviousQuantity { get; set; }

    /// <summary>The calculated balance immediately after this movement.</summary>
    public decimal NewQuantity { get; set; }

    /// <summary>Reason or reference (required for adjustments, waste, damage, returns).</summary>
    public string? Reason { get; set; }

    public int ActorUserId { get; set; }

    /// <summary>The shift this happened in, where applicable.</summary>
    public int? ShiftId { get; set; }

    public DateTimeOffset Utc { get; set; }

    /// <summary>When this movement reverses an earlier one, the id of that original.</summary>
    public int? ReversalOfMovementId { get; set; }

    /// <summary>The sale that caused this movement (for Sale movements, Phase 4).</summary>
    public int? SaleId { get; set; }

    /// <summary>The specific sale line that caused this movement.</summary>
    public int? SaleLineId { get; set; }
}
