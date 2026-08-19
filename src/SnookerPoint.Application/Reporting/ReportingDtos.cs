using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Reporting;

/// <summary>
/// A half-open UTC time window [FromUtc, ToUtc). Reports display in local time but always
/// filter and store in UTC. Build preset ranges with <see cref="ReportRanges"/>.
/// </summary>
public sealed record ReportRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public bool Contains(DateTimeOffset utc) => utc >= FromUtc && utc < ToUtc;
}

/// <summary>Named preset for the reports date filter.</summary>
public enum ReportPreset
{
    Today,
    Yesterday,
    ThisWeek,
    ThisMonth,
    Custom,
}

/// <summary>Builds UTC ranges from local-day presets (weeks start Monday).</summary>
public static class ReportRanges
{
    /// <summary>Builds a range for the given preset relative to local "now".</summary>
    public static ReportRange For(ReportPreset preset, DateTimeOffset localNow, DateTime? customFromLocal = null, DateTime? customToLocal = null)
    {
        var today = localNow.LocalDateTime.Date;
        return preset switch
        {
            ReportPreset.Today => FromLocalDays(today, today.AddDays(1)),
            ReportPreset.Yesterday => FromLocalDays(today.AddDays(-1), today),
            ReportPreset.ThisWeek => FromLocalDays(StartOfWeek(today), StartOfWeek(today).AddDays(7)),
            ReportPreset.ThisMonth => FromLocalDays(new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1)),
            _ => FromLocalDays(
                (customFromLocal ?? today).Date,
                (customToLocal ?? today).Date.AddDays(1)),
        };
    }

    private static ReportRange FromLocalDays(DateTime fromLocalDay, DateTime toLocalDayExclusive) =>
        new(new DateTimeOffset(DateTime.SpecifyKind(fromLocalDay, DateTimeKind.Local)).ToUniversalTime(),
            new DateTimeOffset(DateTime.SpecifyKind(toLocalDayExclusive, DateTimeKind.Local)).ToUniversalTime());

    private static DateTime StartOfWeek(DateTime day)
    {
        int diff = (7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return day.AddDays(-diff);
    }
}

// ==================== Dashboard ====================

/// <summary>A per-method payment total for the dashboard and payment report.</summary>
public sealed record PaymentMethodTotal(
    string MethodName,
    PaymentMethodKind Kind,
    Money Total,
    int TransactionCount,
    Money CashReceived,
    Money ChangeGiven);

/// <summary>Operational dashboard summary for a date range. Empty ranges yield zeroes, not fabricated values.</summary>
public sealed record DashboardSummary(
    ReportRange Range,
    Money GrossSales,
    int CompletedSaleCount,
    Money TableRevenue,
    Money ProductRevenue,
    Money DiscountTotal,
    Money AverageSaleValue,
    IReadOnlyList<PaymentMethodTotal> PaymentTotals,
    int OpenShiftCount,
    int ClosedShiftCount,
    int AwaitingCheckoutCount,
    int LowStockCount)
{
    public bool HasSales => CompletedSaleCount > 0;
}

// ==================== Sales report ====================

public sealed record SalesReportFilter(
    ReportRange Range,
    int? CashierUserId = null,
    SaleType? Type = null,
    int? MethodId = null,
    int? TableId = null,
    SaleStatus? Status = null);

public sealed record SalesReportRow(
    int SaleNumber,
    DateTimeOffset CompletedUtc,
    SaleType Type,
    int? SessionNumber,
    string Cashier,
    Money Gross,
    Money Discount,
    Money Final,
    string PaymentBreakdown,
    SaleStatus Status);

public sealed record SalesReport(
    IReadOnlyList<SalesReportRow> Rows,
    int Count,
    Money Gross,
    Money Discount,
    Money Final,
    Money AverageSale);

// ==================== Payment report ====================

