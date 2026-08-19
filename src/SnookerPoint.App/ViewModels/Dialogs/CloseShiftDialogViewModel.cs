using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the Close Shift dialog. Shows the expected drawer total and computes a
/// live variance as the user types the counted cash.
/// </summary>
public partial class CloseShiftDialogViewModel : ObservableObject
{
    public CloseShiftDialogViewModel(Money expectedCash)
    {
        ExpectedCash = expectedCash;
        _countedCashText = string.Empty;
        VarianceText = "—";
    }

    public Money ExpectedCash { get; }

    public string ExpectedCashText => ExpectedCash.Format();

    [ObservableProperty]
    private string _countedCashText;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string _varianceText;

    [ObservableProperty]
    private string? _errorMessage;

    public CloseShiftInput? Result { get; private set; }

    partial void OnCountedCashTextChanged(string value)
    {
        if (MoneyInput.TryParseRupees(value, out Money counted))
        {
            var variance = counted - ExpectedCash;
            var sign = variance.IsPositive ? "+" : string.Empty;
            VarianceText = variance.IsZero ? "Balanced" : $"{sign}{variance.Format()}";
        }
        else
        {
            VarianceText = "—";
        }
    }

    public bool TryConfirm()
    {
        if (!MoneyInput.TryParseRupees(CountedCashText, out Money counted))
        {
            ErrorMessage = "Please enter the counted cash (0 or more).";
            return false;
        }

        Result = new CloseShiftInput(counted, string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
        return true;
    }
}
