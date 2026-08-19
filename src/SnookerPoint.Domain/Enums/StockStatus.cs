namespace SnookerPoint.Domain.Enums;

/// <summary>A product's inventory status, always shown with a written label (not colour alone).</summary>
public enum StockStatus
{
    InStock = 0,
    LowStock = 1,
    OutOfStock = 2,
    NotTracked = 3,
    Inactive = 4,
}
