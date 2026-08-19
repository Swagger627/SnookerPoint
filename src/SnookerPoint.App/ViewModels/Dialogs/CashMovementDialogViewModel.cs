using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Backs the Cash In / Cash Out / Expense / Cash Drop dialog.</summary>
public partial class CashMovementDialogViewModel : ObservableObject
{
    public CashMovementDialogViewModel(CashMovementType type)
    {
        Type = type;
        Title = type switch
        {
            CashMovementType.CashIn => "Cash In",
            CashMovementType.CashOut => "Cash Out",
            CashMovementType.Expense => "Expense",
            CashMovementType.Drop => "Cash Drop",
            _ => "Cash Movement",
        };
        Prompt = type switch
        {
            CashMovementType.CashIn => "Add cash to the drawer.",
            CashMovementType.CashOut => "Remove cash from the drawer.",
            CashMovementType.Expense => "Record money paid out for an expense.",
            CashMovementType.Drop => "Record cash removed and secured (a drop).",
            _ => string.Empty,
        };
    }

    public CashMovementType Type { get; }

    public string Title { get; }

    public string Prompt { get; }

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public CashMovementInput? Result { get; private set; }

    public bool TryConfirm()
    {
        if (!MoneyInput.TryParseRupees(AmountText, out Money amount) || !amount.IsPositive)
        {
            ErrorMessage = "Please enter an amount greater than zero.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Please enter a reason.";
            return false;
        }

        Result = new CashMovementInput(amount, Reason.Trim());
        return true;
    }
}
