using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Read access to completed sales and receipt reprint accounting. Never edits a completed
/// sale. Reprints are audited and increment the print count.
/// </summary>
public sealed class SalesQueryService : ISalesQueryService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<SalesQueryService> _logger;

    public SalesQueryService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<SalesQueryService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<SaleHistoryItem> GetHistory(SalesHistoryFilter filter)
    {
        using var db = _factory.CreateDbContext();

        var query = db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed);

        if (filter.SaleNumber is { } number)
        {
            query = query.Where(s => s.SaleNumber == number);
        }

        if (filter.CashierUserId is { } cashier)
        {
            query = query.Where(s => s.CompletedByUserId == cashier);
        }

        if (filter.TableSessionId is { } sessionId)
        {
            query = query.Where(s => s.TableSessionId == sessionId);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(s => s.Type == type);
        }

        var sales = query.ToList();

        // Client-side date filtering (SQLite can't compare DateTimeOffset).
        if (filter.FromUtc is { } from)
        {
            sales = sales.Where(s => s.CompletedUtc >= from).ToList();
        }

        if (filter.ToUtc is { } to)
        {
            sales = sales.Where(s => s.CompletedUtc <= to).ToList();
        }

        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var sessionNumbers = db.TableSessions.AsNoTracking().ToDictionary(s => s.Id, s => s.SessionNumber);
        var payments = db.SalePayments.AsNoTracking().ToList()
            .GroupBy(p => p.SaleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = sales
            .Where(s => filter.MethodId is not { } mid || (payments.TryGetValue(s.Id, out var ps) && ps.Any(p => p.MethodId == mid)))
            .OrderByDescending(s => s.SaleNumber)
            .Select(s => new SaleHistoryItem(
                s.Id, s.SaleNumber ?? 0, s.CompletedUtc ?? s.CreatedUtc, s.Type,
                s.TableSessionId, s.TableSessionId is { } id ? sessionNumbers.GetValueOrDefault(id) : null,
                s.CompletedByUserId is { } cid ? users.GetValueOrDefault(cid, "—") : "—",
                s.Total,
                PaymentSummary(payments.GetValueOrDefault(s.Id)),
                s.Status))
            .ToList();

        return items;
    }

    public SaleDetail? GetDetail(int saleId)
    {
        using var db = _factory.CreateDbContext();
        var sale = db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments)
            .FirstOrDefault(s => s.Id == saleId && s.Status == SaleStatus.Completed);
        if (sale is null)
        {
            return null;
        }

        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var sessionNumber = sale.TableSessionId is { } sid
            ? db.TableSessions.AsNoTracking().Where(s => s.Id == sid).Select(s => (int?)s.SessionNumber).FirstOrDefault()
            : null;

        var lines = sale.Lines.OrderBy(l => l.Id)
            .Select(l => new CartLine(l.Id, l.ProductId, l.NameSnapshot, l.SkuSnapshot, l.BarcodeSnapshot,
                l.UnitPrice, l.Quantity, l.LineTotal, l.OriginalUnitPrice, l.TrackInventory))
            .ToList();

        var payments = sale.Payments.OrderBy(p => p.Id)
            .Select(p => new SalePaymentLine(p.MethodNameSnapshot, p.Kind, p.Amount, p.ReceivedAmount, p.ChangeAmount, p.Reference))
            .ToList();

        var products = db.Products.AsNoTracking().ToDictionary(p => p.Id, p => p.Name);
        var stock = db.StockMovements.AsNoTracking()
            .Where(m => m.SaleId == saleId)
            .ToList()
            .Select(m => new SaleStockLine(products.GetValueOrDefault(m.ProductId, "—"), m.QuantityDelta, m.PreviousQuantity, m.NewQuantity))
            .ToList();

        return new SaleDetail(
            sale.Id, sale.SaleNumber ?? 0, sale.CompletedUtc ?? sale.CreatedUtc, sale.Type,
            sale.TableSessionId, sessionNumber,
            sale.CompletedByUserId is { } cid ? users.GetValueOrDefault(cid, "—") : "—",
            sale.Subtotal, sale.TableCharge, sale.DiscountAmount, sale.DiscountReason, sale.TaxAmount, sale.ServiceAmount,
            sale.Total, sale.CashReceived, sale.ChangeGiven, lines, payments, stock, sale.PrintCount);
    }

    public ReceiptData? GetReceiptData(int saleId)
    {
        using var db = _factory.CreateDbContext();
        var sale = db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments)
            .FirstOrDefault(s => s.Id == saleId && s.Status == SaleStatus.Completed);
        return sale is null ? null : SaleService.BuildReceiptData(db, sale, isReprint: false);
    }

    public string? GetReceiptSnapshot(int saleId)
    {
        using var db = _factory.CreateDbContext();
        return db.Sales.AsNoTracking().Where(s => s.Id == saleId).Select(s => s.ReceiptSnapshot).FirstOrDefault();
    }

    public OperationResult MarkReceiptPrinted(int saleId, int actorUserId, bool isReprint)
    {
        using var db = _factory.CreateDbContext();

        if (isReprint)
        {
            var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
            if (actor is null || !_permissions.HasPermission(actor, Permission.ReprintReceipt))
            {
                return OperationResult.Failure("You do not have permission to reprint receipts.");
            }
        }

        var sale = db.Sales.FirstOrDefault(s => s.Id == saleId && s.Status == SaleStatus.Completed);
        if (sale is null)
        {
            return OperationResult.Failure("That sale was not found.");
        }

        sale.PrintCount += 1;
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = isReprint ? AuditActions.ReceiptReprinted : AuditActions.ReceiptPrinted,
            ActorUserId = actorUserId,
            Entity = nameof(Sale),
            EntityId = sale.Id.ToString(),
            Details = $"Receipt for sale #{sale.SaleNumber} {(isReprint ? "reprinted" : "printed")}.",
        });
        db.SaveChanges();
        return OperationResult.Success();
    }

    private static string PaymentSummary(List<SalePayment>? payments)
    {
        if (payments is null || payments.Count == 0)
        {
            return "—";
        }

        return string.Join(", ", payments
            .GroupBy(p => p.MethodNameSnapshot)
            .Select(g => g.Count() > 1 || payments.Count > 1 ? $"{g.Key} {g.Aggregate(Money.Zero, (a, p) => a + p.Amount).Format()}" : g.Key));
    }
}
