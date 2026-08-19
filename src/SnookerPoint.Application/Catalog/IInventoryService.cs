using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Catalog;

/// <summary>
/// Records and reads append-only stock movements. Current stock is always recomputed
/// from the movement log. Movements that would drive stock negative are rejected unless
/// negative stock is explicitly allowed. Corrections are made by a reversing movement
/// that references the original — originals are never edited.
/// </summary>
public interface IInventoryService
{
    IReadOnlyList<InventoryRow> GetInventory(InventoryFilter filter);

    IReadOnlyList<StockMovementLine> GetHistory(int productId);

    /// <summary>The current calculated stock for a product.</summary>
    decimal GetCurrentStock(int productId);

    OperationResult RecordMovement(StockMovementRequest request, int actorUserId);

    /// <summary>Reverses an earlier movement by appending a compensating one.</summary>
    OperationResult ReverseMovement(int movementId, string reason, int actorUserId, int? shiftId);
}
