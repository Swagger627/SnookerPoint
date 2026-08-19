using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Catalog;

/// <summary>Metadata found for a barcode (never a price — pricing is always operator-entered).</summary>
public sealed record ProductLookupResult(
    string Barcode,
    string? Name,
    string? Brand,
    string? Size,
    string? Category);

/// <summary>
/// An optional, manually-triggered online barcode metadata lookup. The application is
/// fully usable offline; this is only invoked when the user presses "Lookup". It never
/// returns or sets a price, and results are only applied after the user confirms.
/// </summary>
public interface IProductLookupProvider
{
    /// <summary>Looks up metadata for a barcode. Returns a friendly failure when offline or not found.</summary>
    Task<OperationResult<ProductLookupResult>> LookupAsync(string barcode, CancellationToken cancellationToken = default);
}
