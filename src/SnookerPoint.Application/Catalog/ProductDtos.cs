using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Catalog;

/// <summary>Which active/inactive products to include in a list.</summary>
public enum ProductActiveFilter
{
    All = 0,
    ActiveOnly = 1,
    InactiveOnly = 2,
}

/// <summary>Filters for the products list. All parts are optional/combinable.</summary>
public sealed record ProductFilter(
    string? SearchText = null,
    int? CategoryId = null,
    ProductActiveFilter Active = ProductActiveFilter.ActiveOnly,
    bool LowStockOnly = false);

/// <summary>A product row for the catalogue/inventory lists (carries the calculated stock).</summary>
public sealed record ProductListItem(
    int Id,
    string Name,
    string Sku,
    string? Barcode,
    int CategoryId,
    string CategoryName,
    string? Brand,
    string? Variant,
    string? Size,
    ProductUnit Unit,
    Money? Cost,
    Money Price,
    bool TrackInventory,
    decimal ReorderLevel,
    bool IsActive,
    decimal CurrentStock,
    StockStatus Status,
    string? ImagePath,
    DateTimeOffset? LastMovementUtc);

/// <summary>Full detail for the product editor.</summary>
public sealed record ProductDetail(
    int Id,
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
    decimal ReorderLevel,
    bool AllowNegativeStock,
    bool IsActive,
    string? ImagePath,
    string? ImageHash,
    string? ImageOriginalName,
    string? Notes,
    decimal CurrentStock);

/// <summary>Everything needed to create a product, including any opening stock.</summary>
public sealed record CreateProductRequest(
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
    decimal ReorderLevel,
    decimal OpeningQuantity = 0m,
    bool AllowNegativeStock = false,
    bool IsActive = true,
    string? Notes = null,
    string? ImagePath = null,
    string? ImageHash = null,
    string? ImageOriginalName = null);

/// <summary>Everything the editor can change on an existing product (stock excluded).</summary>
public sealed record UpdateProductRequest(
    int Id,
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
    decimal ReorderLevel,
    bool AllowNegativeStock = false,
    string? Notes = null,
    string? ImagePath = null,
    string? ImageHash = null,
    string? ImageOriginalName = null);
