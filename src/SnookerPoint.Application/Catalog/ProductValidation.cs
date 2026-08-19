using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Catalog;

/// <summary>
/// Pure, dependency-free validation for products and categories, reused by the services
/// and the CSV import so the rules live in one place. Uniqueness (SKU/barcode/category
/// name) is a database concern and checked by the services, not here.
/// </summary>
public static class ProductValidation
{
    /// <summary>Returns friendly errors for a product's core fields (empty list when valid).</summary>
    public static List<string> Validate(
        string? name,
        string? sku,
        Money price,
        Money? cost,
        decimal reorderLevel,
        decimal openingQuantity)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Product name is required.");
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            errors.Add("An internal SKU is required.");
        }

        if (price.IsNegative)
        {
            errors.Add("The selling price cannot be negative.");
        }

        if (cost is { IsNegative: true })
        {
            errors.Add("The purchase cost cannot be negative.");
        }

        if (reorderLevel < 0)
        {
            errors.Add("The reorder level cannot be negative.");
        }

        if (openingQuantity < 0)
        {
            errors.Add("The opening quantity cannot be negative.");
        }

        return errors;
    }

    /// <summary>Returns an error for an invalid category name, or null when valid.</summary>
    public static string? ValidateCategoryName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "A category name is required." : null;

    /// <summary>Normalises a barcode: trims, keeps it as text (leading zeroes preserved), null when blank.</summary>
    public static string? NormalizeBarcode(string? barcode)
    {
        var trimmed = barcode?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
