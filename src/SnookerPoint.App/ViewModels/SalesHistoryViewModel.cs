using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// Completed-sales history: search by number, filter by type, view details and reprint
/// receipts (reprints are permission-gated and clearly marked). Completed sales are
/// read-only here.
/// </summary>
public partial class SalesHistoryViewModel : ObservableObject
{
    private readonly ISalesQueryService _salesQuery;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    public SalesHistoryViewModel(
        ISalesQueryService salesQuery,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme)
    {
        _salesQuery = salesQuery;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;

        TypeFilters = new[]
        {
            new SaleTypeFilter(null, "All sales"),
            new SaleTypeFilter(SaleType.Walkin, "Walk-in"),
            new SaleTypeFilter(SaleType.Table, "Table"),
        };
        _selectedType = TypeFilters[0];

        Refresh();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<SaleRow> Rows { get; } = new();
    public IReadOnlyList<SaleTypeFilter> TypeFilters { get; }

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;
    public bool IsEmpty => Rows.Count == 0;
    public bool CanReprint => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ReprintReceipt);

    private int UserId => _session.CurrentUser!.Id;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private SaleTypeFilter _selectedType;

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedTypeChanged(SaleTypeFilter value) => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        int? number = int.TryParse(SearchText?.Trim(), out var n) ? n : null;
        var filter = new SalesHistoryFilter(SaleNumber: number, Type: SelectedType?.Value);

        Rows.Clear();
        foreach (var item in _salesQuery.GetHistory(filter))
        {
            Rows.Add(new SaleRow(item));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void ViewDetails(SaleRow? row)
    {
        if (row is null)
        {
            return;
        }

        var snapshot = _salesQuery.GetReceiptSnapshot(row.SaleId);
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            Feedback.Error("This sale's receipt could not be loaded.");
            return;
        }

        _dialogs.ShowReceiptPreview($"Sale #{row.SaleNumber}", snapshot);
    }

    [RelayCommand]
    private void Reprint(SaleRow? row)
    {
        if (row is null)
        {
            return;
        }

        Feedback.Clear();
        if (!CanReprint)
        {
            Feedback.Error("You do not have permission to reprint receipts.");
            return;
        }

        var snapshot = _salesQuery.GetReceiptSnapshot(row.SaleId);
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            Feedback.Error("This sale's receipt could not be loaded.");
            return;
        }

        var reprintText = "*** REPRINT ***\r\n\r\n" + snapshot;
        var printed = _dialogs.ShowReceiptPreview($"Reprint · Sale #{row.SaleNumber}", reprintText);
        var result = _salesQuery.MarkReceiptPrinted(row.SaleId, UserId, isReprint: true);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success(printed ? "Receipt reprinted." : "Reprint recorded.");
    }

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void NewSale() => _navigation.ShowNewSale();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();
}

/// <summary>A sale-type filter choice.</summary>
public sealed record SaleTypeFilter(SaleType? Value, string Label);

/// <summary>A read-only sales-history row.</summary>
public sealed class SaleRow
{
    private readonly SaleHistoryItem _item;

    public SaleRow(SaleHistoryItem item)
    {
        _item = item;
    }

    public int SaleId => _item.SaleId;
    public int SaleNumber => _item.SaleNumber;
    public string When => _item.CompletedUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture);
    public string TypeText => _item.Type == SaleType.Table
        ? (_item.TableSessionNumber is { } n ? $"Table (#{n})" : "Table")
        : "Walk-in";
    public string CashierName => _item.CashierName;
    public string TotalText => _item.Total.Format();
    public string PaymentSummary => _item.PaymentSummary;
    public string PrintCountText => string.Empty;
}
