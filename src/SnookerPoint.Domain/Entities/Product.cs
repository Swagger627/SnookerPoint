using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A single stock-keeping unit (SKU). Each distinct flavour, size or barcode is a
/// separate product — different barcodes/sizes are never merged into one record.
/// Money (cost/price) is stored as integer minor units (paisa); quantities live in the
/// append-only <see cref="StockMovement"/> log, not on the product.
/// </summary>
public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Internal stock-keeping unit code. Unique across all products.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Scanned barcode, stored as text to preserve leading zeroes. Unique when supplied.</summary>
    public string? Barcode { get; set; }

    public int CategoryId { get; set; }

    public string? Brand { get; set; }

    /// <summary>Flavour or variant, e.g. "Salted", "Masala".</summary>
    public string? Variant { get; set; }

    /// <summary>Pack size, e.g. "330 ml", "25 g".</summary>
    public string? Size { get; set; }

    public ProductUnit Unit { get; set; } = ProductUnit.Each;

    /// <summary>Purchase cost per unit (optional).</summary>
    public Money? Cost { get; set; }

    /// <summary>Selling price per unit (required).</summary>
    public Money Price { get; set; } = Money.Zero;

    public bool TrackInventory { get; set; } = true;

    /// <summary>
    /// When true, stock is allowed to go below zero (an authorised opt-in). Default false,
    /// so a movement that would drive stock negative is rejected.
    /// </summary>
    public bool AllowNegativeStock { get; set; }

    /// <summary>Low-stock threshold; a tracked product at or below this is "Low Stock".</summary>
    public decimal ReorderLevel { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Managed relative path under the app images folder (never an absolute path).</summary>
    public string? ImagePath { get; set; }

    /// <summary>Hash of the stored image file, for integrity and de-duplication.</summary>
    public string? ImageHash { get; set; }

    /// <summary>Original filename of the imported image, for display only.</summary>
    public string? ImageOriginalName { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
