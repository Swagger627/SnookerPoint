using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Sales;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Sales;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the split-payment dialog. The user adds one or more payment portions; the
/// remaining balance updates live. Cash may over-pay (producing change); electronic
/// portions cannot. Completion is enabled only when the applied total exactly covers the
/// amount due. Guards against duplicate submissions by validating each add.
/// </summary>
public partial class PaymentDialogViewModel : ObservableObject
{
    public PaymentDialogViewModel(PaymentDialogContext context)
    {
        AmountDue = context.AmountDue;
        Methods = context.Methods;
        _selectedMethod = Methods.FirstOrDefault();
        ResetAmountToRemaining();
    }

    public Money AmountDue { get; }
    public IReadOnlyList<PaymentMethodOption> Methods { get; }

    public ObservableCollection<PaymentRow> Rows { get; } = new();

    public string AmountDueText => AmountDue.Format();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCashSelected))]
    private PaymentMethodOption? _selectedMethod;

    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _cashReceivedText = string.Empty;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingText))]
    [NotifyPropertyChangedFor(nameof(ChangeText))]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    private Money _remaining;

    public bool IsCashSelected => SelectedMethod?.Kind == PaymentMethodKind.Cash;

    public string RemainingText => Remaining.Format();

    public Money Change => ComputeChange();
    public string ChangeText => Change.Format();

    public bool CanComplete => Rows.Count > 0 && Remaining.IsZero;

    public PaymentDialogResult? Result { get; private set; }

    partial void OnSelectedMethodChanged(PaymentMethodOption? value)
    {
        CashReceivedText = string.Empty;
        Reference = string.Empty;
    }

    [RelayCommand]
    private void AddPayment()
    {
        ErrorMessage = null;

        if (SelectedMethod is null)
        {
            ErrorMessage = "Choose a payment method.";
            return;
        }

        if (!MoneyInput.TryParseRupees(AmountText, out var amount) || amount.IsZero)
        {
            ErrorMessage = "Enter a valid amount greater than zero.";
            return;
        }

        Money? received = null;
        if (IsCashSelected)
        {
            if (string.IsNullOrWhiteSpace(CashReceivedText))
            {
                received = amount;
            }
            else if (!MoneyInput.TryParseRupees(CashReceivedText, out var r))
            {
                ErrorMessage = "Enter a valid cash-received amount.";
                return;
            }
            else
            {
                received = r;
                if (received < amount)
                {
                    ErrorMessage = "Cash received cannot be less than the amount.";
                    return;
                }
            }
        }

        // Validate against the running total so applied never exceeds the amount due.
        var entries = Rows.Select(ToEntry).Append(new PaymentEntry(SelectedMethod.Kind, amount, received)).ToList();
        var validation = PaymentMath.Validate(AmountDue, entries);
        if (validation.Applied > AmountDue)
        {
            ErrorMessage = "That would pay more than the amount due.";
            return;
        }

        Rows.Add(new PaymentRow(
            SelectedMethod.Id, SelectedMethod.Name, SelectedMethod.Kind, amount, received,
            string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim()));

        Recompute();
        // Prime the next entry with whatever is still owed.
        ResetAmountToRemaining();
        CashReceivedText = string.Empty;
        Reference = string.Empty;
        OnPropertyChanged(nameof(Change));
        OnPropertyChanged(nameof(ChangeText));
    }

    [RelayCommand]
    private void RemoveRow(PaymentRow? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
            Recompute();
        }
    }

    public bool TryComplete()
    {
        var entries = Rows.Select(ToEntry).ToList();
        var validation = PaymentMath.Validate(AmountDue, entries);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.Error;
            return false;
        }

        var payments = Rows.Select(r => new PaymentInput(r.MethodId, r.Amount, r.CashReceived, r.Reference, null)).ToList();
        Result = new PaymentDialogResult(payments, validation.Change);
        return true;
    }

    private void Recompute()
    {
        var validation = PaymentMath.Validate(AmountDue, Rows.Select(ToEntry).ToList());
        Remaining = validation.Remaining;
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(Change));
        OnPropertyChanged(nameof(ChangeText));
    }

    private void ResetAmountToRemaining()
    {
        var remaining = Remaining.IsZero && Rows.Count == 0 ? AmountDue : Remaining;
        AmountText = remaining.IsPositive ? remaining.ToRupees().ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
    }

    private Money ComputeChange() =>
        PaymentMath.Validate(AmountDue, Rows.Select(ToEntry).ToList()).Change;

    private static PaymentEntry ToEntry(PaymentRow r) => new(r.Kind, r.Amount, r.CashReceived);
}

/// <summary>A payment portion shown in the dialog list.</summary>
public sealed record PaymentRow(int MethodId, string MethodName, PaymentMethodKind Kind, Money Amount, Money? CashReceived, string? Reference)
{
    public string AmountText => Amount.Format();
    public string Detail => Kind == PaymentMethodKind.Cash && CashReceived is { } r && r > Amount
        ? $"received {r.Format()}, change {(r - Amount).Format()}"
        : (Reference is null ? string.Empty : $"ref {Reference}");
}
