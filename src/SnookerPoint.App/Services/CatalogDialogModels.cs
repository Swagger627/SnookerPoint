using SnookerPoint.Application.Catalog;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.Services;

/// <summary>A category choice for the product editor's category dropdown.</summary>
public sealed record CategoryOption(int Id, string Name);

/// <summary>What to do with a product's image when saving the editor.</summary>
public enum ProductImageAction
{
    Keep,
    Replace,
    Remove,
}

/// <summary>What the product editor needs to open in add / edit / duplicate mode.</summary>
public sealed record ProductEditorContext(
    string Title,
    bool IsNew,
    ProductDetail? Existing,
    IReadOnlyList<CategoryOption> Categories,
    string? PrefillBarcode,
    string? ExistingImageFullPath);

/// <summary>The product editor's validated result (money/quantities already parsed).</summary>
public sealed record ProductEditorResult(
    string Name,
    string Sku,
    string? Barcode,
    int CategoryId,
    string? Brand,
    string? Variant,
    string? Size,
    ProductUnit Unit,
    Money? Cost,
    Money Price,
    bool TrackInventory,
    bool AllowNegativeStock,
    decimal ReorderLevel,
    decimal OpeningQuantity,
    string? Notes,
    ProductImageAction ImageAction,
    string? NewImageSourcePath);

/// <summary>What the stock-movement dialog needs.</summary>
public sealed record StockMovementContext(
    string ProductName,
    decimal CurrentStock,
    StockMovementType InitialType);

/// <summary>The stock-movement dialog's validated result.</summary>
public sealed record StockMovementResult(StockMovementType Type, decimal Quantity, string? Reason);
