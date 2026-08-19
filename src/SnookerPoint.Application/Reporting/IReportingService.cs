namespace SnookerPoint.Application.Reporting;

/// <summary>
/// Read-only operational reporting over completed data. Revenue is always taken from
/// completed sales only (held drafts never count), split payments are attributed to each
/// portion's actual method, electronic payments never count toward physical cash, and table
/// transfers count a session once. Historical product figures use the sale-line snapshots so
/// changing a product's current price or cost never alters history. All times are UTC.
/// </summary>
public interface IReportingService
{
    DashboardSummary GetDashboard(ReportRange range);

    SalesReport GetSalesReport(SalesReportFilter filter);

    PaymentReport GetPaymentReport(ReportRange range);

    TableReport GetTableReport(ReportRange range);

    ProductSalesReport GetProductSalesReport(ReportRange range);

    InventorySummary GetInventorySummary();

    IReadOnlyList<StockMovementReportRow> GetStockMovements(StockMovementReportFilter filter);

    IReadOnlyList<ShiftReportRow> GetShiftReport(ReportRange range);

    BookingReport GetBookingReport(ReportRange range);
}
