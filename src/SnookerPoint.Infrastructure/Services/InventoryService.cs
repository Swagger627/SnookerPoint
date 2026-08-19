using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Inventory;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Records and reads append-only stock movements. Stock is always recomputed from the
/// movement log; balances are never overwritten. Movements that would drive stock below
/// zero are rejected unless the product opts into negative stock. Corrections are made by
/// a reversing movement that references the original.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<InventoryService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<InventoryRow> GetInventory(InventoryFilter filter)
    {
        using var db = _factory.CreateDbContext();

        var products = db.Products.AsNoTracking().AsQueryable();
        if (!filter.IncludeInactive)
        {
            products = products.Where(p => p.IsActive);
        }

        if (filter.CategoryId is { } catId)
        {
            products = products.Where(p => p.CategoryId == catId);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim().ToLower();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Sku.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
        }

        var list = products.ToList();
        var categories = db.Categories.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);

        // SQLite cannot aggregate decimal/DateTimeOffset server-side, so pull the movement
        // columns and group in memory.
        var movements = db.StockMovements.AsNoTracking()
            .Select(m => new { m.ProductId, m.QuantityDelta, m.Utc })
            .AsEnumerable()
            .ToList();
        var stock = movements.GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityDelta));
        var lastMovement = movements.GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.Max(x => x.Utc));

        var rows = list.Select(p =>
        {
            var qty = stock.GetValueOrDefault(p.Id);
            return new InventoryRow(
                p.Id, p.Name, p.Sku, p.Barcode,
                categories.GetValueOrDefault(p.CategoryId, "—"),
                p.TrackInventory, p.IsActive, qty, p.ReorderLevel,
                InventoryMath.Classify(p.IsActive, p.TrackInventory, qty, p.ReorderLevel),
                p.Price,
                lastMovement.TryGetValue(p.Id, out var last) ? last : null);
        });

        if (filter.LowStockOnly)
        {
            rows = rows.Where(r => r.Status is StockStatus.LowStock or StockStatus.OutOfStock);
        }

        return rows.OrderBy(r => r.Name).ToList();
    }

    public IReadOnlyList<StockMovementLine> GetHistory(int productId)
    {
        using var db = _factory.CreateDbContext();
        var names = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);

        return db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .AsEnumerable()
            .OrderByDescending(m => m.Utc)
            .ThenByDescending(m => m.Id)
            .Select(m => new StockMovementLine(
                m.Id, m.Utc, m.Type, m.QuantityDelta, m.PreviousQuantity, m.NewQuantity,
                m.Reason, names.GetValueOrDefault(m.ActorUserId, "—"), m.ReversalOfMovementId))
            .ToList();
    }

    public decimal GetCurrentStock(int productId)
    {
        using var db = _factory.CreateDbContext();
        return CurrentStock(db, productId);
    }

    public OperationResult RecordMovement(StockMovementRequest request, int actorUserId)
    {
        if (request.Type == StockMovementType.OpeningStock)
        {
            return OperationResult.Failure("Opening stock is set when the product is created.");
        }

        using var db = _factory.CreateDbContext();

        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (actor is null || !_permissions.HasPermission(actor, PermissionFor(request.Type)))
        {
            return OperationResult.Failure("You do not have permission to make this stock change.");
        }

        if (request.Quantity <= 0)
        {
            return OperationResult.Failure("The quantity must be greater than zero.");
        }

        if (RequiresReason(request.Type) && string.IsNullOrWhiteSpace(request.Reason))
        {
            return OperationResult.Failure("Please enter a reason.");
        }

        var product = db.Products.FirstOrDefault(p => p.Id == request.ProductId);
        if (product is null)
        {
            return OperationResult.Failure("That product was not found.");
        }

        if (!product.TrackInventory)
        {
            return OperationResult.Failure("This product does not track inventory.");
        }

        using var tx = db.Database.BeginTransaction();
        try
        {
            var result = Append(db, product, request.Type, request.Quantity, request.Reason, actorUserId, request.ShiftId, null);
            if (result.Failed)
            {
                tx.Rollback();
                return result;
            }

            db.SaveChanges();
            tx.Commit();
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Recording a stock movement failed and was rolled back.");
            return OperationResult.Failure("Something went wrong while saving. No changes were made. Please try again.");
        }
    }

    public OperationResult ReverseMovement(int movementId, string reason, int actorUserId, int? shiftId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure("Please enter a reason for the reversal.");
        }

        using var db = _factory.CreateDbContext();

        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (actor is null || !_permissions.HasPermission(actor, Permission.AdjustInventory))
        {
            return OperationResult.Failure("You do not have permission to reverse a stock movement.");
        }

        var original = db.StockMovements.FirstOrDefault(m => m.Id == movementId);
        if (original is null)
        {
            return OperationResult.Failure("That stock movement was not found.");
        }

        if (original.ReversalOfMovementId is not null)
        {
            return OperationResult.Failure("A reversal cannot itself be reversed.");
        }

        var product = db.Products.FirstOrDefault(p => p.Id == original.ProductId);
        if (product is null)
        {
            return OperationResult.Failure("That product was not found.");
        }

        // The reversing movement is the opposite direction of the original.
        var magnitude = Math.Abs(original.QuantityDelta);
        var reversingType = original.QuantityDelta >= 0
            ? StockMovementType.NegativeAdjustment
            : StockMovementType.PositiveAdjustment;

        using var tx = db.Database.BeginTransaction();
        try
        {
            var result = Append(db, product, reversingType, magnitude, reason, actorUserId, shiftId, original.Id,
                AuditActions.StockMovementReversed,
                $"Reversed movement #{original.Id} ({original.Type}) on '{product.Name}'.");
            if (result.Failed)
            {
                tx.Rollback();
                return result;
            }

            db.SaveChanges();
            tx.Commit();
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Reversing a stock movement failed and was rolled back.");
            return OperationResult.Failure("Something went wrong while saving. No changes were made. Please try again.");
        }
    }

    /// <summary>
    /// Appends one movement (assumes an open transaction and does not commit/SaveChanges).
    /// Computes previous/new balances and enforces the non-negative rule. Also used by
    /// product creation for the opening-stock movement.
    /// </summary>
    internal static OperationResult Append(
        SnookerPointDbContext db,
        Product product,
        StockMovementType type,
        decimal magnitude,
        string? reason,
        int actorUserId,
        int? shiftId,
        int? reversalOf,
        string auditAction = AuditActions.StockMovementRecorded,
        string? auditDetails = null,
        DateTimeOffset? nowOverride = null,
        int? saleId = null,
        int? saleLineId = null)
    {
        var previous = CurrentStock(db, product.Id);
        var delta = InventoryMath.SignedDelta(type, magnitude);
        var next = previous + delta;

        if (next < 0 && !product.AllowNegativeStock)
        {
            return OperationResult.Failure(
                $"This would take '{product.Name}' below zero (available: {previous:0.###}). Negative stock is not allowed.");
        }

        var now = nowOverride ?? DateTimeOffset.UtcNow;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Type = type,
            QuantityDelta = delta,
            PreviousQuantity = previous,
            NewQuantity = next,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ActorUserId = actorUserId,
            ShiftId = shiftId,
            Utc = now,
            ReversalOfMovementId = reversalOf,
            SaleId = saleId,
            SaleLineId = saleLineId,
        });

        db.AuditEvents.Add(new AuditEvent
        {
            Utc = now,
            Action = auditAction,
            ActorUserId = actorUserId,
            Entity = nameof(StockMovement),
            EntityId = product.Id.ToString(),
            Details = auditDetails ?? $"{type} {magnitude:0.###} on '{product.Name}' ({previous:0.###} → {next:0.###}).",
        });

        return OperationResult.Success();
    }

    /// <summary>Current stock for one product, summed client-side (SQLite can't SUM decimals).</summary>
    internal static decimal CurrentStock(SnookerPointDbContext db, int productId) =>
        db.StockMovements.Where(m => m.ProductId == productId)
            .Select(m => m.QuantityDelta)
            .AsEnumerable()
            .Sum();

    private static Permission PermissionFor(StockMovementType type) => type switch
    {
        StockMovementType.StockIn => Permission.AddStock,
        StockMovementType.Waste => Permission.RecordWasteDamage,
        StockMovementType.Damage => Permission.RecordWasteDamage,
        _ => Permission.AdjustInventory,
    };

    private static bool RequiresReason(StockMovementType type) => type switch
    {
        StockMovementType.StockIn => false,
        _ => true,
    };
}
