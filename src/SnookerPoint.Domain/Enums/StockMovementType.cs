namespace SnookerPoint.Domain.Enums;

/// <summary>
/// The kinds of append-only stock movement supported in Phase 3. A <c>Sale</c> movement
/// is deliberately absent — checkout is not implemented in this phase.
/// </summary>
public enum StockMovementType
{
    OpeningStock = 0,
    StockIn = 1,
    PositiveAdjustment = 2,
    NegativeAdjustment = 3,
    Waste = 4,
    Damage = 5,
    SupplierReturn = 6,

    /// <summary>Stock consumed by a completed sale (Phase 4). Decreases stock.</summary>
    Sale = 7,
}
