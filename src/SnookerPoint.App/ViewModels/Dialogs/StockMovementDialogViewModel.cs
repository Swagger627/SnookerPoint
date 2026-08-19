using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Inventory;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the stock-movement dialog (stock in, adjustments, waste, damage, supplier
/// return). Shows the current stock and a before → after preview, and requires a reason
/// for everything except plain stock-in.
/// </summary>
public partial class StockMovementDialogViewModel : ObservableObject
{
    public StockMovementDialogViewModel(StockMovementContext context)
    {
        ProductName = context.ProductName;
        CurrentStock = context.CurrentStock;
        SelectedType = context.InitialType;
    }

    public string ProductName { get; }
    public decimal CurrentStock { get; }
    public string CurrentStockText => CurrentStock.ToString("0.###", CultureInfo.CurrentCulture);

    public Array MovementTypes => new[]
    {
        StockMovementType.StockIn,
        StockMovementType.PositiveAdjustment,
        StockMovementType.NegativeAdjustment,
        StockMovementType.Waste,
        StockMovementType.Damage,
        StockMovementType.SupplierReturn,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReasonRequired))]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private StockMovementType _selectedType = StockMovementType.StockIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private string _quantityText = string.Empty;

    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public bool ReasonRequired => SelectedType != StockMovementType.StockIn;

    public string PreviewText
    {
        get
        {
            if (!TryParseQuantity(out var qty))
            {
                return $"{CurrentStockText} → —";
            }

            var next = CurrentStock + InventoryMath.SignedDelta(SelectedType, qty);
            return $"{CurrentStockText} → {next.ToString("0.###", CultureInfo.CurrentCulture)}";
        }
    }

    public StockMovementResult? Result { get; private set; }

    public bool TryConfirm()
    {
        ErrorMessage = null;

        if (!TryParseQuantity(out var qty) || qty <= 0)
        {
            ErrorMessage = "Enter a quantity greater than zero.";
            return false;
        }

        if (ReasonRequired && string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Please enter a reason.";
            return false;
        }

        Result = new StockMovementResult(SelectedType, qty, string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim());
        return true;
    }

    private bool TryParseQuantity(out decimal qty) =>
        decimal.TryParse((QuantityText ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out qty);
}
