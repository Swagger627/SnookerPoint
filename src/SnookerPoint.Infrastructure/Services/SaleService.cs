using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Sales;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Draft-sale lifecycle and transactional checkout. Drafts persist immediately (surviving a
/// crash) and deduct no stock. Completion is one atomic transaction that snapshots totals,
/// records payments, deducts tracked inventory exactly once (referencing the sale/line),
/// marks any table session Checked Out, and assigns a unique sale number. Completed sales
/// are immutable.
/// </summary>
public sealed class SaleService : ISaleService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<SaleService> _logger;

    public SaleService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<SaleService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    // ==================== CREATE ====================

    public OperationResult<int> CreateWalkinDraft(int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.CreateSale) is { } denied)
        {
            return OperationResult<int>.Failure(denied);
        }

        var now = _clock.UtcNow;
        var sale = new Sale
        {
            Type = SaleType.Walkin,
            Status = SaleStatus.Draft,
            CreatedByUserId = actorUserId,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        db.Sales.Add(sale);
        db.SaveChanges();

        WriteAudit(db, AuditActions.SaleCreated, actorUserId, sale.Id, "Walk-in sale started.");
        db.SaveChanges();
        return OperationResult<int>.Success(sale.Id);
    }

    public OperationResult<int> CreateTableCheckoutDraft(int sessionId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.CreateSale) is { } denied)
        {
            return OperationResult<int>.Failure(denied);
        }

        var session = db.TableSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
        {
            return OperationResult<int>.Failure("That table session was not found.");
        }

        if (session.Status != SessionStatus.Completed || session.CheckoutStatus != CheckoutStatus.AwaitingCheckout)
        {
            return session.CheckoutStatus == CheckoutStatus.CheckedOut
                ? OperationResult<int>.Failure("That table session has already been checked out.")
                : OperationResult<int>.Failure("That table session is not awaiting checkout.");
        }

        // Resume an existing open draft for this session rather than creating a second one.
        var existing = db.Sales.FirstOrDefault(s => s.TableSessionId == sessionId &&
            (s.Status == SaleStatus.Draft || s.Status == SaleStatus.Held));
        if (existing is not null)
        {
            return OperationResult<int>.Success(existing.Id);
        }

        var now = _clock.UtcNow;
        var sale = new Sale
        {
            Type = SaleType.Table,
            Status = SaleStatus.Draft,
            TableSessionId = sessionId,
            TableCharge = session.FinalCharge ?? Money.Zero,
            TableBillingType = session.BillingType,
            CreatedByUserId = actorUserId,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        var (taxPercent0, servicePercent0) = TaxServiceRates(db);
        RebuildTotals(sale, taxPercent0, servicePercent0);
        db.Sales.Add(sale);

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // The filtered unique index rejected a concurrent attach; resume the winner.
            var winner = db.Sales.AsNoTracking().FirstOrDefault(s => s.TableSessionId == sessionId &&
                (s.Status == SaleStatus.Draft || s.Status == SaleStatus.Held));
            return winner is not null
                ? OperationResult<int>.Success(winner.Id)
                : OperationResult<int>.Failure("That table session is already being checked out.");
        }

        WriteAudit(db, AuditActions.SaleTableAttached, actorUserId, sale.Id,
            $"Checkout started for session #{session.SessionNumber} (table charge {sale.TableCharge.Format()}).");
        db.SaveChanges();
        return OperationResult<int>.Success(sale.Id);
    }

    // ==================== READ ====================

    public DraftSaleView? GetDraft(int saleId)
    {
        using var db = _factory.CreateDbContext();
        var sale = db.Sales.AsNoTracking().Include(s => s.Lines).FirstOrDefault(s => s.Id == saleId);
        if (sale is null)
        {
            return null;
        }

        string? tableLabel = null;
        if (sale.TableSessionId is { } sid)
        {
            var session = db.TableSessions.AsNoTracking().FirstOrDefault(s => s.Id == sid);
            if (session is not null)
            {
                tableLabel = $"Session #{session.SessionNumber} · {TableNames(db, sid)}";
            }
        }

        var (taxPercentD, servicePercentD) = TaxServiceRates(db);
        return MapDraft(sale, tableLabel, taxPercentD, servicePercentD);
    }

    public IReadOnlyList<HeldSaleListItem> GetHeldSales()
    {
        using var db = _factory.CreateDbContext();
        var held = db.Sales.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.Status == SaleStatus.Held)
            .ToList();

        return held
            .OrderByDescending(s => s.Id)
            .Select(s => new HeldSaleListItem(s.Id, s.Label, s.Type, s.Lines.Count, s.Total, s.UpdatedUtc, s.TableSessionId))
            .ToList();
    }

    public IReadOnlyList<AwaitingCheckoutItem> GetAwaitingCheckout()
    {
        using var db = _factory.CreateDbContext();
        var sessions = db.TableSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Completed && s.CheckoutStatus == CheckoutStatus.AwaitingCheckout)
            .ToList();

        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var inDraft = db.Sales.AsNoTracking()
            .Where(s => s.TableSessionId != null && (s.Status == SaleStatus.Draft || s.Status == SaleStatus.Held))
            .Select(s => s.TableSessionId!.Value)
            .ToHashSet();

        return sessions
            .OrderByDescending(s => s.SessionNumber)
            .Select(s => new AwaitingCheckoutItem(
                s.Id, s.SessionNumber, TableNames(db, s.Id), s.CustomerLabel, s.FinishUtc,
                s.FinalBillableSeconds ?? 0, s.FinalCharge ?? Money.Zero, s.BillingType,
                s.FinishedByUserId is { } fid ? users.GetValueOrDefault(fid, "—") : "—",
                inDraft.Contains(s.Id)))
            .ToList();
    }

    // ==================== MUTATIONS ====================

    public OperationResult AddProduct(int saleId, int productId, decimal quantity, int actorUserId)
    {
        if (quantity <= 0)
        {
            return OperationResult.Failure("Quantity must be greater than zero.");
        }

        using var db = _factory.CreateDbContext();
        var sale = LoadEditable(db, saleId, out var error);
        if (sale is null)
        {
            return OperationResult.Failure(error!);
        }

        var product = db.Products.FirstOrDefault(p => p.Id == productId);
        if (product is null)
        {
            return OperationResult.Failure("That product was not found.");
        }

        if (!product.IsActive)
        {
            return OperationResult.Failure("That product is inactive and cannot be sold.");
        }

        // Merge into an existing, non-overridden line for the same product.
        var line = sale.Lines.FirstOrDefault(l => l.ProductId == productId && l.OriginalUnitPrice == null);
        if (line is not null)
        {
            line.Quantity += quantity;
            line.LineTotal = SaleMath.LineTotal(line.UnitPrice, line.Quantity);
        }
        else
        {
            line = new SaleLine
            {
                ProductId = product.Id,
                NameSnapshot = product.Name,
                SkuSnapshot = product.Sku,
                BarcodeSnapshot = product.Barcode,
                Quantity = quantity,
                UnitPrice = product.Price,
                CostSnapshot = product.Cost,
                TrackInventory = product.TrackInventory,
                LineTotal = SaleMath.LineTotal(product.Price, quantity),
            };
            sale.Lines.Add(line);
        }

        Touch(db, sale);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult SetLineQuantity(int saleId, int lineId, decimal quantity, int actorUserId)
    {
        if (quantity <= 0)
        {
            return OperationResult.Failure("Quantity must be greater than zero.");
        }

        using var db = _factory.CreateDbContext();
        var sale = LoadEditable(db, saleId, out var error);
        if (sale is null)
        {
            return OperationResult.Failure(error!);
        }

        var line = sale.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return OperationResult.Failure("That line was not found.");
        }

        line.Quantity = quantity;
        line.LineTotal = SaleMath.LineTotal(line.UnitPrice, quantity);
        Touch(db, sale);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult RemoveLine(int saleId, int lineId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        var sale = LoadEditable(db, saleId, out var error);
        if (sale is null)
        {
            return OperationResult.Failure(error!);
        }

        var line = sale.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return OperationResult.Failure("That line was not found.");
        }

        sale.Lines.Remove(line);
        db.SaleLines.Remove(line);
        Touch(db, sale);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult OverrideLinePrice(int saleId, int lineId, Money newUnitPrice, string reason, int actorUserId)
    {
        if (newUnitPrice.IsNegative)
        {
            return OperationResult.Failure("The price cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure("Please enter a reason for the price change.");
        }

        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.OverridePrice) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var sale = LoadEditable(db, saleId, out var error);
        if (sale is null)
        {
            return OperationResult.Failure(error!);
        }

        var line = sale.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return OperationResult.Failure("That line was not found.");
        }

        var original = line.OriginalUnitPrice ?? line.UnitPrice;
        line.OriginalUnitPrice = original;
        line.UnitPrice = newUnitPrice;
        line.LineTotal = SaleMath.LineTotal(newUnitPrice, line.Quantity);
        Touch(db, sale);

        WriteAudit(db, AuditActions.SalePriceOverridden, actorUserId, sale.Id,
            $"'{line.NameSnapshot}' price {original.Format()} → {newUnitPrice.Format()}. Reason: {reason.Trim()}");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult ApplyDiscount(int saleId, DiscountKind kind, long value, string reason, int actorUserId)
    {
        if (kind == DiscountKind.None || value <= 0)
        {
            return OperationResult.Failure("Enter a discount greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure("Please enter a reason for the discount.");
        }

        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ApplyDiscount) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var sale = LoadEditable(db, saleId, out var error);
        if (sale is null)
        {
            return OperationResult.Failure(error!);
        }

        sale.DiscountKind = kind;
        sale.DiscountValue = value;
        sale.DiscountReason = reason.Trim();
        Touch(db, sale);

        WriteAudit(db, AuditActions.SaleDiscountApplied, actorUserId, sale.Id,
            $"Discount {(kind == DiscountKind.Percentage ? value + "%" : Money.FromPaisa(value).Format())} applied ({sale.DiscountAmount.Format()}). Reason: {reason.Trim()}");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult RemoveDiscount(int saleId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        var sale = LoadEditable(db, saleId, out var error);
        if (sale is null)
        {
            return OperationResult.Failure(error!);
        }

        sale.DiscountKind = DiscountKind.None;
        sale.DiscountValue = 0;
        sale.DiscountReason = null;
        Touch(db, sale);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult Hold(int saleId, string? label, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        var sale = LoadEditable(db, saleId, out var error);
        if (sale is null)
        {
            return OperationResult.Failure(error!);
        }

        sale.Status = SaleStatus.Held;
        sale.Label = string.IsNullOrWhiteSpace(label) ? sale.Label : label.Trim();
        Touch(db, sale);
        WriteAudit(db, AuditActions.SaleHeld, actorUserId, sale.Id, "Sale held.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult Reopen(int saleId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ViewHeldSales) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var sale = db.Sales.Include(s => s.Lines).FirstOrDefault(s => s.Id == saleId);
        if (sale is null || sale.Status != SaleStatus.Held)
        {
            return OperationResult.Failure("That held sale was not found.");
        }

        sale.Status = SaleStatus.Draft;
        Touch(db, sale);
        WriteAudit(db, AuditActions.SaleReopened, actorUserId, sale.Id, "Held sale reopened.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult Cancel(int saleId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.CancelDraftSale) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var sale = db.Sales.FirstOrDefault(s => s.Id == saleId);
        if (sale is null || (sale.Status != SaleStatus.Draft && sale.Status != SaleStatus.Held))
        {
            return OperationResult.Failure("That sale cannot be cancelled.");
        }

        sale.Status = SaleStatus.Cancelled;
        sale.CancelReason = "Cancelled by cashier.";
        sale.UpdatedUtc = _clock.UtcNow;
        WriteAudit(db, AuditActions.SaleCancelled, actorUserId, sale.Id, "Draft sale cancelled.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    // ==================== COMPLETE (transactional) ====================

    public OperationResult<SaleCompletionResult> Complete(CompleteSaleRequest request)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, request.ActorUserId, Permission.CompletePayment) is { } denied)
        {
            return OperationResult<SaleCompletionResult>.Failure(denied);
        }

        var shift = db.Shifts.FirstOrDefault(s => s.Id == request.ShiftId);
        if (shift is null || shift.Status != ShiftStatus.Open)
        {
            return OperationResult<SaleCompletionResult>.Failure("An open shift is required before taking payment.");
        }

        var sale = db.Sales.Include(s => s.Lines).FirstOrDefault(s => s.Id == request.SaleId);
        if (sale is null)
        {
            return OperationResult<SaleCompletionResult>.Failure("That sale was not found.");
        }

        if (sale.Status != SaleStatus.Draft && sale.Status != SaleStatus.Held)
        {
            return OperationResult<SaleCompletionResult>.Failure("This sale has already been completed or cancelled.");
        }

        if (sale.Lines.Count == 0 && sale.TableCharge.IsZero)
        {
            return OperationResult<SaleCompletionResult>.Failure("Add at least one product or a table charge before taking payment.");
        }

        var (taxPercentC, servicePercentC) = TaxServiceRates(db);
        RebuildTotals(sale, taxPercentC, servicePercentC);
        var amountDue = sale.Total;

        // Resolve and validate the payment portions.
        var methods = db.PaymentMethods.AsNoTracking().ToDictionary(m => m.Id);
        var entries = new List<PaymentEntry>();
        foreach (var p in request.Payments)
        {
            if (!methods.TryGetValue(p.MethodId, out var method) || !method.IsActive)
            {
                return OperationResult<SaleCompletionResult>.Failure("An invalid payment method was selected.");
            }

            entries.Add(new PaymentEntry(method.Kind, p.Amount, p.CashReceived));
        }

        var validation = PaymentMath.Validate(amountDue, entries);
        if (!validation.IsValid)
        {
            return OperationResult<SaleCompletionResult>.Failure(validation.Error ?? "The payment is not valid.");
        }

        // Re-check the table session inside the coming transaction scope.
        TableSession? session = null;
        if (sale.TableSessionId is { } sid)
        {
            session = db.TableSessions.FirstOrDefault(s => s.Id == sid);
            if (session is null)
            {
                return OperationResult<SaleCompletionResult>.Failure("The linked table session was not found.");
            }

            if (session.CheckoutStatus == CheckoutStatus.CheckedOut)
            {
                return OperationResult<SaleCompletionResult>.Failure("That table session has already been checked out.");
            }
        }

        var now = _clock.UtcNow;
        using var tx = db.Database.BeginTransaction();
        try
        {
            // Assign the next sale number and freeze the sale.
            var nextNumber = (db.Sales.Where(s => s.SaleNumber != null).Max(s => (int?)s.SaleNumber) ?? 0) + 1;
            sale.SaleNumber = nextNumber;
            sale.Status = SaleStatus.Completed;
            sale.ShiftId = shift.Id;
            sale.CompletedByUserId = request.ActorUserId;
            sale.CompletedUtc = now;
            sale.UpdatedUtc = now;
            sale.CashReceived = validation.CashApplied.IsZero && validation.Change.IsZero ? null : validation.CashApplied + validation.Change;
            sale.ChangeGiven = validation.Change;

            // Payment rows.
            foreach (var p in request.Payments)
            {
                var method = methods[p.MethodId];
                var isCash = method.Kind == PaymentMethodKind.Cash;
                sale.Payments.Add(new SalePayment
                {
                    MethodId = method.Id,
                    MethodNameSnapshot = method.Name,
                    Kind = method.Kind,
                    Amount = p.Amount,
                    ReceivedAmount = isCash ? (p.CashReceived ?? p.Amount) : null,
                    ChangeAmount = isCash ? (p.CashReceived ?? p.Amount) - p.Amount : null,
                    Reference = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim(),
                    Note = string.IsNullOrWhiteSpace(p.Note) ? null : p.Note.Trim(),
                    Utc = now,
                });
            }

            db.SaveChanges(); // sale + lines + payments get ids

            // Deduct inventory exactly once for tracked lines.
            foreach (var line in sale.Lines.Where(l => l.TrackInventory && l.ProductId is not null))
            {
                var product = db.Products.First(p => p.Id == line.ProductId!.Value);
                var deduct = InventoryService.Append(
                    db, product, StockMovementType.Sale, line.Quantity,
                    $"Sale #{nextNumber}", request.ActorUserId, shift.Id, reversalOf: null,
                    nowOverride: now, saleId: sale.Id, saleLineId: line.Id);
                if (deduct.Failed)
                {
                    tx.Rollback();
                    return OperationResult<SaleCompletionResult>.Failure(deduct.ErrorMessage);
                }
            }

            // Mark the table session checked out and permanently linked.
            if (session is not null)
            {
                session.CheckoutStatus = CheckoutStatus.CheckedOut;
                session.UpdatedUtc = now;
            }

            // Render and store the immutable receipt snapshot.
            var receipt = BuildReceiptData(db, sale, isReprint: false);
            var width = db.ClubSettings.AsNoTracking().Select(c => c.ReceiptWidthMm).FirstOrDefault();
            var receiptText = ReceiptRenderer.Render(receipt, width == 0 ? 58 : width, isReprint: false);
            sale.ReceiptSnapshot = receiptText;

            WriteAudit(db, AuditActions.SaleCompleted, request.ActorUserId, sale.Id,
                $"Sale #{nextNumber} completed. Total {sale.Total.Format()}, {request.Payments.Count} payment(s).");

            db.SaveChanges();
            tx.Commit();

            return OperationResult<SaleCompletionResult>.Success(
                new SaleCompletionResult(sale.Id, nextNumber, sale.Total, validation.Change, receiptText));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Completing sale {SaleId} failed and was rolled back.", request.SaleId);
            return OperationResult<SaleCompletionResult>.Failure(
                "Something went wrong while completing the sale. Nothing was charged. Please try again.");
        }
    }

    // ==================== HELPERS ====================

    private Sale? LoadEditable(SnookerPointDbContext db, int saleId, out string? error)
    {
        var sale = db.Sales.Include(s => s.Lines).FirstOrDefault(s => s.Id == saleId);
        if (sale is null)
        {
            error = "That sale was not found.";
            return null;
        }

        if (sale.Status != SaleStatus.Draft)
        {
            error = "This sale is not open for editing.";
            return null;
        }

        error = null;
        return sale;
    }

    private void Touch(SnookerPointDbContext db, Sale sale)
    {
        var (taxPercent, servicePercent) = TaxServiceRates(db);
        RebuildTotals(sale, taxPercent, servicePercent);
        sale.UpdatedUtc = _clock.UtcNow;
    }

    /// <summary>Current tax/service percentages (0 when the corresponding charge is disabled).</summary>
    private static (decimal TaxPercent, decimal ServicePercent) TaxServiceRates(SnookerPointDbContext db)
    {
        var s = db.ClubSettings.AsNoTracking()
            .Select(c => new { c.TaxEnabled, c.TaxPercent, c.ServiceChargeEnabled, c.ServiceChargePercent })
            .FirstOrDefault();
        if (s is null)
        {
            return (0m, 0m);
        }

        return (s.TaxEnabled ? s.TaxPercent : 0m, s.ServiceChargeEnabled ? s.ServiceChargePercent : 0m);
    }

    private static void RebuildTotals(Sale sale, decimal taxPercent, decimal servicePercent)
    {
        var subtotal = sale.Lines.Aggregate(Money.Zero, (acc, l) => acc + l.LineTotal);
        var totals = SaleMath.ComputeWithRates(subtotal, sale.TableCharge, sale.DiscountKind, sale.DiscountValue, taxPercent, servicePercent);
        sale.Subtotal = totals.Subtotal;
        sale.DiscountAmount = totals.Discount;
        sale.TaxAmount = totals.Tax;
        sale.ServiceAmount = totals.Service;
        sale.Total = totals.Total;
    }

    private static DraftSaleView MapDraft(Sale sale, string? tableLabel, decimal taxPercent, decimal servicePercent)
    {
        var lines = sale.Lines
            .OrderBy(l => l.Id)
            .Select(l => new CartLine(l.Id, l.ProductId, l.NameSnapshot, l.SkuSnapshot, l.BarcodeSnapshot,
                l.UnitPrice, l.Quantity, l.LineTotal, l.OriginalUnitPrice, l.TrackInventory))
            .ToList();

        var subtotal = sale.Lines.Aggregate(Money.Zero, (acc, l) => acc + l.LineTotal);
        var totals = SaleMath.ComputeWithRates(subtotal, sale.TableCharge, sale.DiscountKind, sale.DiscountValue, taxPercent, servicePercent);

        return new DraftSaleView(
            sale.Id, sale.Type, sale.Status, sale.Label, sale.TableSessionId, tableLabel,
            sale.TableCharge, sale.TableBillingType, sale.DiscountKind, sale.DiscountValue, sale.DiscountReason,
            lines, totals);
    }

    internal static ReceiptData BuildReceiptData(SnookerPointDbContext db, Sale sale, bool isReprint)
    {
        var club = db.ClubSettings.AsNoTracking().FirstOrDefault();
        var cashier = sale.CompletedByUserId is { } cid
            ? db.Users.AsNoTracking().Where(u => u.Id == cid).Select(u => u.DisplayName).FirstOrDefault() ?? "—"
            : db.Users.AsNoTracking().Where(u => u.Id == sale.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault() ?? "—";

        string? tableInfo = null;
        if (sale.TableSessionId is { } sid)
        {
            var session = db.TableSessions.AsNoTracking().FirstOrDefault(s => s.Id == sid);
            if (session is not null)
            {
                tableInfo = $"Table session #{session.SessionNumber} ({TableNames(db, sid)})";
            }
        }

        var lines = sale.Lines.OrderBy(l => l.Id)
            .Select(l => new ReceiptLine(l.NameSnapshot, l.Quantity, l.UnitPrice, l.LineTotal))
            .ToList();

        var payments = sale.Payments.OrderBy(p => p.Id)
            .Select(p => new ReceiptPayment(p.MethodNameSnapshot, p.Amount, p.ReceivedAmount, p.ChangeAmount, p.Reference))
            .ToList();

        return new ReceiptData(
            club?.ClubName ?? "Snooker Point",
            club?.Address,
            club?.Phone,
            sale.SaleNumber ?? 0,
            sale.CompletedUtc ?? sale.CreatedUtc,
            cashier,
            sale.Type == SaleType.Table ? "Table" : "Walk-in",
            tableInfo,
            lines,
            sale.TableCharge,
            sale.Subtotal,
            sale.DiscountAmount,
            sale.TaxAmount,
            sale.ServiceAmount,
            sale.Total,
            payments,
            sale.CashReceived,
            sale.ChangeGiven);
    }

    internal static string TableNames(SnookerPointDbContext db, int sessionId)
    {
        var tableIds = db.SessionSegments.AsNoTracking()
            .Where(seg => seg.SessionId == sessionId)
            .Select(seg => seg.TableId)
            .Distinct()
            .ToList();

        var names = db.PoolTables.AsNoTracking()
            .Where(t => tableIds.Contains(t.Id))
            .Select(t => t.Name)
            .ToList();

        return names.Count > 0 ? string.Join(", ", names) : "—";
    }

    private string? Guard(SnookerPointDbContext db, int actorUserId, Permission permission)
    {
        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        return actor is not null && _permissions.HasPermission(actor, permission)
            ? null
            : "You do not have permission for this action.";
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int actorUserId, int saleId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(Sale),
            EntityId = saleId.ToString(),
            Details = details,
        });
    }
}
