using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Reporting;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Staff;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>A generic tabular report: column headers, string rows and summary lines. Exports 1:1 to CSV.</summary>
public sealed class ReportTable
{
    public ReportTable(string title, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, IReadOnlyList<string> summary)
    {
        Title = title;
        Columns = columns;
        Rows = rows;
        Summary = summary;
    }

    public string Title { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<string[]> Rows { get; }
    public IReadOnlyList<string> Summary { get; }
    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>The report sections available on the Reports screen.</summary>
public enum ReportSection { Dashboard, Sales, Payments, Tables, Products, Inventory, Shifts, Bookings }

/// <summary>
/// The Reports screen: an operational dashboard and sales/payment/table/product/inventory/
/// shift/booking reports over a chosen local date range, each exportable to CSV. The Sales
/// report exposes the full set of filters (cashier, sale type, payment method, table, status);
/// the Inventory report offers a current-stock summary and a filterable stock-movement history.
/// Revenue and totals come from completed data only; exported CSV reflects exactly the current
/// filters. Viewing needs ViewReports; exporting needs ExportReports; profit needs
/// ViewProfitReports; shift reports need ViewShiftReports.
/// </summary>
public partial class ReportsViewModel : ObservableObject
{
    private readonly IReportingService _reports;
    private readonly ICsvExportService _csv;
    private readonly IStaffManagementService _staff;
    private readonly IPaymentMethodService _paymentMethods;
    private readonly ITableManagementService _tables;
    private readonly IProductService _products;
    private readonly ICategoryService _categories;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IClock _clock;

    public ReportsViewModel(
        IReportingService reports,
        ICsvExportService csv,
        IStaffManagementService staff,
        IPaymentMethodService paymentMethods,
        ITableManagementService tables,
        IProductService products,
        ICategoryService categories,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme,
        IClock clock)
    {
        _reports = reports;
        _csv = csv;
        _staff = staff;
        _paymentMethods = paymentMethods;
        _tables = tables;
        _products = products;
        _categories = categories;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _clock = clock;

        Presets = new[]
        {
            new PresetChoice(ReportPreset.Today, "Today"),
            new PresetChoice(ReportPreset.Yesterday, "Yesterday"),
            new PresetChoice(ReportPreset.ThisWeek, "This week"),
            new PresetChoice(ReportPreset.ThisMonth, "This month"),
            new PresetChoice(ReportPreset.Custom, "Custom range"),
        };
        _selectedPreset = Presets[0];
        _customFrom = _clock.UtcNow.ToLocalTime().Date;
        _customTo = _clock.UtcNow.ToLocalTime().Date;

        SaleTypes = new[]
        {
            new SaleTypeChoice(null, "All types"),
            new SaleTypeChoice(SaleType.Walkin, "Walk-in"),
            new SaleTypeChoice(SaleType.Table, "Table"),
        };
        _selectedSaleType = SaleTypes[0];

        SaleStatuses = new[]
        {
            new SaleStatusChoice(null, "Completed only"),
            new SaleStatusChoice(SaleStatus.Completed, "Completed"),
            new SaleStatusChoice(SaleStatus.Cancelled, "Cancelled"),
        };
        _selectedSaleStatus = SaleStatuses[0];

        MovementTypes = BuildMovementTypeChoices();
        _selectedMovementType = MovementTypes[0];

        LoadLookups();

        _selectedCashier = Cashiers[0];
        _selectedMethod = Methods[0];
        _selectedSalesTable = SalesTables[0];
        _selectedProduct = Products[0];
        _selectedCategory = Categories[0];
        _selectedUser = Users[0];

        Reload();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public IReadOnlyList<PresetChoice> Presets { get; }
    public IReadOnlyList<SaleTypeChoice> SaleTypes { get; }
    public IReadOnlyList<SaleStatusChoice> SaleStatuses { get; }
    public IReadOnlyList<MovementTypeChoice> MovementTypes { get; }
    public ObservableCollection<NamedChoice> Cashiers { get; } = new();
    public ObservableCollection<NamedChoice> Methods { get; } = new();
    public ObservableCollection<NamedChoice> SalesTables { get; } = new();
    public ObservableCollection<NamedChoice> Products { get; } = new();
    public ObservableCollection<NamedChoice> Categories { get; } = new();
    public ObservableCollection<NamedChoice> Users { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    public bool CanExport => Has(Permission.ExportReports);
    public bool CanViewProfit => Has(Permission.ViewProfitReports);
    public bool CanViewShiftReports => Has(Permission.ViewShiftReports);

    [ObservableProperty] private PresetChoice _selectedPreset;
    [ObservableProperty] private DateTime _customFrom;
    [ObservableProperty] private DateTime _customTo;
    [ObservableProperty] private ReportSection _section = ReportSection.Dashboard;
    [ObservableProperty] private ReportTable? _table;
    [ObservableProperty] private string _rangeText = string.Empty;

    // Sales filters
    [ObservableProperty] private NamedChoice _selectedCashier;
    [ObservableProperty] private SaleTypeChoice _selectedSaleType;
    [ObservableProperty] private NamedChoice _selectedMethod;
    [ObservableProperty] private NamedChoice _selectedSalesTable;
    [ObservableProperty] private SaleStatusChoice _selectedSaleStatus;

    // Inventory movement filters
    [ObservableProperty] private bool _showMovements;
    [ObservableProperty] private NamedChoice _selectedProduct;
    [ObservableProperty] private NamedChoice _selectedCategory;
    [ObservableProperty] private MovementTypeChoice _selectedMovementType;
    [ObservableProperty] private NamedChoice _selectedUser;

    public bool IsCustom => SelectedPreset?.Value == ReportPreset.Custom;
    public bool IsSalesSection => Section == ReportSection.Sales;
    public bool IsInventorySection => Section == ReportSection.Inventory;
    public bool IsInventoryMovements => IsInventorySection && ShowMovements;

    partial void OnSelectedPresetChanged(PresetChoice value)
    {
        OnPropertyChanged(nameof(IsCustom));
        Reload();
    }

    partial void OnCustomFromChanged(DateTime value) { if (IsCustom) { Reload(); } }
    partial void OnCustomToChanged(DateTime value) { if (IsCustom) { Reload(); } }

    partial void OnSectionChanged(ReportSection value)
    {
        OnPropertyChanged(nameof(IsSalesSection));
        OnPropertyChanged(nameof(IsInventorySection));
        OnPropertyChanged(nameof(IsInventoryMovements));
    }

    partial void OnSelectedCashierChanged(NamedChoice value) => ReloadIf(IsSalesSection);
    partial void OnSelectedSaleTypeChanged(SaleTypeChoice value) => ReloadIf(IsSalesSection);
    partial void OnSelectedMethodChanged(NamedChoice value) => ReloadIf(IsSalesSection);
    partial void OnSelectedSalesTableChanged(NamedChoice value) => ReloadIf(IsSalesSection);
    partial void OnSelectedSaleStatusChanged(SaleStatusChoice value) => ReloadIf(IsSalesSection);

    partial void OnShowMovementsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInventoryMovements));
        ReloadIf(IsInventorySection);
    }

    partial void OnSelectedProductChanged(NamedChoice value) => ReloadIf(IsInventoryMovements);
    partial void OnSelectedCategoryChanged(NamedChoice value) => ReloadIf(IsInventoryMovements);
    partial void OnSelectedMovementTypeChanged(MovementTypeChoice value) => ReloadIf(IsInventoryMovements);
    partial void OnSelectedUserChanged(NamedChoice value) => ReloadIf(IsInventoryMovements);

    private int UserId => _session.CurrentUser!.Id;

    private ReportRange CurrentRange => ReportRanges.For(
        SelectedPreset?.Value ?? ReportPreset.Today, _clock.UtcNow.ToLocalTime(), CustomFrom, CustomTo);

    // ---------- Section selection ----------

    [RelayCommand]
    private void Show(string section)
    {
        if (Enum.TryParse<ReportSection>(section, out var parsed))
        {
            Section = parsed;
            Reload();
        }
    }

    private void ReloadIf(bool condition)
    {
        if (condition)
        {
            Reload();
        }
    }

    [RelayCommand]
    private void Reload()
    {
        Feedback.Clear();
        var range = CurrentRange;
        RangeText = $"{range.FromUtc.ToLocalTime():dd MMM yyyy} – {range.ToUtc.ToLocalTime().AddSeconds(-1):dd MMM yyyy}";

        Table = Section switch
        {
            ReportSection.Dashboard => BuildDashboard(range),
            ReportSection.Sales => BuildSales(range),
            ReportSection.Payments => BuildPayments(range),
            ReportSection.Tables => BuildTables(range),
            ReportSection.Products => BuildProducts(range),
            ReportSection.Inventory => ShowMovements ? BuildStockMovements(range) : BuildInventory(),
            ReportSection.Shifts => BuildShifts(range),
            ReportSection.Bookings => BuildBookings(range),
            _ => null,
        };
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedCashier = Cashiers[0];
        SelectedSaleType = SaleTypes[0];
        SelectedMethod = Methods[0];
        SelectedSalesTable = SalesTables[0];
        SelectedSaleStatus = SaleStatuses[0];

        ShowMovements = false;
        SelectedProduct = Products[0];
        SelectedCategory = Categories[0];
        SelectedMovementType = MovementTypes[0];
        SelectedUser = Users[0];

        CustomFrom = _clock.UtcNow.ToLocalTime().Date;
        CustomTo = _clock.UtcNow.ToLocalTime().Date;
        SelectedPreset = Presets[0]; // Today; triggers a reload
    }

    [RelayCommand]
    private void Export()
    {
        Feedback.Clear();
        if (!CanExport)
        {
            Feedback.Error("You do not have permission to export reports.");
            return;
        }

        if (Table is null || Table.IsEmpty)
        {
            Feedback.Warning("There is nothing to export for this report and the current filters.");
            return;
        }

        var doc = new CsvDocument(Table.Title, Table.Columns, Table.Rows.Select(r => (IReadOnlyList<string>)r).ToList());
        var result = _csv.Export(doc, null, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        LastExportPath = result.Value;
        Feedback.Success($"Exported to {result.Value}");
    }

    [RelayCommand]
    private void OpenExportsFolder() => _dialogs.OpenPath(_csv.DefaultExportsFolder);

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    public string? LastExportPath { get; private set; }

    // ---------- Builders ----------

    private ReportTable BuildDashboard(ReportRange range)
    {
        var d = _reports.GetDashboard(range);
        var rows = new List<string[]>
        {
            new[] { "Gross sales", d.GrossSales.Format() },
            new[] { "Completed sales", d.CompletedSaleCount.ToString(CultureInfo.CurrentCulture) },
            new[] { "Average sale", d.AverageSaleValue.Format() },
            new[] { "Table revenue", d.TableRevenue.Format() },
            new[] { "Product revenue", d.ProductRevenue.Format() },
            new[] { "Discounts", d.DiscountTotal.Format() },
        };
        foreach (var m in d.PaymentTotals)
        {
            rows.Add(new[] { $"{m.MethodName} payments", m.Total.Format() });
        }

        rows.Add(new[] { "Open shifts", d.OpenShiftCount.ToString(CultureInfo.CurrentCulture) });
        rows.Add(new[] { "Closed shifts (in range)", d.ClosedShiftCount.ToString(CultureInfo.CurrentCulture) });
        rows.Add(new[] { "Awaiting checkout", d.AwaitingCheckoutCount.ToString(CultureInfo.CurrentCulture) });
        rows.Add(new[] { "Low-stock products", d.LowStockCount.ToString(CultureInfo.CurrentCulture) });

        var summary = d.HasSales
            ? new[] { $"{d.CompletedSaleCount} sale(s), {d.GrossSales.Format()} gross." }
            : new[] { "No completed sales in this date range yet." };
        return new ReportTable("Dashboard", new[] { "Metric", "Value" }, rows, summary);
    }

    private ReportTable BuildSales(ReportRange range)
    {
        var filter = new SalesReportFilter(
            range,
            SelectedCashier?.Id,
            SelectedSaleType?.Value,
            SelectedMethod?.Id,
            SelectedSalesTable?.Id,
            SelectedSaleStatus?.Value);

        var report = _reports.GetSalesReport(filter);
        var rows = report.Rows.Select(r => new[]
        {
            r.SaleNumber.ToString(CultureInfo.CurrentCulture),
            r.CompletedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture),
            r.Type == SaleType.Table ? (r.SessionNumber is { } n ? $"Table #{n}" : "Table") : "Walk-in",
            r.Cashier,
            r.Gross.Format(),
            r.Discount.Format(),
            r.Final.Format(),
            r.PaymentBreakdown,
            r.Status.ToString(),
        }).ToList();

        var summary = new[]
        {
            $"{report.Count} sale(s) matching the current filters.",
            $"Gross {report.Gross.Format()}, discounts {report.Discount.Format()}, net {report.Final.Format()}.",
            $"Average sale {report.AverageSale.Format()}.",
        };
        return new ReportTable("Sales", new[] { "Sale #", "Date/time", "Type", "Cashier", "Gross", "Discount", "Final", "Payment", "Status" }, rows, summary);
    }

    private ReportTable BuildPayments(ReportRange range)
    {
        var report = _reports.GetPaymentReport(range);
        var rows = report.Methods.Select(m => new[]
        {
            m.MethodName,
            m.Kind.ToString(),
            m.TransactionCount.ToString(CultureInfo.CurrentCulture),
            m.TotalApplied.Format(),
            m.CashReceived.Format(),
            m.ChangeGiven.Format(),
        }).ToList();

        var summary = new[]
        {
            $"Total applied {report.TotalApplied.Format()} across {report.Methods.Sum(m => m.TransactionCount)} portion(s).",
            $"Split-payment sales: {report.SplitPaymentSaleCount}.",
            $"Expected physical cash (cash portions only): {report.ExpectedPhysicalCash.Format()}.",
        };
        return new ReportTable("Payments", new[] { "Method", "Kind", "Transactions", "Total", "Cash received", "Change" }, rows, summary);
    }

    private ReportTable BuildTables(ReportRange range)
    {
        var report = _reports.GetTableReport(range);
        var rows = report.Rows.Select(r => new[]
        {
            r.SessionNumber.ToString(CultureInfo.CurrentCulture),
            r.Tables,
            r.Billing.ToString(),
            r.StartUtc.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture),
            r.FinishUtc?.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture) ?? "—",
            Duration(r.ActiveSeconds),
            Duration(r.PausedSeconds),
            r.Charge.Format(),
            r.Checkout.ToString(),
            r.SaleNumber?.ToString(CultureInfo.CurrentCulture) ?? "—",
            r.StartedBy,
            r.FinishedBy,
        }).ToList();

