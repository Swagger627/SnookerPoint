using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// An editable row on the Manage Tables screen. Holds the working copy of a table's
/// name, type, rate and active state; validation and persistence happen on Save.
/// </summary>
public partial class TableEditRowViewModel : ObservableObject
{
    /// <summary>Existing table id, or null for a table being added.</summary>
    public int? Id { get; }

    /// <summary>True when this table is currently running a live session.</summary>
    public bool InUse { get; }

    public TableEditRowViewModel(TableListItem item)
    {
        Id = item.Id;
        InUse = item.InUse;
        _name = item.Name;
        _type = item.Type;
        _rateText = FormatRate(item.HourlyRate);
        _isActive = item.IsActive;
    }

    public TableEditRowViewModel()
    {
        Id = null;
        InUse = false;
        _name = string.Empty;
        _type = TableType.Snooker;
        _rateText = "0";
        _isActive = true;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private TableType _type;
    [ObservableProperty] private string _rateText;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isSelected;

    public bool IsNew => Id is null;

    public IReadOnlyList<TableType> TypeOptions { get; } = new[] { TableType.Snooker, TableType.Pool, TableType.Other };

    /// <summary>Parses the entered rate. Returns false when it is blank/non-numeric/negative.</summary>
    public bool TryBuildDraft(out TableDraft draft, out string? error)
    {
        draft = default!;
        error = null;

        if (string.IsNullOrWhiteSpace(Name) && IsActive)
        {
            error = "Every active table needs a name.";
            return false;
        }

        if (!MoneyInput.TryParseRupees(RateText, out Money rate))
        {
            error = $"Enter a valid rate for '{DisplayName}' (0 or more).";
            return false;
        }

        draft = new TableDraft(Id, Name.Trim(), Type, rate, IsActive);
        return true;
    }

    private string DisplayName => string.IsNullOrWhiteSpace(Name) ? "new table" : Name.Trim();

    private static string FormatRate(Money rate) =>
        rate.ToRupees().ToString("0.##", CultureInfo.CurrentCulture);
}
