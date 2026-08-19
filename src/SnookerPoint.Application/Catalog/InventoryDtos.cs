using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Catalog;

/// <summary>Filters for the inventory screen.</summary>
public sealed record InventoryFilter(
    string? SearchText = null,
    int? CategoryId = null,
    bool LowStockOnly = false,
    bool IncludeInactive = false);

/// <summary>A row on the inventory screen with the calculated current stock and status.</summary>
public sealed record InventoryRow(
    int ProductId,
    string Name,
    string Sku,
    string? Barcode,
    string CategoryName,
    bool TrackInventory,
    bool IsActive,
    decimal CurrentStock,
    decimal ReorderLevel,
    StockStatus Status,
    Money Price,
    DateTimeOffset? LastMovementUtc);

/// <summary>A request to record one stock movement. Quantity is a positive magnitude.</summary>
public sealed record StockMovementRequest(
    int ProductId,
    StockMovementType Type,
    decimal Quantity,
    string? Reason,
    int? ShiftId);

/// <summary>A stock-history line for a product.</summary>
public sealed record StockMovementLine(
    int Id,
    DateTimeOffset Utc,
    StockMovementType Type,
    decimal QuantityDelta,
    decimal PreviousQuantity,
    decimal NewQuantity,
    string? Reason,
    string ActorName,
    int? ReversalOfMovementId);
