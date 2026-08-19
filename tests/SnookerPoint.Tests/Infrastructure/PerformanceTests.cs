using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Reporting;
using SnookerPoint.Application.Audit;
using SnookerPoint.Application.Sales;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;
using Xunit.Abstractions;

namespace SnookerPoint.Tests.Infrastructure;

/// <summary>
/// Realistic-scale performance checks against a generated temporary database (never the real
/// user data). Data is created directly via the context for speed, measured, then discarded when
/// the environment is disposed. Thresholds are generous sanity bounds; the measured timings are
/// written to test output and reported honestly.
/// </summary>
[Collection("Performance")]
public class PerformanceTests
{
    // Substantial-but-CI-practical scale. See the report for the honestly-measured numbers.
    private const int Products = 10_000;
    private const int Sales = 20_000;
    private const int Movements = 20_000;
    private const int Sessions = 5_000;
    private const int Bookings = 5_000;
    private const int AuditEvents = 20_000;

    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LargeDataset_KeyOperations_PerformWithinBounds()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        var categoryId = env.SeedCategory(ownerId, "Bulk");
        var cashMethodId = env.CashMethodId;

        var seed = Measure(() => Seed(env, ownerId, shiftId, tableIds[0], categoryId, cashMethodId));
        _output.WriteLine($"Seed {Products} products / {Sales} sales / {Movements} movements / {Sessions} sessions / {Bookings} bookings / {AuditEvents} audit: {seed} ms");

        var range = new ReportRange(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var search = Measure(() =>
        {
            var results = env.Products.GetList(new ProductFilter(SearchText: "Product 9999"));
            Assert.NotEmpty(results);
        });
        _output.WriteLine($"Product search: {search} ms");

        var barcode = Measure(() => env.Products.GetList(new ProductFilter(SearchText: "PBAR012345")));
        _output.WriteLine($"Barcode-style lookup: {barcode} ms");

        var history = Measure(() =>
        {
            var page = env.SalesQuery.GetHistory(new SalesHistoryFilter());
            Assert.NotEmpty(page);
        });
        _output.WriteLine($"Sales history load: {history} ms");

        var dashboard = Measure(() => env.Reporting.GetDashboard(range));
        _output.WriteLine($"Dashboard report: {dashboard} ms");

        var salesReport = Measure(() => env.Reporting.GetSalesReport(new SalesReportFilter(range)));
        _output.WriteLine($"Sales report: {salesReport} ms");

        var auditPage = Measure(() => env.Audit.Query(new AuditFilter(), 0, 100));
        _output.WriteLine($"Audit pagination (page of 100): {auditPage} ms");

        var backup = Measure(() => Assert.True(env.Backups.CreateBackup(null, "perf", ownerId).Succeeded));
        _output.WriteLine($"Backup creation: {backup} ms");

        var integrity = Measure(() => Assert.True(env.Health.RunIntegrityCheck(ownerId).Succeeded));
        _output.WriteLine($"Integrity check: {integrity} ms");

        // Generous sanity bounds for a single-computer POS on developer hardware.
        Assert.True(search < 5_000, $"Product search too slow: {search} ms");
        Assert.True(barcode < 5_000, $"Barcode lookup too slow: {barcode} ms");
        Assert.True(history < 15_000, $"Sales history too slow: {history} ms");
        Assert.True(dashboard < 15_000, $"Dashboard too slow: {dashboard} ms");
        Assert.True(auditPage < 5_000, $"Audit pagination too slow: {auditPage} ms");
        Assert.True(backup < 60_000, $"Backup too slow: {backup} ms");
        Assert.True(integrity < 60_000, $"Integrity check too slow: {integrity} ms");
    }