        var summary = new List<string>
        {
            $"{report.Rows.Count} session(s); average {report.AverageSessionMinutes:0} min.",
            $"Hourly {report.HourlyCount} ({report.HourlyTotal.Format()}), Fixed {report.FixedCount} ({report.FixedTotal.Format()}).",
            $"Awaiting checkout: {report.AwaitingCheckoutCount} ({report.AwaitingCheckoutTotal.Format()}).",
        };
        summary.AddRange(report.ByTable.Select(t => $"{t.TableName}: {t.Revenue.Format()}, {t.UsageHours:0.0} h, {t.SessionCount} session(s)."));

        return new ReportTable("Tables",
            new[] { "Session #", "Table(s)", "Billing", "Start", "Finish", "Active", "Paused", "Charge", "Checkout", "Sale #", "Started by", "Finished by" },
            rows, summary);
    }

    private ReportTable BuildProducts(ReportRange range)
    {
        var report = _reports.GetProductSalesReport(range);
        var columns = new List<string> { "Product", "SKU", "Barcode", "Category", "Qty", "Revenue", "Discount", "Unit cost" };
        if (CanViewProfit)
        {
            columns.Add("Est. profit");
        }

        var rows = report.Rows.Select(r =>
        {
            var cells = new List<string>
            {
                r.Name, r.Sku, r.Barcode ?? "—", r.Category,
                r.QuantitySold.ToString("0.###", CultureInfo.CurrentCulture),
                r.GrossRevenue.Format(), r.Discount.Format(),
                r.UnitCost?.Format() ?? "—",
            };
            if (CanViewProfit)
            {
                cells.Add(r.CostAvailable ? r.EstimatedProfit!.Value.Format() : "Cost not recorded");
            }

            return cells.ToArray();
        }).ToList();

        var summary = new List<string> { $"{report.Rows.Count} product(s), {report.TotalQuantity:0.###} sold, revenue {report.TotalRevenue.Format()}." };
        if (CanViewProfit)
        {
            summary.Add(report.ProfitComplete
                ? $"Estimated gross profit {report.TotalProfit!.Value.Format()}."
                : "Estimated gross profit unavailable — some sold products had no recorded cost.");
        }

        return new ReportTable("Product sales", columns, rows, summary);
    }

    private ReportTable BuildInventory()
    {
        var report = _reports.GetInventorySummary();
        var rows = report.Stock.Select(r => new[]
        {
            r.Name, r.Sku, r.Barcode ?? "—", r.Category,
            r.Tracked ? r.CurrentStock.ToString("0.###", CultureInfo.CurrentCulture) : "Not tracked",
            r.ReorderLevel.ToString("0.###", CultureInfo.CurrentCulture),
            r.IsOut ? "Out of stock" : r.IsLow ? "Low" : "OK",
            r.StockValue.Format(),
        }).ToList();

        var summary = new[]
        {
            $"{report.Stock.Count} active product(s); {report.LowCount} low, {report.OutCount} out of stock.",
            $"Total stock value (at cost): {report.TotalStockValue.Format()}.",
        };
        return new ReportTable("Inventory", new[] { "Product", "SKU", "Barcode", "Category", "Stock", "Reorder", "Status", "Value" }, rows, summary);
    }

    private ReportTable BuildStockMovements(ReportRange range)
    {
        var filter = new StockMovementReportFilter(
            range,
            SelectedProduct?.Id,
            SelectedCategory?.Id,
            SelectedMovementType?.Value,
            SelectedUser?.Id);

        var movements = _reports.GetStockMovements(filter);
        var rows = movements.Select(m => new[]
        {
            m.Utc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture),
            m.Product,
            m.Sku,
            m.Type.ToString(),
            m.QuantityDelta.ToString("0.###", CultureInfo.CurrentCulture),
            m.NewQuantity.ToString("0.###", CultureInfo.CurrentCulture),
            m.Reason ?? "—",
            m.User,
        }).ToList();

        var summary = new[] { $"{movements.Count} stock movement(s) matching the current filters." };
        return new ReportTable("Stock movements",
            new[] { "Date/time", "Product", "SKU", "Type", "Change", "New stock", "Reason", "User" }, rows, summary);
    }

    private ReportTable BuildShifts(ReportRange range)
    {
        if (!CanViewShiftReports)
        {
            return new ReportTable("Shifts", new[] { "Shift" }, Array.Empty<string[]>(),
                new[] { "You do not have permission to view shift reports." });
        }

        var report = _reports.GetShiftReport(range);
        var rows = report.Select(r => new[]
        {
            r.ShiftId.ToString(CultureInfo.CurrentCulture),
            r.User,
            r.OpenedUtc.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture),
            r.ClosedUtc?.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture) ?? "—",
            r.OpeningCash.Format(),
            r.CashSales.Format(),
            r.ElectronicSales.Format(),
            r.CashIn.Format(),
            r.CashOut.Format(),
            r.Expenses.Format(),
            r.Drops.Format(),
            r.ExpectedCash.Format(),
            r.CountedCash?.Format() ?? "—",
            r.Variance?.Format() ?? "—",
            r.SaleCount.ToString(CultureInfo.CurrentCulture),
            r.Status.ToString(),
        }).ToList();

        var summary = new[] { $"{report.Count} shift(s) in this range." };
        return new ReportTable("Shifts",
            new[] { "Shift", "User", "Opened", "Closed", "Opening", "Cash sales", "Electronic", "Cash in", "Cash out", "Expenses", "Drops", "Expected", "Counted", "Variance", "Sales", "Status" },
            rows, summary);
    }

    private ReportTable BuildBookings(ReportRange range)
    {
        var report = _reports.GetBookingReport(range);
        var rows = report.Rows.Select(r => new[]
        {
            r.Id.ToString(CultureInfo.CurrentCulture),
            r.Customer,
            r.Phone ?? "—",
            r.Table,
            r.StartUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture),
            $"{r.DurationMinutes} min",
            r.Status.ToString(),
            r.LinkedSessionNumber?.ToString(CultureInfo.CurrentCulture) ?? "—",
            r.CreatedBy,
            r.Reason ?? "—",
        }).ToList();

        var summary = new List<string>
        {
            $"Scheduled {report.Scheduled}, checked in {report.CheckedIn}, started {report.Started}, completed {report.Completed}, cancelled {report.Cancelled}, no-shows {report.NoShow}.",
        };
        summary.AddRange(report.ByTable.Select(t => $"{t.TableName}: {t.Count} booking(s)."));

        return new ReportTable("Bookings",
            new[] { "Booking #", "Customer", "Phone", "Table", "When", "Duration", "Status", "Session #", "Created by", "Reason" },
            rows, summary);
    }

    // ---------- Lookups ----------

    private void LoadLookups()
    {
        Cashiers.Clear();
        Cashiers.Add(new NamedChoice(null, "All cashiers"));
        foreach (var s in _staff.GetAll())
        {
            Cashiers.Add(new NamedChoice(s.Id, s.DisplayName));
        }

        Users.Clear();
        Users.Add(new NamedChoice(null, "All users"));
        foreach (var s in _staff.GetAll())
        {
            Users.Add(new NamedChoice(s.Id, s.DisplayName));
        }

        Methods.Clear();
        Methods.Add(new NamedChoice(null, "All methods"));
        foreach (var m in _paymentMethods.GetAll())
        {
            Methods.Add(new NamedChoice(m.Id, m.Name));
        }

        SalesTables.Clear();
        SalesTables.Add(new NamedChoice(null, "All tables"));
        foreach (var t in _tables.GetAll())
        {
            SalesTables.Add(new NamedChoice(t.Id, t.Name));
        }

        Products.Clear();
        Products.Add(new NamedChoice(null, "All products"));
        foreach (var p in _products.GetList(new ProductFilter(Active: ProductActiveFilter.All)))
        {
            Products.Add(new NamedChoice(p.Id, p.Name));
        }

        Categories.Clear();
        Categories.Add(new NamedChoice(null, "All categories"));
        foreach (var c in _categories.GetAll())
        {
            Categories.Add(new NamedChoice(c.Id, c.Name));
        }
    }

    private static IReadOnlyList<MovementTypeChoice> BuildMovementTypeChoices()
    {
        var list = new List<MovementTypeChoice> { new(null, "All movement types") };
        list.AddRange(Enum.GetValues<StockMovementType>().Select(t => new MovementTypeChoice(t, Humanise(t))));
        return list;
    }

    private static string Humanise(StockMovementType type) => type switch
    {
        StockMovementType.OpeningStock => "Opening stock",
        StockMovementType.StockIn => "Stock in",
        StockMovementType.PositiveAdjustment => "Positive adjustment",
        StockMovementType.NegativeAdjustment => "Negative adjustment",
        StockMovementType.SupplierReturn => "Supplier return",
        _ => type.ToString(),
    };

    private static string Duration(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }

    private bool Has(Permission p) => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, p);
}

/// <summary>A date-preset choice for the reports filter.</summary>
public sealed record PresetChoice(ReportPreset Value, string Label);

/// <summary>A generic id/name filter choice (null id = "all").</summary>
public sealed record NamedChoice(int? Id, string Name);

/// <summary>A sale-type filter choice.</summary>
public sealed record SaleTypeChoice(SaleType? Value, string Label);

/// <summary>A sale-status filter choice (null = completed only).</summary>
public sealed record SaleStatusChoice(SaleStatus? Value, string Label);

/// <summary>A stock-movement-type filter choice (null = all).</summary>
public sealed record MovementTypeChoice(StockMovementType? Value, string Label);
