using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Backs the price-override dialog: a new non-negative unit price with a required reason.</summary>
public partial class PriceOverrideDialogViewModel : ObservableObject
{
    public PriceOverrideDialogViewModel(string productName, Money currentPrice)
    {
        ProductName = productName;
        CurrentPriceText = currentPrice.Format();
        NewPriceText = currentPrice.ToRupees().ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    public string ProductName { get; }
    public string CurrentPriceText { get; }

    [ObservableProperty] private string _newPriceText = string.Empty;
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public PriceOverrideResult? Result { get; private set; }

    public bool TryConfirm()
    {
        ErrorMessage = null;

        if (!MoneyInput.TryParseRupees(NewPriceText, out var price))
        {
            ErrorMessage = "Enter a valid price (0 or more).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Please enter a reason for the price change.";
            return false;
        }

        Result = new PriceOverrideResult(price, Reason.Trim());
        return true;
    }
}
