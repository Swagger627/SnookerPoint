using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Shifts;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Cashier shift and cash-movement operations. Enforces one open shift per user,
/// append-only movements, and irreversible closing. Expected cash (Phase 1) =
/// Opening + CashIn − CashOut − Expenses − Drops.
/// </summary>
public sealed class ShiftService : IShiftService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IClock _clock;
    private readonly ILogger<ShiftService> _logger;

    public ShiftService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IClock clock,
        ILogger<ShiftService> logger)
    {
        _factory = factory;
        _clock = clock;
        _logger = logger;
    }

    public OperationResult<ShiftSummary> OpenShift(int userId, Money openingCash, string? note)
    {
        if (openingCash.IsNegative)
        {
            return OperationResult<ShiftSummary>.Failure("Opening cash cannot be negative.");
        }

        using var db = _factory.CreateDbContext();

        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null || !user.IsActive)
        {
            return OperationResult<ShiftSummary>.Failure("Your account cannot open a shift.");
        }

        if (db.Shifts.Any(s => s.UserId == userId && s.Status == ShiftStatus.Open))
        {
            return OperationResult<ShiftSummary>.Failure("You already have an open shift.");
        }

        var now = _clock.UtcNow;
        var shift = new Shift
        {
            UserId = userId,
            OpenedUtc = now,
            OpeningCash = openingCash,
            OpeningNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = ShiftStatus.Open,
        };
        db.Shifts.Add(shift);
        db.SaveChanges();

        db.AuditEvents.Add(new AuditEvent
        {
            Utc = now,
            Action = AuditActions.ShiftOpened,
            ActorUserId = userId,
            Entity = nameof(Shift),
            EntityId = shift.Id.ToString(),
            Details = $"Opened with opening cash {openingCash.Format()}.",
        });
        db.SaveChanges();

        _logger.LogInformation("Shift {ShiftId} opened for user {UserId}.", shift.Id, userId);
        return OperationResult<ShiftSummary>.Success(BuildSummary(db, shift, user.DisplayName));
    }

    public ShiftSummary? GetCurrentShift(int userId)
    {
        using var db = _factory.CreateDbContext();
        var shift = db.Shifts
            .Include(s => s.User)
            .FirstOrDefault(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        return shift is null ? null : BuildSummary(db, shift, shift.User?.DisplayName ?? string.Empty);
    }

    public OperationResult RecordCashMovement(
        int shiftId,
        CashMovementType type,
        Money amount,
        string reason,
        int actorUserId,
        int? approverUserId = null)
    {
        if (!amount.IsPositive)
        {
            return OperationResult.Failure("Please enter an amount greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure("Please enter a reason.");
        }

        using var db = _factory.CreateDbContext();
        var shift = db.Shifts.FirstOrDefault(s => s.Id == shiftId);
        if (shift is null || shift.Status != ShiftStatus.Open)
        {
            return OperationResult.Failure("This shift is not open.");
        }

        var now = _clock.UtcNow;
        db.CashMovements.Add(new CashMovement
        {
            ShiftId = shiftId,
            Type = type,
            Amount = amount,
            Reason = reason.Trim(),
            CreatedUtc = now,
            ActorUserId = actorUserId,
            ApproverUserId = approverUserId,
        });
        db.SaveChanges();

        db.AuditEvents.Add(new AuditEvent
        {
            Utc = now,
            Action = AuditActions.CashMovementRecorded,
            ActorUserId = actorUserId,
            Entity = nameof(Shift),
            EntityId = shiftId.ToString(),
            Details = $"{type} {amount.Format()} — {reason.Trim()}",
        });
        db.SaveChanges();

        return OperationResult.Success();
    }

    public IReadOnlyList<CashMovementLine> GetCashMovements(int shiftId)
    {
        using var db = _factory.CreateDbContext();
        // Order by the autoincrement Id (monotonic with insertion order); SQLite
        // cannot ORDER BY a DateTimeOffset column.
        return db.CashMovements
            .AsNoTracking()
            .Where(m => m.ShiftId == shiftId)
            .OrderByDescending(m => m.Id)
            .Select(m => new CashMovementLine(m.Type, m.Amount, m.Reason, m.CreatedUtc))
            .ToList();
    }

    public OperationResult<ShiftCloseResult> CloseShift(int shiftId, Money countedCash, string? note)
    {
        if (countedCash.IsNegative)
        {
            return OperationResult<ShiftCloseResult>.Failure("Counted cash cannot be negative.");
        }

        using var db = _factory.CreateDbContext();
        var shift = db.Shifts.FirstOrDefault(s => s.Id == shiftId);
        if (shift is null || shift.Status != ShiftStatus.Open)
        {
            return OperationResult<ShiftCloseResult>.Failure("This shift is not open.");
        }

        var expected = ComputeExpectedCash(db, shift);
        var variance = countedCash - expected;
        var now = _clock.UtcNow;

        shift.Status = ShiftStatus.Closed;
        shift.ClosedUtc = now;
        shift.ExpectedCash = expected;
        shift.CountedCash = countedCash;
        shift.Variance = variance;
        shift.ClosingNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        db.SaveChanges();

        db.AuditEvents.Add(new AuditEvent
        {
            Utc = now,
            Action = AuditActions.ShiftClosed,
            ActorUserId = shift.UserId,
            Entity = nameof(Shift),
            EntityId = shiftId.ToString(),
            Details = $"Expected {expected.Format()}, counted {countedCash.Format()}, variance {variance.Format()}.",
        });
        db.SaveChanges();

        _logger.LogInformation("Shift {ShiftId} closed with variance {Variance}.", shiftId, variance.Paisa);
        return OperationResult<ShiftCloseResult>.Success(new ShiftCloseResult(expected, countedCash, variance));
    }

    private static ShiftSummary BuildSummary(SnookerPointDbContext db, Shift shift, string userDisplayName)
    {
        var totals = SumByType(db, shift.Id);
        var sales = SumSales(db, shift.Id);
        var expected = shift.OpeningCash + sales.CashSales + totals.CashIn - totals.CashOut - totals.Expense - totals.Drop;

        return new ShiftSummary(
            shift.Id,
            shift.UserId,
            userDisplayName,
            shift.OpenedUtc,
            shift.OpeningCash,
            totals.CashIn,
            totals.CashOut,
            totals.Expense,
            totals.Drop,
            expected,
            shift.OpeningNote,
            sales.Gross,
            sales.CashSales,
            sales.ElectronicSales,
            sales.Discount,
            sales.SaleCount,
            sales.PaymentTotals);
    }

    private static Money ComputeExpectedCash(SnookerPointDbContext db, Shift shift)
    {
        var totals = SumByType(db, shift.Id);
        var sales = SumSales(db, shift.Id);
        return shift.OpeningCash + sales.CashSales + totals.CashIn - totals.CashOut - totals.Expense - totals.Drop;
    }

    /// <summary>Aggregates completed-sale payment totals for a shift (cash vs electronic, per method).</summary>
    private static (Money Gross, Money CashSales, Money ElectronicSales, Money Discount, int SaleCount, System.Collections.Generic.List<ShiftPaymentTotal> PaymentTotals) SumSales(
        SnookerPointDbContext db, int shiftId)
    {
        var saleIds = db.Sales.AsNoTracking()
            .Where(s => s.ShiftId == shiftId && s.Status == SaleStatus.Completed)
            .Select(s => new { s.Id, s.Total, s.DiscountAmount })
            .ToList();

        var idSet = saleIds.Select(s => s.Id).ToHashSet();
        var gross = saleIds.Aggregate(Money.Zero, (acc, s) => acc + s.Total);
        var discount = saleIds.Aggregate(Money.Zero, (acc, s) => acc + s.DiscountAmount);

        var payments = db.SalePayments.AsNoTracking()
            .Select(p => new { p.SaleId, p.MethodNameSnapshot, p.Kind, p.Amount })
            .ToList()
            .Where(p => idSet.Contains(p.SaleId))
            .ToList();

        var cashSales = payments.Where(p => p.Kind == PaymentMethodKind.Cash).Aggregate(Money.Zero, (a, p) => a + p.Amount);
        var electronicSales = payments.Where(p => p.Kind == PaymentMethodKind.Electronic).Aggregate(Money.Zero, (a, p) => a + p.Amount);

        var perMethod = payments
            .GroupBy(p => new { p.MethodNameSnapshot, p.Kind })
            .Select(g => new ShiftPaymentTotal(g.Key.MethodNameSnapshot, g.Key.Kind, g.Aggregate(Money.Zero, (a, p) => a + p.Amount)))
            .OrderBy(t => t.Kind).ThenBy(t => t.MethodName)
            .ToList();

        return (gross, cashSales, electronicSales, discount, saleIds.Count, perMethod);
    }

    private static (Money CashIn, Money CashOut, Money Expense, Money Drop) SumByType(
        SnookerPointDbContext db, int shiftId)
    {
        // Money is a converted value object, so the sum is done in memory after
        // materialising the (small) movement set — EF cannot translate Money.Paisa.
        var movements = db.CashMovements
            .AsNoTracking()
            .Where(m => m.ShiftId == shiftId)
            .Select(m => new { m.Type, m.Amount })
            .ToList();

        Money Total(CashMovementType type) =>
            movements
                .Where(x => x.Type == type)
                .Aggregate(Money.Zero, (acc, x) => acc + x.Amount);

        return (
            Total(CashMovementType.CashIn),
            Total(CashMovementType.CashOut),
            Total(CashMovementType.Expense),
            Total(CashMovementType.Drop));
    }
}