public sealed record PaymentReportRow(
    string MethodName,
    PaymentMethodKind Kind,
    int TransactionCount,
    Money TotalApplied,
    Money CashReceived,
    Money ChangeGiven);

public sealed record PaymentReport(
    IReadOnlyList<PaymentReportRow> Methods,
    int SplitPaymentSaleCount,
    Money TotalApplied,
    Money ExpectedPhysicalCash);

// ==================== Table report ====================

public sealed record TableReportRow(
    int SessionNumber,
    string Tables,
    BillingType Billing,
    DateTimeOffset StartUtc,
    DateTimeOffset? FinishUtc,
    long ActiveSeconds,
    long PausedSeconds,
    Money Charge,
    CheckoutStatus Checkout,
    int? SaleNumber,
    string StartedBy,
    string FinishedBy);

public sealed record TableAggregate(int TableId, string TableName, Money Revenue, double UsageHours, int SessionCount);

public sealed record TableReport(
    IReadOnlyList<TableReportRow> Rows,
    IReadOnlyList<TableAggregate> ByTable,
    double AverageSessionMinutes,
    Money HourlyTotal,
    Money FixedTotal,
    int HourlyCount,
    int FixedCount,
    Money AwaitingCheckoutTotal,
    int AwaitingCheckoutCount);

// ==================== Product sales report ====================

public sealed record ProductSalesRow(
    string Name,
    string Sku,
    string? Barcode,
    string Category,
    decimal QuantitySold,
    Money GrossRevenue,
    Money Discount,
    Money? UnitCost,
    Money? EstimatedProfit,
    bool CostAvailable);

public sealed record ProductSalesReport(
    IReadOnlyList<ProductSalesRow> Rows,
    decimal TotalQuantity,
    Money TotalRevenue,
    Money? TotalProfit,
    bool ProfitComplete);

// ==================== Inventory report ====================

public sealed record InventoryStockRow(
    int ProductId,
    string Name,
    string Sku,
    string? Barcode,
    string Category,
    decimal CurrentStock,
    decimal ReorderLevel,
    bool Tracked,
    bool IsLow,
    bool IsOut,
    Money? UnitCost,
    Money StockValue);

public sealed record InventorySummary(
    IReadOnlyList<InventoryStockRow> Stock,
    int LowCount,
    int OutCount,
    Money TotalStockValue);

public sealed record StockMovementReportFilter(
    ReportRange Range,
    int? ProductId = null,
    int? CategoryId = null,
    StockMovementType? Type = null,
    int? UserId = null);

public sealed record StockMovementReportRow(
    DateTimeOffset Utc,
    string Product,
    string Sku,
    StockMovementType Type,
    decimal QuantityDelta,
    decimal NewQuantity,
    string? Reason,
    string User);

// ==================== Shift report ====================

public sealed record ShiftReportRow(
    int ShiftId,
    string User,
    DateTimeOffset OpenedUtc,
    DateTimeOffset? ClosedUtc,
    Money OpeningCash,
    Money CashSales,
    Money ElectronicSales,
    Money CashIn,
    Money CashOut,
    Money Expenses,
    Money Drops,
    Money ExpectedCash,
    Money? CountedCash,
    Money? Variance,
    int SaleCount,
    string? Notes,
    ShiftStatus Status,
    IReadOnlyList<PaymentMethodTotal> ElectronicByMethod);

// ==================== Booking report ====================

public sealed record BookingReportRow(
    int Id,
    string Customer,
    string? Phone,
    string Table,
    DateTimeOffset StartUtc,
    int DurationMinutes,
    BookingStatus Status,
    int? LinkedSessionNumber,
    string CreatedBy,
    string? Reason);

public sealed record BookingTableCount(int TableId, string TableName, int Count);

public sealed record BookingReport(
    IReadOnlyList<BookingReportRow> Rows,
    int Scheduled,
    int CheckedIn,
    int Started,
    int Completed,
    int Cancelled,
    int NoShow,
    IReadOnlyList<BookingTableCount> ByTable);
