using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Shifts;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The Inventory screen: shows calculated stock and status, and records stock-in,
/// adjustments, waste, damage and supplier returns (each with a before → after preview).
/// Actions are permission-gated; every movement is append-only and audited.
/// </summary>
public partial class InventoryViewModel : ObservableObject
{
    private readonly IInventoryService _inventory;
    private readonly ICategoryService _categories;
    private readonly IShiftService _shifts;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly SnookerPoint.App.Licensing.ILicenseGate _gate;

    public InventoryViewModel(
        IInventoryService inventory,
        ICategoryService categories,
        IShiftService shifts,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme,
        SnookerPoint.App.Licensing.ILicenseGate gate)
    {
        _inventory = inventory;
        _categories = categories;
        _shifts = shifts;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _gate = gate;
        _theme = theme;

        LoadCategoryFilters();
        Refresh();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<InventoryRowViewModel> Rows { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;
    public bool IsEmpty => Rows.Count == 0;

    public bool CanStockIn => Has(Permission.AddStock);
    public bool CanAdjust => Has(Permission.AdjustInventory);
    public bool CanWasteDamage => Has(Permission.RecordWasteDamage);

    public ObservableCollection<CategoryFilterOption> CategoryFilters { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private CategoryFilterOption? _selectedCategoryFilter;
    [ObservableProperty] private bool _lowStockOnly;
    [ObservableProperty] private bool _includeInactive;

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedCategoryFilterChanged(CategoryFilterOption? value) => Refresh();
    partial void OnLowStockOnlyChanged(bool value) => Refresh();
    partial void OnIncludeInactiveChanged(bool value) => Refresh();

    private int UserId => _session.CurrentUser!.Id;

    [RelayCommand]
    private void Refresh()
    {
        var filter = new InventoryFilter(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            SelectedCategoryFilter?.Id,
            LowStockOnly,
            IncludeInactive);

        Rows.Clear();
        foreach (var row in _inventory.GetInventory(filter))
        {
            Rows.Add(new InventoryRowViewModel(row));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void StockIn(InventoryRowViewModel? row) => Record(row, StockMovementType.StockIn, CanStockIn);

    [RelayCommand]
    private void Adjust(InventoryRowViewModel? row) => Record(row, StockMovementType.PositiveAdjustment, CanAdjust);

    [RelayCommand]
    private void Waste(InventoryRowViewModel? row) => Record(row, StockMovementType.Waste, CanWasteDamage);

    [RelayCommand]
    private void Damage(InventoryRowViewModel? row) => Record(row, StockMovementType.Damage, CanWasteDamage);

    [RelayCommand]
    private void SupplierReturn(InventoryRowViewModel? row) => Record(row, StockMovementType.SupplierReturn, CanAdjust);

    [RelayCommand]
    private void History(InventoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _dialogs.ShowStockHistory(row.Name, _inventory.GetHistory(row.ProductId));
    }

    private void Record(InventoryRowViewModel? row, StockMovementType initialType, bool allowed)
    {
        if (row is null)
        {
            return;
        }

        Feedback.Clear();
        if (!allowed)
        {
            Feedback.Error("You do not have permission to make this stock change.");
            return;
        }

        if (!_gate.EnsureCanOperate())
        {
            return;
        }

        if (!row.TrackInventory)
        {
            Feedback.Warning($"{row.Name} does not track inventory.");
            return;
        }

        var input = _dialogs.ShowStockMovement(new StockMovementContext(row.Name, row.CurrentStock, initialType));
        if (input is null)
        {
            return;
        }

        var shiftId = _shifts.GetCurrentShift(UserId)?.ShiftId;
        var result = _inventory.RecordMovement(
            new StockMovementRequest(row.ProductId, input.Type, input.Quantity, input.Reason, shiftId), UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success($"{row.Name} stock updated.");
    }

    [RelayCommand]
    private void OpenProducts() => _navigation.ShowProducts();

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    private void LoadCategoryFilters()
    {
        CategoryFilters.Clear();
        CategoryFilters.Add(new CategoryFilterOption(null, "All categories"));
        foreach (var c in _categories.GetAll())
        {
            CategoryFilters.Add(new CategoryFilterOption(c.Id, c.Name));
        }

        SelectedCategoryFilter = CategoryFilters[0];
    }

    private bool Has(Permission permission) =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, permission);
}

/// <summary>A read-only inventory row with calculated stock and a written status label.</summary>
public sealed class InventoryRowViewModel
{
    private readonly InventoryRow _row;

    public InventoryRowViewModel(InventoryRow row)
    {
        _row = row;
    }

    public int ProductId => _row.ProductId;
    public string Name => _row.Name;
    public string Sku => _row.Sku;
    public string Barcode => _row.Barcode ?? "—";
    public string CategoryName => _row.CategoryName;
    public bool TrackInventory => _row.TrackInventory;
    public decimal CurrentStock => _row.CurrentStock;

    public string StockText => _row.TrackInventory
        ? _row.CurrentStock.ToString("0.###", CultureInfo.CurrentCulture)
        : "—";

    public string ReorderText => _row.ReorderLevel.ToString("0.###", CultureInfo.CurrentCulture);
    public string StatusText => StatusLabels.For(_row.Status);
    public string PriceText => _row.Price.Format();

    public string LastMovementText => _row.LastMovementUtc is { } utc
        ? utc.ToLocalTime().ToString("dd MMM, h:mm tt", CultureInfo.CurrentCulture)
        : "—";
}
