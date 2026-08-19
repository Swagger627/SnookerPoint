using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// One product line on a sale. Snapshots the product's identity and price at the time of
/// sale so later catalogue changes never alter historical sales. Quantity supports whole
/// and reasonable decimal amounts.
/// </summary>
public sealed class SaleLine
{
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    /// <summary>The product sold. Kept as a restrict FK; the snapshots below preserve history.</summary>
    public int? ProductId { get; set; }

    public string NameSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? BarcodeSnapshot { get; set; }

    public decimal Quantity { get; set; }

    public Money UnitPrice { get; set; } = Money.Zero;

    /// <summary>The unit cost at sale time, kept for future profit reporting.</summary>
    public Money? CostSnapshot { get; set; }

    /// <summary>The original unit price before an authorised override, preserved for audit.</summary>
    public Money? OriginalUnitPrice { get; set; }

    public Money LineTotal { get; set; } = Money.Zero;

    /// <summary>Whether inventory was tracked for this product at sale time (drives stock deduction).</summary>
    public bool TrackInventory { get; set; }
}
