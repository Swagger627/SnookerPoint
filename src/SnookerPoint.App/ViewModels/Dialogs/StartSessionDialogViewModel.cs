using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the Start Session dialog. The user picks a billing type: Hourly (charge by
/// time at the table's snapshotted rate and policy) or Fixed (a single agreed charge
/// that elapsed time never changes). A fixed charge is required and non-negative when
/// Fixed is selected.
/// </summary>
public partial class StartSessionDialogViewModel : ObservableObject
{
    public StartSessionDialogViewModel(string tableName, string tableType, string rateText, string policySummary, string currentTimeText)
    {
        TableName = tableName;
        TableType = tableType;
        RateText = rateText;
        PolicySummary = policySummary;
        CurrentTimeText = currentTimeText;
    }

    public string TableName { get; }
    public string TableType { get; }
    public string RateText { get; }
    public string PolicySummary { get; }
    public string CurrentTimeText { get; }

    [ObservableProperty] private string _customerLabel = string.Empty;
    [ObservableProperty] private string _note = string.Empty;

    // Billing type (mutually exclusive; Hourly by default).
    [ObservableProperty] private bool _isHourly = true;
    [ObservableProperty] private bool _isFixed;
    [ObservableProperty] private string _fixedAmountRupees = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    private bool _switching;

    partial void OnIsHourlyChanged(bool value)
    {
        if (value && !_switching) { _switching = true; IsFixed = false; _switching = false; }
    }

    partial void OnIsFixedChanged(bool value)
    {
        if (value && !_switching) { _switching = true; IsHourly = false; _switching = false; }
    }

    public StartSessionInput? Result { get; private set; }

    public bool TryConfirm()
    {
        var label = string.IsNullOrWhiteSpace(CustomerLabel) ? null : CustomerLabel.Trim();
        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();

        if (IsFixed)
        {
            if (!MoneyInput.TryParseRupees(FixedAmountRupees, out Money fixedAmount))
            {
                ErrorMessage = "Enter a valid fixed charge in Rs (0 or more).";
                return false;
            }

            Result = new StartSessionInput(label, note, BillingType.Fixed, fixedAmount);
            return true;
        }

        Result = new StartSessionInput(label, note, BillingType.Hourly, null);
        return true;
    }
}
