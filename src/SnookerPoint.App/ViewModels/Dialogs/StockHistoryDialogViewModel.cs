using System.Globalization;
using SnookerPoint.Application.Catalog;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Read-only stock-movement history for one product.</summary>
public sealed class StockHistoryDialogViewModel
{
    public StockHistoryDialogViewModel(string productName, IReadOnlyList<StockMovementLine> history)
    {
        ProductName = productName;
        Rows = history.Select(m => new StockHistoryRow(
            m.Utc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture),
            Friendly(m.Type),
            (m.QuantityDelta >= 0 ? "+" : string.Empty) + m.QuantityDelta.ToString("0.###", CultureInfo.CurrentCulture),
            $"{m.PreviousQuantity.ToString("0.###", CultureInfo.CurrentCulture)} → {m.NewQuantity.ToString("0.###", CultureInfo.CurrentCulture)}",
            m.ReversalOfMovementId is not null ? $"Reversal · {m.Reason}" : m.Reason ?? "—",
            m.ActorName)).ToList();
    }

    public string ProductName { get; }
    public IReadOnlyList<StockHistoryRow> Rows { get; }
    public bool IsEmpty => Rows.Count == 0;

    private static string Friendly(Domain.Enums.StockMovementType type) => type switch
    {
        Domain.Enums.StockMovementType.OpeningStock => "Opening stock",
        Domain.Enums.StockMovementType.StockIn => "Stock in",
        Domain.Enums.StockMovementType.PositiveAdjustment => "Adjustment (+)",
        Domain.Enums.StockMovementType.NegativeAdjustment => "Adjustment (−)",
        Domain.Enums.StockMovementType.Waste => "Waste",
        Domain.Enums.StockMovementType.Damage => "Damage",
        Domain.Enums.StockMovementType.SupplierReturn => "Supplier return",
        _ => type.ToString(),
    };
}

public sealed record StockHistoryRow(string When, string Type, string Change, string Balance, string Reason, string User);
