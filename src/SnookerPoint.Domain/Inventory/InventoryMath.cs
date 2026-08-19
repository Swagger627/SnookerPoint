using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Inventory;

/// <summary>
/// Pure inventory rules: whether a movement type increases or decreases stock, the signed
/// delta for a magnitude, and how a calculated balance maps to a status label. No
/// dependencies, so it is trivially testable and reused by the services and UI.
/// </summary>
public static class InventoryMath
{
    /// <summary>True for movement types that add stock.</summary>
    public static bool IsIncrease(StockMovementType type) => type switch
    {
        StockMovementType.OpeningStock => true,
        StockMovementType.StockIn => true,
        StockMovementType.PositiveAdjustment => true,
        _ => false,
    };

    /// <summary>
    /// Converts a non-negative magnitude into a signed delta for the given type. Callers
    /// pass the quantity as a positive number; direction comes from the type.
    /// </summary>
    public static decimal SignedDelta(StockMovementType type, decimal magnitude) =>
        IsIncrease(type) ? magnitude : -magnitude;

    /// <summary>Classifies a calculated balance into a status label.</summary>
    public static StockStatus Classify(bool isActive, bool trackInventory, decimal quantity, decimal reorderLevel)
    {
        if (!isActive)
        {
            return StockStatus.Inactive;
        }

        if (!trackInventory)
        {
            return StockStatus.NotTracked;
        }

        if (quantity <= 0)
        {
            return StockStatus.OutOfStock;
        }

        return quantity <= reorderLevel ? StockStatus.LowStock : StockStatus.InStock;
    }
}