    private static long Measure(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static void Seed(Phase1Environment env, int ownerId, int shiftId, int tableId, int categoryId, int cashMethodId)
    {
        using var db = env.NewContext();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var productIds = new List<int>();

        // Products
        for (var i = 0; i < Products; i++)
        {
            db.Products.Add(new Product
            {
                Name = $"Product {i}",
                Sku = $"SKU{i:D6}",
                Barcode = $"PBAR{i:D6}",
                CategoryId = categoryId,
                Price = Money.FromRupees(50 + (i % 200)),
                Cost = Money.FromRupees(30 + (i % 100)),
                TrackInventory = true,
                ReorderLevel = 5,
                IsActive = true,
                CreatedUtc = now,
                UpdatedUtc = now,
            });
            FlushIfNeeded(db, i);
        }

        Save(db);
        productIds = db.Products.AsNoTracking().Select(p => p.Id).Take(Products).ToList();

        // Completed sales with one line + one payment each
        for (var i = 0; i < Sales; i++)
        {
            var pid = productIds[i % productIds.Count];
            var sale = new Sale
            {
                SaleNumber = i + 1,
                Type = SaleType.Walkin,
                Status = SaleStatus.Completed,
                Subtotal = Money.FromRupees(100),
                Total = Money.FromRupees(100),
                CreatedByUserId = ownerId,
                CompletedByUserId = ownerId,
                ShiftId = shiftId,
                CreatedUtc = now,
                UpdatedUtc = now,
                CompletedUtc = now.AddMinutes(i % 10_000),
                Lines = new List<SaleLine>
                {
                    new() { ProductId = pid, NameSnapshot = $"Product {i % Products}", SkuSnapshot = $"SKU{i % Products:D6}",
                        BarcodeSnapshot = $"PBAR{i % Products:D6}", Quantity = 1, UnitPrice = Money.FromRupees(100),
                        CostSnapshot = Money.FromRupees(60), LineTotal = Money.FromRupees(100), TrackInventory = true },
                },
                Payments = new List<SalePayment>
                {
                    new() { MethodId = cashMethodId, MethodNameSnapshot = "Cash", Kind = PaymentMethodKind.Cash,
                        Amount = Money.FromRupees(100), ReceivedAmount = Money.FromRupees(100), ChangeAmount = Money.Zero, Utc = now },
                },
            };
            db.Sales.Add(sale);
            FlushIfNeeded(db, i);
        }

        Save(db);

        // Stock movements
        for (var i = 0; i < Movements; i++)
        {
            var pid = productIds[i % productIds.Count];
            db.StockMovements.Add(new StockMovement
            {
                ProductId = pid,
                Type = (StockMovementType)(i % 7),
                QuantityDelta = 1,
                PreviousQuantity = 100,
                NewQuantity = 101,
                ActorUserId = ownerId,
                Utc = now.AddMinutes(i % 10_000),
            });
            FlushIfNeeded(db, i);
        }

        Save(db);

        // Table sessions
        for (var i = 0; i < Sessions; i++)
        {
            db.TableSessions.Add(new TableSession
            {
                SessionNumber = i + 1,
                Status = SessionStatus.Completed,
                CheckoutStatus = CheckoutStatus.CheckedOut,
                CurrentTableId = tableId,
                StartUtc = now.AddMinutes(i),
                FinishUtc = now.AddMinutes(i + 60),
                BillingType = BillingType.Hourly,
                OpenedByUserId = ownerId,
                OpenedShiftId = shiftId,
                FinalCharge = Money.FromRupees(120),
                FinalBillableSeconds = 3600,
                CreatedUtc = now,
                UpdatedUtc = now,
            });
            FlushIfNeeded(db, i);
        }

        Save(db);

        // Bookings
        for (var i = 0; i < Bookings; i++)
        {
            db.Bookings.Add(new Booking
            {
                CustomerName = $"Customer {i}",
                TableId = tableId,
                StartUtc = now.AddHours(i),
                DurationMinutes = 60,
                Status = (BookingStatus)(i % 6),
                CreatedByUserId = ownerId,
                CreatedUtc = now,
                UpdatedUtc = now,
            });
            FlushIfNeeded(db, i);
        }

        Save(db);

        // Audit history
        for (var i = 0; i < AuditEvents; i++)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Utc = now.AddMinutes(i % 10_000),
                Action = i % 2 == 0 ? AuditActions.SaleCompleted : AuditActions.StockMovementRecorded,
                ActorUserId = ownerId,
                Entity = "Perf",
                EntityId = i.ToString(),
                Details = $"Perf event {i}",
            });
            FlushIfNeeded(db, i);
        }

        Save(db);
    }

    private static void FlushIfNeeded(SnookerPoint.Infrastructure.Persistence.SnookerPointDbContext db, int i)
    {
        if (i > 0 && i % 5_000 == 0)
        {
            Save(db);
        }
    }

    private static void Save(SnookerPoint.Infrastructure.Persistence.SnookerPointDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }
}
