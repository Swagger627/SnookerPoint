using Microsoft.EntityFrameworkCore;
using SnookerPoint.Application.Reporting;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Read-only operational reporting. Revenue comes from completed sales only; split payments
/// are attributed per portion; electronic payments never count toward physical cash; table
/// transfers count a session once; product/profit figures use the immutable sale-line
/// snapshots. SQLite cannot aggregate Money or compare DateTimeOffset, so rows are pulled to
/// memory and aggregated client-side.
/// </summary>
public sealed class ReportingService : IReportingService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;

    public ReportingService(IDbContextFactory<SnookerPointDbContext> factory)
    {
        _factory = factory;
    }

    // ==================== Dashboard ====================

    public DashboardSummary GetDashboard(ReportRange range)
    {
        using var db = _factory.CreateDbContext();
        var sales = CompletedSalesInRange(db, range);
        var saleIds = sales.Select(s => s.Id).ToHashSet();
        var payments = db.SalePayments.AsNoTracking().ToList().Where(p => saleIds.Contains(p.SaleId)).ToList();

        var gross = Sum(sales.Select(s => s.Total));
        var count = sales.Count;
        var openShifts = db.Shifts.AsNoTracking().Count(s => s.Status == ShiftStatus.Open);
        var closedShifts = db.Shifts.AsNoTracking().Where(s => s.Status == ShiftStatus.Closed).ToList()
            .Count(s => s.ClosedUtc is { } c && range.Contains(c));
        var awaiting = db.TableSessions.AsNoTracking()
            .Count(s => s.Status == SessionStatus.Completed && s.CheckoutStatus == CheckoutStatus.AwaitingCheckout);

        return new DashboardSummary(
            range,
            gross,
            count,
            Sum(sales.Select(s => s.TableCharge)),
            Sum(sales.Select(s => s.Subtotal)),
            Sum(sales.Select(s => s.DiscountAmount)),
            count == 0 ? Money.Zero : Money.FromPaisa(gross.Paisa / count),
            MethodTotals(payments),
            openShifts,
            closedShifts,
            awaiting,
            CountLowStock(db));
    }

    // ==================== Sales report ====================

    public SalesReport GetSalesReport(SalesReportFilter filter)
    {
        using var db = _factory.CreateDbContext();

        // Default to completed only; an explicit status filter may widen this.
        var wanted = filter.Status;
        var query = db.Sales.AsNoTracking()
            .Where(s => wanted == null ? s.Status == SaleStatus.Completed : s.Status == wanted);
        if (filter.CashierUserId is { } cashier)
        {
            query = query.Where(s => s.CompletedByUserId == cashier);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(s => s.Type == type);
        }

        var sales = query.ToList().Where(s => InRange(range: filter.Range, s.CompletedUtc ?? s.CreatedUtc)).ToList();

        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var sessionNumbers = db.TableSessions.AsNoTracking().ToDictionary(s => s.Id, s => s.SessionNumber);
        var sessionTable = db.TableSessions.AsNoTracking().ToDictionary(s => s.Id, s => s.CurrentTableId);
        var saleIds = sales.Select(s => s.Id).ToHashSet();
        var payments = db.SalePayments.AsNoTracking().ToList().Where(p => saleIds.Contains(p.SaleId))
            .GroupBy(p => p.SaleId).ToDictionary(g => g.Key, g => g.ToList());

        if (filter.MethodId is { } mid)
        {
            sales = sales.Where(s => payments.TryGetValue(s.Id, out var ps) && ps.Any(p => p.MethodId == mid)).ToList();
        }

        if (filter.TableId is { } tid)
        {
            sales = sales.Where(s => s.TableSessionId is { } sid && sessionTable.GetValueOrDefault(sid) == tid).ToList();
        }

        var rows = sales
            .OrderByDescending(s => s.SaleNumber)
            .Select(s => new SalesReportRow(
                s.SaleNumber ?? 0,
                s.CompletedUtc ?? s.CreatedUtc,
                s.Type,
                s.TableSessionId is { } sid ? sessionNumbers.GetValueOrDefault(sid) : null,
                s.CompletedByUserId is { } cid ? users.GetValueOrDefault(cid, "—") : "—",
                s.Subtotal + s.TableCharge,
                s.DiscountAmount,
                s.Total,
                PaymentBreakdown(payments.GetValueOrDefault(s.Id)),
                s.Status))
            .ToList();

        var gross = Sum(rows.Select(r => r.Gross));
        var final = Sum(rows.Select(r => r.Final));
        return new SalesReport(rows, rows.Count, gross, Sum(rows.Select(r => r.Discount)), final,
            rows.Count == 0 ? Money.Zero : Money.FromPaisa(final.Paisa / rows.Count));
    }

    // ==================== Payment report ====================

    public PaymentReport GetPaymentReport(ReportRange range)
    {
        using var db = _factory.CreateDbContext();
        var sales = CompletedSalesInRange(db, range);
        var saleIds = sales.Select(s => s.Id).ToHashSet();
        var payments = db.SalePayments.AsNoTracking().ToList().Where(p => saleIds.Contains(p.SaleId)).ToList();

        var methods = MethodTotals(payments)
            .Select(m => new PaymentReportRow(m.MethodName, m.Kind, m.TransactionCount, m.Total, m.CashReceived, m.ChangeGiven))
            .ToList();

        // A split-payment sale is one with more than one payment portion.
        var splitCount = payments.GroupBy(p => p.SaleId).Count(g => g.Count() > 1);

        // Expected physical cash counts only the cash portions (never electronic).
        var cash = Sum(payments.Where(p => p.Kind == PaymentMethodKind.Cash).Select(p => p.Amount));

        return new PaymentReport(methods, splitCount, Sum(payments.Select(p => p.Amount)), cash);
    }

    // ==================== Table report ====================

    public TableReport GetTableReport(ReportRange range)
    {
        using var db = _factory.CreateDbContext();
        var sessions = db.TableSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Completed)
            .ToList()
            .Where(s => s.FinishUtc is { } f && range.Contains(f))
            .ToList();

        var sessionIds = sessions.Select(s => s.Id).ToHashSet();
        var pauses = db.SessionPauses.AsNoTracking().ToList().Where(p => sessionIds.Contains(p.SessionId))
            .GroupBy(p => p.SessionId).ToDictionary(g => g.Key, g => g.ToList());
        var segments = db.SessionSegments.AsNoTracking().ToList().Where(s => sessionIds.Contains(s.SessionId))
            .GroupBy(s => s.SessionId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SegmentIndex).ToList());
        var tableNames = db.PoolTables.AsNoTracking().ToDictionary(t => t.Id, t => t.Name);
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var saleBySession = db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.TableSessionId != null)
            .ToList().Where(s => sessionIds.Contains(s.TableSessionId!.Value))
            .GroupBy(s => s.TableSessionId!.Value).ToDictionary(g => g.Key, g => g.First().SaleNumber);

        var rows = new List<TableReportRow>();
        foreach (var s in sessions.OrderByDescending(s => s.SessionNumber))
        {
            var paused = PausedSeconds(pauses.GetValueOrDefault(s.Id));
            var total = s.FinishUtc is { } f ? (long)(f - s.StartUtc).TotalSeconds : 0;
            var active = Math.Max(0, total - paused);
            var names = segments.TryGetValue(s.Id, out var segs) && segs.Count > 0
                ? string.Join(" → ", segs.Select(x => tableNames.GetValueOrDefault(x.TableId, "—")).Distinct())
                : tableNames.GetValueOrDefault(s.CurrentTableId, "—");

            rows.Add(new TableReportRow(
                s.SessionNumber, names, s.BillingType, s.StartUtc, s.FinishUtc, active, paused,
                s.FinalCharge ?? Money.Zero, s.CheckoutStatus,
                saleBySession.GetValueOrDefault(s.Id),
                users.GetValueOrDefault(s.OpenedByUserId, "—"),
                s.FinishedByUserId is { } fid ? users.GetValueOrDefault(fid, "—") : "—"));
        }

        // Aggregate once per session (transfers never duplicate a session).
        var byTable = sessions
            .GroupBy(s => s.CurrentTableId)
            .Select(g => new TableAggregate(
                g.Key,
                tableNames.GetValueOrDefault(g.Key, "—"),
                Sum(g.Select(s => s.FinalCharge ?? Money.Zero)),
                g.Sum(s => Math.Max(0, ((s.FinishUtc is { } f ? (long)(f - s.StartUtc).TotalSeconds : 0) - PausedSeconds(pauses.GetValueOrDefault(s.Id)))) / 3600.0),
                g.Count()))
            .OrderByDescending(a => a.Revenue.Paisa)
            .ToList();

        var avgMinutes = rows.Count == 0 ? 0 : rows.Average(r => (r.ActiveSeconds + r.PausedSeconds) / 60.0);

        var awaiting = db.TableSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Completed && s.CheckoutStatus == CheckoutStatus.AwaitingCheckout)
            .ToList();

        return new TableReport(
            rows, byTable, avgMinutes,
            Sum(sessions.Where(s => s.BillingType == BillingType.Hourly).Select(s => s.FinalCharge ?? Money.Zero)),
            Sum(sessions.Where(s => s.BillingType == BillingType.Fixed).Select(s => s.FinalCharge ?? Money.Zero)),
            sessions.Count(s => s.BillingType == BillingType.Hourly),
            sessions.Count(s => s.BillingType == BillingType.Fixed),
            Sum(awaiting.Select(s => s.FinalCharge ?? Money.Zero)),
            awaiting.Count);
    }

    // ==================== Product sales report ====================

    public ProductSalesReport GetProductSalesReport(ReportRange range)
    {
        using var db = _factory.CreateDbContext();
        var sales = CompletedSalesInRange(db, range);
        var saleIds = sales.Select(s => s.Id).ToHashSet();
        var lines = db.SaleLines.AsNoTracking().ToList().Where(l => saleIds.Contains(l.SaleId)).ToList();

        var categoryByProduct = db.Products.AsNoTracking().ToDictionary(p => p.Id, p => p.CategoryId);
        var categoryNames = db.Categories.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);

        var rows = new List<ProductSalesRow>();
        foreach (var g in lines.GroupBy(l => l.SkuSnapshot))
        {
            var first = g.First();
            var qty = g.Sum(l => l.Quantity);
            var revenue = Sum(g.Select(l => l.LineTotal));
            var discount = Sum(g.Where(l => l.OriginalUnitPrice is { } o && o > l.UnitPrice)
                .Select(l => SnookerPoint.Domain.Sales.SaleMath.LineTotal(l.OriginalUnitPrice!.Value - l.UnitPrice, l.Quantity)));

            var costAvailable = g.All(l => l.CostSnapshot is not null);
            Money? unitCost = first.CostSnapshot;
            Money? profit = costAvailable
                ? Sum(g.Select(l => SnookerPoint.Domain.Sales.SaleMath.LineTotal(l.UnitPrice - l.CostSnapshot!.Value, l.Quantity)))
                : null;

            var category = first.ProductId is { } pid && categoryByProduct.TryGetValue(pid, out var cid)
                ? categoryNames.GetValueOrDefault(cid, "—")
                : "—";

            rows.Add(new ProductSalesRow(first.NameSnapshot, first.SkuSnapshot, first.BarcodeSnapshot, category,
                qty, revenue, discount, unitCost, profit, costAvailable));
        }

        rows = rows.OrderByDescending(r => r.GrossRevenue.Paisa).ToList();
        var profitComplete = rows.All(r => r.CostAvailable);
        Money? totalProfit = profitComplete
            ? Sum(rows.Select(r => r.EstimatedProfit ?? Money.Zero))
            : null;

        return new ProductSalesReport(rows, rows.Sum(r => r.QuantitySold), Sum(rows.Select(r => r.GrossRevenue)),
            totalProfit, profitComplete);
    }

    // ==================== Inventory ====================

    public InventorySummary GetInventorySummary()
    {
        using var db = _factory.CreateDbContext();
        var products = db.Products.AsNoTracking().Where(p => p.IsActive).ToList();
        var categoryNames = db.Categories.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);
        var currentStock = CurrentStockByProduct(db);

        var rows = new List<InventoryStockRow>();
        foreach (var p in products.OrderBy(p => p.Name))
        {
            var stock = currentStock.GetValueOrDefault(p.Id, 0m);
            var isOut = p.TrackInventory && stock <= 0m;
            var isLow = p.TrackInventory && stock > 0m && stock <= p.ReorderLevel;
            var value = p.Cost is { } c ? Money.FromPaisa((long)decimal.Round(c.Paisa * Math.Max(0, stock), 0, MidpointRounding.AwayFromZero)) : Money.Zero;
            rows.Add(new InventoryStockRow(p.Id, p.Name, p.Sku, p.Barcode, categoryNames.GetValueOrDefault(p.CategoryId, "—"),
                stock, p.ReorderLevel, p.TrackInventory, isLow, isOut, p.Cost, value));
        }

        return new InventorySummary(rows, rows.Count(r => r.IsLow), rows.Count(r => r.IsOut), Sum(rows.Select(r => r.StockValue)));
    }

    public IReadOnlyList<StockMovementReportRow> GetStockMovements(StockMovementReportFilter filter)
    {
        using var db = _factory.CreateDbContext();
        var products = db.Products.AsNoTracking().ToDictionary(p => p.Id, p => p);
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);

        var query = db.StockMovements.AsNoTracking().AsQueryable();
        if (filter.ProductId is { } pid)
        {
            query = query.Where(m => m.ProductId == pid);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(m => m.Type == type);
        }

        if (filter.UserId is { } uid)
        {
            query = query.Where(m => m.ActorUserId == uid);
        }

        var movements = query.ToList().Where(m => filter.Range.Contains(m.Utc)).ToList();
        if (filter.CategoryId is { } cat)
        {
            movements = movements.Where(m => products.TryGetValue(m.ProductId, out var p) && p.CategoryId == cat).ToList();
        }

        return movements
            .OrderByDescending(m => m.Id)
            .Select(m => new StockMovementReportRow(
                m.Utc,
                products.TryGetValue(m.ProductId, out var p) ? p.Name : "—",
                products.TryGetValue(m.ProductId, out var p2) ? p2.Sku : "—",
                m.Type, m.QuantityDelta, m.NewQuantity, m.Reason,
                users.GetValueOrDefault(m.ActorUserId, "—")))
            .ToList();
    }

    // ==================== Shift report ====================

    public IReadOnlyList<ShiftReportRow> GetShiftReport(ReportRange range)
    {
        using var db = _factory.CreateDbContext();
        var shifts = db.Shifts.AsNoTracking().ToList()
            .Where(s => range.Contains(s.OpenedUtc) || (s.ClosedUtc is { } c && range.Contains(c)))
            .ToList();

        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var shiftIds = shifts.Select(s => s.Id).ToHashSet();

        var salesByShift = db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.ShiftId != null)
            .ToList().Where(s => shiftIds.Contains(s.ShiftId!.Value))
            .GroupBy(s => s.ShiftId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var completedSaleIds = salesByShift.SelectMany(kv => kv.Value).Select(s => s.Id).ToHashSet();
        var paymentsBySale = db.SalePayments.AsNoTracking().ToList().Where(p => completedSaleIds.Contains(p.SaleId))
            .GroupBy(p => p.SaleId).ToDictionary(g => g.Key, g => g.ToList());
        var movementsByShift = db.CashMovements.AsNoTracking().ToList()
            .Where(m => shiftIds.Contains(m.ShiftId))
            .GroupBy(m => m.ShiftId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ShiftReportRow>();
        foreach (var shift in shifts.OrderByDescending(s => s.Id))
        {
            var sales = salesByShift.GetValueOrDefault(shift.Id) ?? new List<Sale>();
            var payments = sales.SelectMany(s => paymentsBySale.GetValueOrDefault(s.Id) ?? new List<SalePayment>()).ToList();
            var cashSales = Sum(payments.Where(p => p.Kind == PaymentMethodKind.Cash).Select(p => p.Amount));
            var electronic = Sum(payments.Where(p => p.Kind != PaymentMethodKind.Cash).Select(p => p.Amount));

            var movements = movementsByShift.GetValueOrDefault(shift.Id) ?? new List<CashMovement>();
            var cashIn = Sum(movements.Where(m => m.Type == CashMovementType.CashIn).Select(m => m.Amount));
            var cashOut = Sum(movements.Where(m => m.Type == CashMovementType.CashOut).Select(m => m.Amount));
            var expenses = Sum(movements.Where(m => m.Type == CashMovementType.Expense).Select(m => m.Amount));
            var drops = Sum(movements.Where(m => m.Type == CashMovementType.Drop).Select(m => m.Amount));

            var expected = shift.ExpectedCash ?? (shift.OpeningCash + cashSales + cashIn - cashOut - expenses - drops);

            rows.Add(new ShiftReportRow(
                shift.Id, users.GetValueOrDefault(shift.UserId, "—"), shift.OpenedUtc, shift.ClosedUtc,
                shift.OpeningCash, cashSales, electronic, cashIn, cashOut, expenses, drops, expected,
                shift.CountedCash, shift.Variance, sales.Count, shift.ClosingNote ?? shift.OpeningNote, shift.Status,
                MethodTotals(payments.Where(p => p.Kind != PaymentMethodKind.Cash).ToList())));
        }

        return rows;
    }

    // ==================== Booking report ====================

    public BookingReport GetBookingReport(ReportRange range)
    {
        using var db = _factory.CreateDbContext();
        var bookings = db.Bookings.AsNoTracking().ToList().Where(b => range.Contains(b.StartUtc)).ToList();
        var tableNames = db.PoolTables.AsNoTracking().ToDictionary(t => t.Id, t => t.Name);
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var sessionNumbers = db.TableSessions.AsNoTracking().ToDictionary(s => s.Id, s => s.SessionNumber);

        var rows = bookings
            .OrderByDescending(b => b.StartUtc)
            .Select(b => new BookingReportRow(
                b.Id, b.CustomerName, b.Phone, tableNames.GetValueOrDefault(b.TableId, "—"),
                b.StartUtc, b.DurationMinutes, b.Status,
                b.LinkedSessionId is { } sid ? sessionNumbers.GetValueOrDefault(sid) : null,
                users.GetValueOrDefault(b.CreatedByUserId, "—"),
                b.CancelReason))
            .ToList();

        var byTable = bookings
            .GroupBy(b => b.TableId)
            .Select(g => new BookingTableCount(g.Key, tableNames.GetValueOrDefault(g.Key, "—"), g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new BookingReport(
            rows,
            bookings.Count(b => b.Status == BookingStatus.Scheduled),
            bookings.Count(b => b.Status == BookingStatus.CheckedIn),
            bookings.Count(b => b.Status == BookingStatus.Started),
            bookings.Count(b => b.Status == BookingStatus.Completed),
            bookings.Count(b => b.Status == BookingStatus.Cancelled),
            bookings.Count(b => b.Status == BookingStatus.NoShow),
            byTable);
    }

    // ==================== Helpers ====================

    private static List<Sale> CompletedSalesInRange(SnookerPointDbContext db, ReportRange range) =>
        db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed).ToList()
            .Where(s => range.Contains(s.CompletedUtc ?? s.CreatedUtc)).ToList();

    private static bool InRange(ReportRange range, DateTimeOffset utc) => range.Contains(utc);

    private static long PausedSeconds(List<SessionPause>? pauses) =>
        pauses is null ? 0 : pauses.Where(p => p.ResumedUtc is not null)
            .Sum(p => (long)(p.ResumedUtc!.Value - p.PausedUtc).TotalSeconds);

    private static Dictionary<int, decimal> CurrentStockByProduct(SnookerPointDbContext db) =>
        db.StockMovements.AsNoTracking()
            .Select(m => new { m.ProductId, m.Id, m.NewQuantity })
            .ToList()
            .GroupBy(m => m.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Id).First().NewQuantity);

    private static int CountLowStock(SnookerPointDbContext db)
    {
        var stock = CurrentStockByProduct(db);
        return db.Products.AsNoTracking().Where(p => p.IsActive && p.TrackInventory).ToList()
            .Count(p => stock.GetValueOrDefault(p.Id, 0m) <= p.ReorderLevel);
    }

    private static IReadOnlyList<PaymentMethodTotal> MethodTotals(List<SalePayment> payments) =>
        payments
            .GroupBy(p => new { p.MethodNameSnapshot, p.Kind })
            .Select(g => new PaymentMethodTotal(
                g.Key.MethodNameSnapshot, g.Key.Kind,
                Sum(g.Select(p => p.Amount)),
                g.Count(),
                Sum(g.Select(p => p.ReceivedAmount ?? Money.Zero)),
                Sum(g.Select(p => p.ChangeAmount ?? Money.Zero))))
            .OrderByDescending(m => m.Total.Paisa)
            .ToList();

    private static string PaymentBreakdown(List<SalePayment>? payments)
    {
        if (payments is null || payments.Count == 0)
        {
            return "—";
        }

        return string.Join(", ", payments
            .GroupBy(p => p.MethodNameSnapshot)
            .Select(g => $"{g.Key} {Sum(g.Select(p => p.Amount)).Format()}"));
    }

    private static Money Sum(IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero, (acc, m) => acc + m);
}
