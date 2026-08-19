using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Backs the sale-discount dialog: a fixed rupee amount or a percentage, with a required reason.</summary>
public partial class DiscountDialogViewModel : ObservableObject
{
    [ObservableProperty] private bool _isFixed = true;
    [ObservableProperty] private bool _isPercentage;
    [ObservableProperty] private string _valueText = string.Empty;
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    private bool _switching;

    partial void OnIsFixedChanged(bool value)
    {
        if (value && !_switching) { _switching = true; IsPercentage = false; _switching = false; }
    }

    partial void OnIsPercentageChanged(bool value)
    {
        if (value && !_switching) { _switching = true; IsFixed = false; _switching = false; }
    }

    public DiscountResult? Result { get; private set; }

    public bool TryConfirm()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Please enter a reason for the discount.";
            return false;
        }

        if (IsPercentage)
        {
            if (!long.TryParse(ValueText?.Trim(), out var pct) || pct <= 0 || pct > 100)
            {
                ErrorMessage = "Enter a percentage between 1 and 100.";
                return false;
            }

            Result = new DiscountResult(DiscountKind.Percentage, pct, Reason.Trim());
            return true;
        }

        if (!MoneyInput.TryParseRupees(ValueText, out var amount) || amount.IsZero)
        {
            ErrorMessage = "Enter a discount amount greater than zero.";
            return false;
        }

        Result = new DiscountResult(DiscountKind.FixedAmount, amount.Paisa, Reason.Trim());
        return true;
    }
}
