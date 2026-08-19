using SnookerPoint.Application.Reporting;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class ReportingServiceTests
{
    private static ReportRange AllTime() =>
        new(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static PaymentInput Cash(Phase1Environment env, long rupees) =>
        new(env.CashMethodId, Money.FromRupees(rupees), Money.FromRupees(rupees), null, null);

    private static PaymentInput Electronic(Phase1Environment env, string method, long rupees) =>
        new(env.MethodId(method), Money.FromRupees(rupees), null, "REF-" + rupees, null);

    private static int CompleteWalkin(Phase1Environment env, int ownerId, int shiftId, int productId, decimal qty, params PaymentInput[] payments)
    {
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        Assert.True(env.Sales.AddProduct(saleId, productId, qty, ownerId).Succeeded);
        var complete = env.Sales.Complete(new CompleteSaleRequest(saleId, payments, ownerId, shiftId));
        Assert.True(complete.Succeeded, complete.ErrorMessage);
        return complete.Value!.SaleNumber;
    }

    private static int RunTableSession(Phase1Environment env, int tableId, int ownerId, int shiftId, int minutes, BillingType billing, long? fixedRupees = null)
    {
        Assert.True(env.Sessions.StartSession(new StartSessionRequest(tableId, ownerId, shiftId, "VIP", null, billing,
            fixedRupees is { } fr ? Money.FromRupees(fr) : null)).Succeeded);
        var id = env.Sessions.GetDashboard().First(c => c.TableId == tableId).Session!.SessionId;
        env.Clock.Advance(TimeSpan.FromMinutes(minutes));
        Assert.True(env.Sessions.FinishSession(id, ownerId, shiftId, null).Succeeded);
        return id;
    }

    private static void CheckoutSession(Phase1Environment env, int sessionId, int ownerId, int shiftId)
    {
        var draft = env.Sales.CreateTableCheckoutDraft(sessionId, ownerId);
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        var view = env.Sales.GetDraft(draft.Value)!;
        var complete = env.Sales.Complete(new CompleteSaleRequest(draft.Value,
            new[] { Cash(env, view.Totals.Total.Paisa / 100) }, ownerId, shiftId));
        Assert.True(complete.Succeeded, complete.ErrorMessage);
    }

    // ---------- Revenue counting ----------

    [Fact]
    public void HeldDraft_IsNotCountedAsRevenue()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);

        // A draft that is never completed.
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 3, ownerId);

        var dash = env.Reporting.GetDashboard(AllTime());
        Assert.Equal(0, dash.CompletedSaleCount);
        Assert.True(dash.GrossSales.IsZero);
        Assert.Empty(env.Reporting.GetSalesReport(new SalesReportFilter(AllTime())).Rows);
    }

    [Fact]
    public void CompletedSale_IsCountedExactlyOnce()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        CompleteWalkin(env, ownerId, shiftId, productId, 2, Cash(env, 120));

        var sales = env.Reporting.GetSalesReport(new SalesReportFilter(AllTime()));
        Assert.Single(sales.Rows);
        Assert.Equal(Money.FromRupees(120).Paisa, sales.Final.Paisa);

        var dash = env.Reporting.GetDashboard(AllTime());
        Assert.Equal(1, dash.CompletedSaleCount);
        Assert.Equal(Money.FromRupees(120).Paisa, dash.GrossSales.Paisa);
    }

    // ---------- Payments ----------

    [Fact]
    public void SplitPayment_IsAllocatedByMethod_AndCashOnlyExpected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 100);
        // One sale of Rs 100 paid Rs 60 cash + Rs 40 EasyPaisa.
        CompleteWalkin(env, ownerId, shiftId, productId, 1, Cash(env, 60), Electronic(env, "EasyPaisa", 40));

        var report = env.Reporting.GetPaymentReport(AllTime());
        var cash = report.Methods.Single(m => m.Kind == PaymentMethodKind.Cash);
        var easy = report.Methods.Single(m => m.MethodName == "EasyPaisa");

        Assert.Equal(Money.FromRupees(60).Paisa, cash.TotalApplied.Paisa);
        Assert.Equal(Money.FromRupees(40).Paisa, easy.TotalApplied.Paisa);
        Assert.Equal(1, report.SplitPaymentSaleCount);
        // Expected physical cash counts only the cash portion.
        Assert.Equal(Money.FromRupees(60).Paisa, report.ExpectedPhysicalCash.Paisa);
    }

    // ---------- Table report ----------

    [Fact]
    public void TableTransfer_DoesNotDuplicateSessionInTotals()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        Assert.True(env.Sessions.StartSession(new StartSessionRequest(tableIds[0], ownerId, shiftId, "VIP", null)).Succeeded);
        var id = env.Sessions.GetDashboard().First(c => c.TableId == tableIds[0]).Session!.SessionId;
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(env.Sessions.TransferSession(id, tableIds[1], ownerId, shiftId, "Move").Succeeded);
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(env.Sessions.FinishSession(id, ownerId, shiftId, null).Succeeded);

        var report = env.Reporting.GetTableReport(AllTime());
        Assert.Single(report.Rows); // one session, not two
        Assert.Equal(1, report.HourlyCount);
    }

    [Fact]
    public void TableReport_SeparatesHourlyAndFixed()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        RunTableSession(env, tableIds[0], ownerId, shiftId, 60, BillingType.Hourly);
        RunTableSession(env, tableIds[1], ownerId, shiftId, 60, BillingType.Fixed, fixedRupees: 500);

        var report = env.Reporting.GetTableReport(AllTime());
        Assert.Equal(1, report.HourlyCount);
        Assert.Equal(1, report.FixedCount);
        Assert.Equal(Money.FromRupees(500).Paisa, report.FixedTotal.Paisa);
        Assert.Equal(Money.FromRupees(120).Paisa, report.HourlyTotal.Paisa); // 60 min at Rs120/hr
    }

    // ---------- Product & profit ----------

    [Fact]
    public void ProductReport_UsesSaleSnapshots_NotCurrentPriceOrCost()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", priceRupees: 60, costRupees: 40);
        CompleteWalkin(env, ownerId, shiftId, productId, 2, Cash(env, 120));

        // Change the current price and cost AFTER the sale.
        using (var db = env.NewContext())
        {
            var p = db.Products.First(x => x.Id == productId);
            p.Price = Money.FromRupees(999);
            p.Cost = Money.FromRupees(500);
            db.SaveChanges();
        }

        var report = env.Reporting.GetProductSalesReport(AllTime());
        var row = Assert.Single(report.Rows);
        Assert.Equal(2m, row.QuantitySold);
        Assert.Equal(Money.FromRupees(120).Paisa, row.GrossRevenue.Paisa); // 2 × 60 snapshot
        Assert.True(row.CostAvailable);
        Assert.Equal(Money.FromRupees(40).Paisa, row.EstimatedProfit!.Value.Paisa); // 2 × (60 − 40)
    }

    [Fact]
    public void ProductReport_MarksProfitUnavailable_WhenNoCostRecorded()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", priceRupees: 60); // no cost
        CompleteWalkin(env, ownerId, shiftId, productId, 1, Cash(env, 60));

        var report = env.Reporting.GetProductSalesReport(AllTime());
        var row = Assert.Single(report.Rows);
        Assert.False(row.CostAvailable);
        Assert.Null(row.EstimatedProfit);
        Assert.False(report.ProfitComplete);
    }

    // ---------- Inventory ----------

    [Fact]
    public void InventorySummary_ReflectsMovements_AndValues()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", priceRupees: 60, opening: 10m, costRupees: 40);

        var summary = env.Reporting.GetInventorySummary();
        var row = summary.Stock.Single(r => r.ProductId == productId);
        Assert.Equal(10m, row.CurrentStock);
        Assert.Equal(Money.FromRupees(400).Paisa, row.StockValue.Paisa); // 10 × 40
    }

    [Fact]
    public void StockMovements_FilterByType_ReturnsWasteAndDamage()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", priceRupees: 60, opening: 20m);
        Assert.True(env.Inventory.RecordMovement(new SnookerPoint.Application.Catalog.StockMovementRequest(
            productId, StockMovementType.Waste, 2m, "Spillage", shiftId), ownerId).Succeeded);
        Assert.True(env.Inventory.RecordMovement(new SnookerPoint.Application.Catalog.StockMovementRequest(
            productId, StockMovementType.Damage, 1m, "Dropped", shiftId), ownerId).Succeeded);

        var waste = env.Reporting.GetStockMovements(new StockMovementReportFilter(AllTime(), Type: StockMovementType.Waste));
        Assert.Single(waste);
        var damage = env.Reporting.GetStockMovements(new StockMovementReportFilter(AllTime(), Type: StockMovementType.Damage));
        Assert.Single(damage);
    }

    // ---------- Shifts ----------

    [Fact]
    public void ShiftReport_ComputesExpectedCash_FromOpeningAndCashSales()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 100);
        CompleteWalkin(env, ownerId, shiftId, productId, 1, Cash(env, 100));

        var report = env.Reporting.GetShiftReport(AllTime());
        var row = report.Single(r => r.ShiftId == shiftId);
        Assert.Equal(Money.FromRupees(100).Paisa, row.CashSales.Paisa);
        Assert.Equal(Money.FromRupees(100).Paisa, row.ExpectedCash.Paisa); // opening 0 + 100 cash
        Assert.Equal(1, row.SaleCount);
    }

    // ---------- Bookings ----------

    [Fact]
    public void BookingReport_CountsByStatus()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(2), 60);
        var toCancel = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(4), 60);
        env.Bookings.Cancel(toCancel, "n/a", ownerId);

        var report = env.Reporting.GetBookingReport(AllTime());
        Assert.Equal(1, report.Scheduled);
        Assert.Equal(1, report.Cancelled);
        Assert.Equal(2, report.Rows.Count);
    }
}
