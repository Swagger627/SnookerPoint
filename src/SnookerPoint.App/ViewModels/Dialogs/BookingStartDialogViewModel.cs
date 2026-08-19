using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the "start booking" dialog. The operator confirms which table to open (the
/// reserved one when it is free, or a free alternative when it is occupied) and picks a
/// billing type — Hourly or a Fixed agreed charge, matching the normal session workflow.
/// </summary>
public partial class BookingStartDialogViewModel : ObservableObject
{
    public BookingStartDialogViewModel(BookingStartContext context)
    {
        CustomerName = context.CustomerName;
        ReservedTableName = context.ReservedTableName;
        ReservedInUse = context.ReservedInUse;
        TableChoices = context.TableChoices;
        _selectedTable = TableChoices.FirstOrDefault();

        OccupiedNotice = ReservedInUse
            ? $"{ReservedTableName} is currently in use. Choose a free alternative table below."
            : string.Empty;
    }

    public string CustomerName { get; }
    public string ReservedTableName { get; }
    public bool ReservedInUse { get; }
    public string OccupiedNotice { get; }
    public bool HasOccupiedNotice => !string.IsNullOrEmpty(OccupiedNotice);
    public IReadOnlyList<BookingTableOption> TableChoices { get; }

    [ObservableProperty] private BookingTableOption? _selectedTable;
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

    public BookingStartResult? Result { get; private set; }

    public bool TryConfirm()
    {
        if (SelectedTable is null)
        {
            ErrorMessage = "Please choose a table to start on.";
            return false;
        }

        if (IsFixed)
        {
            if (!MoneyInput.TryParseRupees(FixedAmountRupees, out Money fixedAmount))
            {
                ErrorMessage = "Enter a valid fixed charge in Rs (0 or more).";
                return false;
            }

            Result = new BookingStartResult(SelectedTable.TableId, BillingType.Fixed, fixedAmount);
            return true;
        }

        Result = new BookingStartResult(SelectedTable.TableId, BillingType.Hourly, null);
        return true;
    }
}
