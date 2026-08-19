using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Shifts;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The New Sale POS screen. Left: product search and barcode entry. Right: the cart with
/// quantities, totals, discount, hold/cancel and Pay Now. Also lists Awaiting-Checkout
/// table sessions and held sales. A draft persists in SQLite the whole time.
/// </summary>
public partial class NewSaleViewModel : ObservableObject
{
    private readonly ISaleService _sales;
    private readonly IProductService _products;
    private readonly IPaymentMethodService _paymentMethods;
    private readonly ISalesQueryService _salesQuery;
    private readonly IShiftService _shifts;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    private int _saleId;
    private string? _lastScanned;

    public NewSaleViewModel(
        ISaleService sales,
        IProductService products,
        IPaymentMethodService paymentMethods,
        ISalesQueryService salesQuery,
        IShiftService shifts,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme)
    {
        _sales = sales;
        _products = products;
        _paymentMethods = paymentMethods;
        _salesQuery = salesQuery;
        _shifts = shifts;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;

        StartWalkin();
        SearchProducts();
        RefreshSideLists();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<CartLineRow> Cart { get; } = new();
    public ObservableCollection<ProductPickRow> Products { get; } = new();
    public ObservableCollection<AwaitingCheckoutItem> Awaiting { get; } = new();
    public ObservableCollection<HeldSaleListItem> Held { get; } = new();

    /// <summary>Raised after a product is added so the view can refocus the barcode box.</summary>
    public event Action? FocusSearchRequested;

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    private int UserId => _session.CurrentUser!.Id;

    public bool CanApplyDiscount => Has(Permission.ApplyDiscount);
    public bool CanOverridePrice => Has(Permission.OverridePrice);
    public bool CanCompletePayment => Has(Permission.CompletePayment);

    [ObservableProperty] private string _searchText = string.Empty;

    // Cart summary
    [ObservableProperty] private string _subtotalText = "Rs 0";
    [ObservableProperty] private string _tableChargeText = "Rs 0";
    [ObservableProperty] private string _discountText = "Rs 0";
    [ObservableProperty] private string _totalText = "Rs 0";
    [ObservableProperty] private bool _hasTableCharge;
    [ObservableProperty] private bool _hasDiscount;
    [ObservableProperty] private string? _saleLabel;
    [ObservableProperty] private bool _isTableCheckout;
    [ObservableProperty] private bool _isEmpty = true;

    partial void OnSearchTextChanged(string value)
    {
        _lastScanned = null;
        SearchProducts();
    }

    // ==================== SEARCH / BARCODE ====================

    [RelayCommand]
    private void SearchProducts()
    {
        Products.Clear();
        var filter = new ProductFilter(string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            null, ProductActiveFilter.ActiveOnly);
        foreach (var p in _products.GetList(filter).Take(60))
        {
            Products.Add(new ProductPickRow(p.Id, p.Name, p.Sku, p.Barcode, p.Price));
        }
    }

    /// <summary>Enter in the search box: treat as a scanned barcode and add the product.</summary>
    [RelayCommand]
    private void Scan()
    {
        var code = SearchText?.Trim();
        if (string.IsNullOrEmpty(code) || code == _lastScanned)
        {
            return;
        }

        _lastScanned = code;
        var found = _products.FindByBarcode(code);
        if (found is null)
        {
            Feedback.Warning($"No product found for barcode {code}.");
            return;
        }

        AddProductById(found.Id, found.Name);
        SearchText = string.Empty;
        FocusSearchRequested?.Invoke();
    }

    [RelayCommand]
    private void AddProduct(ProductPickRow? row)
    {
        if (row is not null)
        {
            AddProductById(row.Id, row.Name);
            FocusSearchRequested?.Invoke();
        }
    }

    private void AddProductById(int productId, string name)
    {
        Feedback.Clear();
        var result = _sales.AddProduct(_saleId, productId, 1, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Reload();
    }

    // ==================== CART ====================

    [RelayCommand]
    private void Increase(CartLineRow? row) => ChangeQty(row, +1);

    [RelayCommand]
    private void Decrease(CartLineRow? row) => ChangeQty(row, -1);

    private void ChangeQty(CartLineRow? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        var next = row.Quantity + delta;
        if (next <= 0)
        {
            RemoveLine(row);
            return;
        }

        Report(_sales.SetLineQuantity(_saleId, row.LineId, next, UserId));
    }

    [RelayCommand]
    private void RemoveLine(CartLineRow? row)
    {
        if (row is not null)
        {
            Report(_sales.RemoveLine(_saleId, row.LineId, UserId));
        }
    }

    [RelayCommand]
    private void OverridePrice(CartLineRow? row)
    {
        if (row is null || !CanOverridePrice)
        {
            return;
        }

        var input = _dialogs.ShowPriceOverride(row.Name, row.UnitPrice);
        if (input is null)
        {
            return;
        }

        Report(_sales.OverrideLinePrice(_saleId, row.LineId, input.NewUnitPrice, input.Reason, UserId), "Price updated.");
    }

    [RelayCommand]
    private void ApplyDiscount()
    {
        if (!CanApplyDiscount)
        {
            return;
        }

        var input = _dialogs.ShowDiscount();
        if (input is null)
        {
            return;
        }

        Report(_sales.ApplyDiscount(_saleId, input.Kind, input.Value, input.Reason, UserId), "Discount applied.");
    }

    [RelayCommand]
    private void RemoveDiscount() => Report(_sales.RemoveDiscount(_saleId, UserId));

    // ==================== HOLD / CANCEL / PAY ====================

    [RelayCommand]
    private void Hold()
    {
        Feedback.Clear();
        if (Cart.Count == 0 && !IsTableCheckout)
        {
            Feedback.Warning("Add something before holding the sale.");
            return;
        }

        var result = _sales.Hold(_saleId, SaleLabel, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        StartWalkin();
        RefreshSideLists();
        Feedback.Success("Sale held successfully.");
    }

    [RelayCommand]
    private void Cancel()
    {
        Feedback.Clear();
        if (Cart.Count > 0 && !_dialogs.Confirm("Cancel sale", "Discard this sale? Nothing will be charged."))
        {
            return;
        }

        _sales.Cancel(_saleId, UserId);
        StartWalkin();
        RefreshSideLists();
        Feedback.Success("Sale cancelled.");
    }

    [RelayCommand]
    private void PayNow()
    {
        Feedback.Clear();

        if (!CanCompletePayment)
        {
            Feedback.Error("You do not have permission to take payment.");
            return;
        }

        if (Cart.Count == 0 && !IsTableCheckout)
        {
            Feedback.Warning("Add at least one product or a table charge first.");
            return;
        }

        var shift = _shifts.GetCurrentShift(UserId);
        if (shift is null)
        {
            Feedback.Error("An open shift is required before taking payment. Open a shift from Home first.");
            return;
        }

        var draft = _sales.GetDraft(_saleId);
        if (draft is null)
        {
            Feedback.Error("This sale could not be loaded.");
            return;
        }

        var methods = _paymentMethods.GetActive();
        var payment = _dialogs.ShowPayment(new PaymentDialogContext(draft.Totals.Total, methods));
        if (payment is null)
        {
            return;
        }

        var result = _sales.Complete(new CompleteSaleRequest(_saleId, payment.Payments, UserId, shift.ShiftId));
        if (result.Failed || result.Value is null)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        var completed = result.Value;
        // Offer the receipt; a printer failure never undoes the completed sale.
        var printed = _dialogs.ShowReceiptPreview($"Receipt #{completed.SaleNumber}", completed.ReceiptText);
        _salesQuery.MarkReceiptPrinted(completed.SaleId, UserId, isReprint: false);

        StartWalkin();
        RefreshSideLists();
        Feedback.Success(printed
            ? $"Sale #{completed.SaleNumber} completed. Receipt sent to printer."
            : $"Sale #{completed.SaleNumber} completed successfully.");
    }

    // ==================== TABLE CHECKOUT / HELD ====================

    [RelayCommand]
    private void StartTableCheckout(AwaitingCheckoutItem? item)
    {
        if (item is null)
        {
            return;
        }

        Feedback.Clear();
        // Discard an empty walk-in draft so we don't leave orphans.
        if (!IsTableCheckout && Cart.Count == 0)
        {
            _sales.Cancel(_saleId, UserId);
        }

        var draft = _sales.CreateTableCheckoutDraft(item.SessionId, UserId);
        if (draft.Failed)
        {
            Feedback.Error(draft.ErrorMessage);
            StartWalkin();
            return;
        }

        _saleId = draft.Value;
        Reload();
        RefreshSideLists();
        Feedback.Success($"Checkout started for session #{item.SessionNumber}.");
    }

    [RelayCommand]
    private void ResumeHeld(HeldSaleListItem? item)
    {
        if (item is null)
        {
            return;
        }

        Feedback.Clear();
        if (!IsTableCheckout && Cart.Count == 0)
        {
            _sales.Cancel(_saleId, UserId);
        }

        var result = _sales.Reopen(item.SaleId, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        _saleId = item.SaleId;
        Reload();
        RefreshSideLists();
    }

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void OpenSalesHistory() => _navigation.ShowSalesHistory();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    // ==================== HELPERS ====================

    private void StartWalkin()
    {
        var result = _sales.CreateWalkinDraft(UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        _saleId = result.Value;
        Reload();
    }

    private void Report(SnookerPoint.Application.Common.OperationResult result, string? success = null)
    {
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Reload();
        if (success is not null)
        {
            Feedback.Success(success);
        }
    }

    private void Reload()
    {
        var draft = _sales.GetDraft(_saleId);
        Cart.Clear();
        if (draft is null)
        {
            return;
        }

        foreach (var line in draft.Lines)
        {
            Cart.Add(new CartLineRow(line.LineId, line.Name, line.UnitPrice, line.Quantity, line.LineTotal, line.OriginalUnitPrice is not null));
        }

        IsTableCheckout = draft.TableSessionId is not null;
        SaleLabel = draft.TableLabel ?? draft.Label;
        HasTableCharge = draft.TableCharge.IsPositive;
        HasDiscount = draft.Totals.Discount.IsPositive;
        SubtotalText = draft.Totals.Subtotal.Format();
        TableChargeText = draft.TableCharge.Format();
        DiscountText = "-" + draft.Totals.Discount.Format();
        TotalText = draft.Totals.Total.Format();
        IsEmpty = Cart.Count == 0 && !HasTableCharge;
    }

    private void RefreshSideLists()
    {
        Awaiting.Clear();
        foreach (var a in _sales.GetAwaitingCheckout())
        {
            Awaiting.Add(a);
        }

        Held.Clear();
        foreach (var h in _sales.GetHeldSales())
        {
            Held.Add(h);
        }
    }

    private bool Has(Permission permission) =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, permission);
}

/// <summary>A product tile in the POS search results.</summary>
public sealed record ProductPickRow(int Id, string Name, string Sku, string? Barcode, Money Price)
{
    public string PriceText => Price.Format();
    public string BarcodeText => Barcode ?? "—";
}

/// <summary>A cart line row.</summary>
public sealed record CartLineRow(int LineId, string Name, Money UnitPrice, decimal Quantity, Money LineTotal, bool IsOverridden)
{
    public string UnitPriceText => UnitPrice.Format();
    public string LineTotalText => LineTotal.Format();
    public string QuantityText => Quantity.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
}
