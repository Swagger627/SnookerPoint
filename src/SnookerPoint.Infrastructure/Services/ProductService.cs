using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Inventory;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Manages the product catalogue. Enforces unique SKU/barcode, non-negative money, a
/// required name/price, and treats each flavour/size/barcode as its own SKU. Creating a
/// product with an opening quantity also writes an Opening Stock movement in the same
/// transaction. Every change is audited (price/cost/barcode changes specifically).
/// </summary>
public sealed class ProductService : IProductService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IProductImageStore _images;
    private readonly IClock _clock;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IProductImageStore images,
        IClock clock,
        ILogger<ProductService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _images = images;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<ProductListItem> GetList(ProductFilter filter)
    {
        using var db = _factory.CreateDbContext();

        var query = db.Products.AsNoTracking().AsQueryable();

        query = filter.Active switch
        {
            ProductActiveFilter.ActiveOnly => query.Where(p => p.IsActive),
            ProductActiveFilter.InactiveOnly => query.Where(p => !p.IsActive),
            _ => query,
        };

        if (filter.CategoryId is { } catId)
        {
            query = query.Where(p => p.CategoryId == catId);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Sku.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)) ||
                (p.Brand != null && p.Brand.ToLower().Contains(term)));
        }

        var products = query.ToList();
        var categories = db.Categories.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);

        // SQLite cannot aggregate decimal/DateTimeOffset server-side; group in memory.
        var movements = db.StockMovements.AsNoTracking()
            .Select(m => new { m.ProductId, m.QuantityDelta, m.Utc })
            .AsEnumerable()
            .ToList();
        var stock = movements.GroupBy(m => m.ProductId)
            .ToDictionary(g => g.Key, g => (Qty: g.Sum(x => x.QuantityDelta), Last: (DateTimeOffset?)g.Max(x => x.Utc)));

        var rows = products.Select(p =>
        {
            var info = stock.TryGetValue(p.Id, out var s) ? s : (Qty: 0m, Last: (DateTimeOffset?)null);
            var status = InventoryMath.Classify(p.IsActive, p.TrackInventory, info.Qty, p.ReorderLevel);
            return new ProductListItem(
                p.Id, p.Name, p.Sku, p.Barcode, p.CategoryId,
                categories.GetValueOrDefault(p.CategoryId, "—"),
                p.Brand, p.Variant, p.Size, p.Unit, p.Cost, p.Price,
                p.TrackInventory, p.ReorderLevel, p.IsActive, info.Qty, status, p.ImagePath,
                info.Last);
        });

        if (filter.LowStockOnly)
        {
            rows = rows.Where(r => r.Status is StockStatus.LowStock or StockStatus.OutOfStock);
        }

        return rows.OrderBy(r => r.Name).ThenBy(r => r.Sku).ToList();
    }

    public ProductDetail? Get(int id)
    {
        using var db = _factory.CreateDbContext();
        var p = db.Products.AsNoTracking().FirstOrDefault(x => x.Id == id);
        if (p is null)
        {
            return null;
        }

        var stock = InventoryService.CurrentStock(db, id);
        return new ProductDetail(
            p.Id, p.Name, p.Sku, p.Barcode, p.CategoryId, p.Brand, p.Variant, p.Size, p.Unit,
            p.Cost, p.Price, p.TrackInventory, p.ReorderLevel, p.AllowNegativeStock, p.IsActive,
            p.ImagePath, p.ImageHash, p.ImageOriginalName, p.Notes, stock);
    }

    public ProductListItem? FindByBarcode(string barcode)
    {
        var normalized = ProductValidation.NormalizeBarcode(barcode);
        if (normalized is null)
        {
            return null;
        }

        using var db = _factory.CreateDbContext();
        var p = db.Products.AsNoTracking().FirstOrDefault(x => x.Barcode == normalized);
        if (p is null)
        {
            return null;
        }

        var movements = db.StockMovements.Where(m => m.ProductId == p.Id)
            .Select(m => new { m.QuantityDelta, m.Utc }).AsEnumerable().ToList();
        var stock = movements.Sum(m => m.QuantityDelta);
        var last = movements.Count > 0 ? movements.Max(m => m.Utc) : (DateTimeOffset?)null;
        var categoryName = db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault() ?? "—";
        var status = InventoryMath.Classify(p.IsActive, p.TrackInventory, stock, p.ReorderLevel);
        return new ProductListItem(
            p.Id, p.Name, p.Sku, p.Barcode, p.CategoryId, categoryName,
            p.Brand, p.Variant, p.Size, p.Unit, p.Cost, p.Price,
            p.TrackInventory, p.ReorderLevel, p.IsActive, stock, status, p.ImagePath, last);
    }

    public OperationResult<int> Create(CreateProductRequest request, int actorUserId, int? shiftId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageProducts) is { } denied)
        {
            return OperationResult<int>.Failure(denied);
        }

        var errors = ProductValidation.Validate(
            request.Name, request.Sku, request.Price, request.Cost, request.ReorderLevel, request.OpeningQuantity);
        if (errors.Count > 0)
        {
            return OperationResult<int>.Failure(errors);
        }

        var sku = request.Sku.Trim();
        var barcode = ProductValidation.NormalizeBarcode(request.Barcode);

        if (SkuExists(db, sku, excludeId: null))
        {
            return OperationResult<int>.Failure($"The SKU '{sku}' is already used by another product.");
        }

        if (barcode is not null && BarcodeExists(db, barcode, excludeId: null))
        {
            return OperationResult<int>.Failure($"The barcode '{barcode}' is already assigned to another product.");
        }

        if (!db.Categories.Any(c => c.Id == request.CategoryId))
        {
            return OperationResult<int>.Failure("Please choose a valid category.");
        }

        var now = _clock.UtcNow;
        using var tx = db.Database.BeginTransaction();
        try
        {
            var product = new Product
            {
                Name = request.Name.Trim(),
                Sku = sku,
                Barcode = barcode,
                CategoryId = request.CategoryId,
                Brand = Clean(request.Brand),
                Variant = Clean(request.Variant),
                Size = Clean(request.Size),
                Unit = request.Unit,
                Cost = request.Cost,
                Price = request.Price,
                TrackInventory = request.TrackInventory,
                AllowNegativeStock = request.AllowNegativeStock,
                ReorderLevel = request.ReorderLevel,
                IsActive = request.IsActive,
                ImagePath = request.ImagePath,
                ImageHash = request.ImageHash,
                ImageOriginalName = request.ImageOriginalName,
                Notes = Clean(request.Notes),
                CreatedUtc = now,
                UpdatedUtc = now,
            };
            db.Products.Add(product);
            db.SaveChanges();

            WriteAudit(db, AuditActions.ProductCreated, actorUserId, product.Id,
                $"Product '{product.Name}' created (SKU {product.Sku}, {product.Price.Format()}).");

            if (request.TrackInventory && request.OpeningQuantity > 0)
            {
                var opening = InventoryService.Append(
                    db, product, StockMovementType.OpeningStock, request.OpeningQuantity,
                    "Opening stock", actorUserId, shiftId, null, nowOverride: now);
                if (opening.Failed)
                {
                    tx.Rollback();
                    return OperationResult<int>.Failure(opening.ErrorMessage);
                }
            }

            db.SaveChanges();
            tx.Commit();
            return OperationResult<int>.Success(product.Id);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Creating a product failed and was rolled back.");
            return OperationResult<int>.Failure("Something went wrong while saving. No product was created. Please try again.");
        }
    }

    public OperationResult Update(UpdateProductRequest request, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageProducts) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var errors = ProductValidation.Validate(
            request.Name, request.Sku, request.Price, request.Cost, request.ReorderLevel, 0m);
        if (errors.Count > 0)
        {
            return OperationResult.Failure(errors);
        }

        var product = db.Products.FirstOrDefault(p => p.Id == request.Id);
        if (product is null)
        {
            return OperationResult.Failure("That product was not found.");
        }

        var sku = request.Sku.Trim();
        var barcode = ProductValidation.NormalizeBarcode(request.Barcode);

        if (SkuExists(db, sku, excludeId: product.Id))
        {
            return OperationResult.Failure($"The SKU '{sku}' is already used by another product.");
        }

        if (barcode is not null && BarcodeExists(db, barcode, excludeId: product.Id))
        {
            return OperationResult.Failure($"The barcode '{barcode}' is already assigned to another product.");
        }

        if (!db.Categories.Any(c => c.Id == request.CategoryId))
        {
            return OperationResult.Failure("Please choose a valid category.");
        }

        var now = _clock.UtcNow;
        var oldImagePath = product.ImagePath;

        // Specific audits for financially/identity-significant changes.
        if (product.Price != request.Price)
        {
            WriteAudit(db, AuditActions.ProductPriceChanged, actorUserId, product.Id,
                $"Price for '{product.Name}' changed {product.Price.Format()} → {request.Price.Format()}.");
        }

        if (product.Cost != request.Cost)
        {
            WriteAudit(db, AuditActions.ProductCostChanged, actorUserId, product.Id,
                $"Cost for '{product.Name}' changed {Describe(product.Cost)} → {Describe(request.Cost)}.");
        }

        if (!string.Equals(product.Barcode, barcode, StringComparison.Ordinal))
        {
            WriteAudit(db, AuditActions.ProductBarcodeChanged, actorUserId, product.Id,
                $"Barcode for '{product.Name}' changed {product.Barcode ?? "(none)"} → {barcode ?? "(none)"}.");
        }

        product.Name = request.Name.Trim();
        product.Sku = sku;
        product.Barcode = barcode;
        product.CategoryId = request.CategoryId;
        product.Brand = Clean(request.Brand);
        product.Variant = Clean(request.Variant);
        product.Size = Clean(request.Size);
        product.Unit = request.Unit;
        product.Cost = request.Cost;
        product.Price = request.Price;
        product.TrackInventory = request.TrackInventory;
        product.AllowNegativeStock = request.AllowNegativeStock;
        product.ReorderLevel = request.ReorderLevel;
        product.Notes = Clean(request.Notes);
        product.ImagePath = request.ImagePath;
        product.ImageHash = request.ImageHash;
        product.ImageOriginalName = request.ImageOriginalName;
        product.UpdatedUtc = now;

        WriteAudit(db, AuditActions.ProductUpdated, actorUserId, product.Id, $"Product '{product.Name}' updated.");
        db.SaveChanges();

        // Clean up a replaced image only when no other product still references the file.
        if (!string.Equals(oldImagePath, product.ImagePath, StringComparison.Ordinal) && oldImagePath is not null)
        {
            var stillReferenced = db.Products.Where(p => p.ImagePath != null).Select(p => p.ImagePath).ToList();
            _images.DeleteIfUnreferenced(oldImagePath, stillReferenced);
        }

        return OperationResult.Success();
    }

    public OperationResult<int> Duplicate(int sourceId, string newSku, string? newBarcode, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        var source = db.Products.AsNoTracking().FirstOrDefault(p => p.Id == sourceId);
        if (source is null)
        {
            return OperationResult<int>.Failure("That product was not found.");
        }

        var request = new CreateProductRequest(
            Name: source.Name,
            Sku: newSku,
            Barcode: newBarcode,
            CategoryId: source.CategoryId,
            Brand: source.Brand,
            Variant: source.Variant,
            Size: source.Size,
            Unit: source.Unit,
            Cost: source.Cost,
            Price: source.Price,
            TrackInventory: source.TrackInventory,
            ReorderLevel: source.ReorderLevel,
            OpeningQuantity: 0m,
            AllowNegativeStock: source.AllowNegativeStock,
            IsActive: source.IsActive,
            Notes: source.Notes,
            ImagePath: source.ImagePath,
            ImageHash: source.ImageHash,
            ImageOriginalName: source.ImageOriginalName);

        return Create(request, actorUserId, null);
    }

    public OperationResult SetActive(int id, bool active, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageProducts) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var product = db.Products.FirstOrDefault(p => p.Id == id);
        if (product is null)
        {
            return OperationResult.Failure("That product was not found.");
        }

        if (product.IsActive == active)
        {
            return OperationResult.Success();
        }

        product.IsActive = active;
        product.UpdatedUtc = _clock.UtcNow;
        WriteAudit(db,
            active ? AuditActions.ProductActivated : AuditActions.ProductDeactivated,
            actorUserId, product.Id,
            $"Product '{product.Name}' {(active ? "activated" : "deactivated")}.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    private static bool SkuExists(SnookerPointDbContext db, string sku, int? excludeId)
    {
        var normalized = sku.ToLower();
        return db.Products.Any(p => p.Sku.ToLower() == normalized && (excludeId == null || p.Id != excludeId));
    }

    private static bool BarcodeExists(SnookerPointDbContext db, string barcode, int? excludeId) =>
        db.Products.Any(p => p.Barcode == barcode && (excludeId == null || p.Id != excludeId));

    private string? Guard(SnookerPointDbContext db, int actorUserId, Permission permission)
    {
        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        return actor is not null && _permissions.HasPermission(actor, permission)
            ? null
            : "You do not have permission to manage products.";
    }

    private static string Describe(Money? cost) => cost?.Format() ?? "(none)";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void WriteAudit(SnookerPointDbContext db, string action, int actorUserId, int productId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(Product),
            EntityId = productId.ToString(),
            Details = details,
        });
    }
}
