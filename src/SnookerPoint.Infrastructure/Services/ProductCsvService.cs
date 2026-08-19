using System.Globalization;
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
/// CSV import/export for the product catalogue and inventory. Import is preview-then-commit:
/// the preview validates every row without writing; the commit is transactional and, if any
/// row is invalid or a failure occurs, nothing is written. Opening quantities create Opening
/// Stock movements. Local prices are only changed under an explicit update strategy.
/// </summary>
public sealed class ProductCsvService : IProductCsvService
{
    private static readonly string[] Columns =
    {
        "SKU", "Barcode", "ProductName", "Category", "Brand", "Variant", "Size", "Unit",
        "PurchaseCost", "SellingPrice", "TrackInventory", "OpeningQuantity", "ReorderLevel", "Active",
    };

    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<ProductCsvService> _logger;

    public ProductCsvService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<ProductCsvService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public string Template() => Csv.Line(Columns) +
        Csv.Line("SNK-COLA-330", "8961000000017", "Cola 330 ml", "Drinks", "CoolCo", "Regular", "330 ml",
            "Bottle", "35", "60", "true", "24", "6", "true");

    public string ExportProducts()
    {
        using var db = _factory.CreateDbContext();
        var categories = db.Categories.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);
        var stock = db.StockMovements.AsNoTracking()
            .Select(m => new { m.ProductId, m.QuantityDelta })
            .AsEnumerable()
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityDelta));

        var sb = new System.Text.StringBuilder();
        sb.Append(Csv.Line(Columns));
        foreach (var p in db.Products.AsNoTracking().OrderBy(p => p.Sku).ToList())
        {
            sb.Append(Csv.Line(
                p.Sku,
                p.Barcode,
                p.Name,
                categories.GetValueOrDefault(p.CategoryId, string.Empty),
                p.Brand,
                p.Variant,
                p.Size,
                p.Unit.ToString(),
                p.Cost is { } c ? c.ToRupees().ToString(CultureInfo.InvariantCulture) : string.Empty,
                p.Price.ToRupees().ToString(CultureInfo.InvariantCulture),
                p.TrackInventory ? "true" : "false",
                stock.GetValueOrDefault(p.Id).ToString(CultureInfo.InvariantCulture),
                p.ReorderLevel.ToString(CultureInfo.InvariantCulture),
                p.IsActive ? "true" : "false"));
        }

        return sb.ToString();
    }

    public string ExportStockSummary()
    {
        using var db = _factory.CreateDbContext();
        var categories = db.Categories.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);
        var stock = db.StockMovements.AsNoTracking()
            .Select(m => new { m.ProductId, m.QuantityDelta })
            .AsEnumerable()
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityDelta));

        var sb = new System.Text.StringBuilder();
        sb.Append(Csv.Line("SKU", "ProductName", "Category", "CurrentStock", "ReorderLevel", "Status", "Price"));
        foreach (var p in db.Products.AsNoTracking().OrderBy(p => p.Name).ToList())
        {
            var qty = stock.GetValueOrDefault(p.Id);
            var status = InventoryMath.Classify(p.IsActive, p.TrackInventory, qty, p.ReorderLevel);
            sb.Append(Csv.Line(
                p.Sku, p.Name, categories.GetValueOrDefault(p.CategoryId, string.Empty),
                qty.ToString(CultureInfo.InvariantCulture),
                p.ReorderLevel.ToString(CultureInfo.InvariantCulture),
                status.ToString(),
                p.Price.ToRupees().ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    public string ExportStockHistory()
    {
        using var db = _factory.CreateDbContext();
        var products = db.Products.AsNoTracking().ToDictionary(p => p.Id, p => (p.Sku, p.Name));
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);

        var sb = new System.Text.StringBuilder();
        sb.Append(Csv.Line("SKU", "ProductName", "Utc", "Type", "QuantityDelta", "PreviousQuantity", "NewQuantity", "Reason", "User"));
        foreach (var m in db.StockMovements.AsNoTracking().AsEnumerable().OrderBy(m => m.Utc).ThenBy(m => m.Id).ToList())
        {
            var product = products.GetValueOrDefault(m.ProductId);
            sb.Append(Csv.Line(
                product.Sku, product.Name,
                m.Utc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                m.Type.ToString(),
                m.QuantityDelta.ToString(CultureInfo.InvariantCulture),
                m.PreviousQuantity.ToString(CultureInfo.InvariantCulture),
                m.NewQuantity.ToString(CultureInfo.InvariantCulture),
                m.Reason,
                users.GetValueOrDefault(m.ActorUserId, string.Empty)));
        }

        return sb.ToString();
    }

    public CsvImportPreview Preview(string csvContent)
    {
        using var db = _factory.CreateDbContext();
        return BuildPreview(db, csvContent);
    }

    public OperationResult<CsvImportResult> Import(string csvContent, CsvDuplicateStrategy strategy, int actorUserId, int? shiftId)
    {
        using var db = _factory.CreateDbContext();

        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (actor is null || !_permissions.HasPermission(actor, Permission.ImportProducts))
        {
            return OperationResult<CsvImportResult>.Failure("You do not have permission to import products.");
        }

        if (strategy == CsvDuplicateStrategy.Cancel)
        {
            return OperationResult<CsvImportResult>.Failure("Import cancelled. Nothing was imported.");
        }

        var preview = BuildPreview(db, csvContent);
        if (preview.HasFileErrors)
        {
            return OperationResult<CsvImportResult>.Failure(preview.FileErrors);
        }

        if (preview.InvalidCount > 0)
        {
            var lines = string.Join(", ", preview.Rows.Where(r => !r.IsValid).Select(r => $"line {r.LineNumber}"));
            return OperationResult<CsvImportResult>.Failure(
                $"Nothing was imported because {preview.InvalidCount} row(s) have problems ({lines}). Please fix them and try again.");
        }

        var header = ParseRows(csvContent, out var dataRows)!;
        var headerIndex = BuildHeaderIndex(header);
        var now = _clock.UtcNow;
        var added = 0;
        var updated = 0;
        var skipped = 0;

        using var tx = db.Database.BeginTransaction();
        try
        {
            foreach (var row in dataRows)
            {
                var parsed = ParseRow(MapFields(row, headerIndex));
                var sku = parsed.Sku;
                var barcode = parsed.Barcode;

                var existing = db.Products.FirstOrDefault(p => p.Sku.ToLower() == sku.ToLower());
                var byBarcode = barcode is not null
                    ? db.Products.FirstOrDefault(p => p.Barcode == barcode)
                    : null;

                if (existing is not null || byBarcode is not null)
                {
                    switch (strategy)
                    {
                        case CsvDuplicateStrategy.Skip:
                            skipped++;
                            continue;
                        case CsvDuplicateStrategy.UpdateBySku when existing is not null:
                            ApplyUpdate(db, existing, parsed, now);
                            updated++;
                            continue;
                        case CsvDuplicateStrategy.UpdateByBarcode when byBarcode is not null:
                            ApplyUpdate(db, byBarcode, parsed, now);
                            updated++;
                            continue;
                        default:
                            skipped++;
                            continue;
                    }
                }

                var categoryId = ResolveOrCreateCategory(db, parsed.Category, actorUserId, now);
                var product = new Product
                {
                    Name = parsed.Name,
                    Sku = sku,
                    Barcode = barcode,
                    CategoryId = categoryId,
                    Brand = parsed.Brand,
                    Variant = parsed.Variant,
                    Size = parsed.Size,
                    Unit = parsed.Unit,
                    Cost = parsed.Cost,
                    Price = parsed.Price,
                    TrackInventory = parsed.TrackInventory,
                    ReorderLevel = parsed.ReorderLevel,
                    IsActive = parsed.Active,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                };
                db.Products.Add(product);
                db.SaveChanges();
                added++;

                if (parsed.TrackInventory && parsed.OpeningQuantity > 0)
                {
                    InventoryService.Append(db, product, StockMovementType.OpeningStock, parsed.OpeningQuantity,
                        "Opening stock (CSV import)", actorUserId, shiftId, null, nowOverride: now);
                }
            }

            db.AuditEvents.Add(new AuditEvent
            {
                Utc = now,
                Action = AuditActions.ProductsImported,
                ActorUserId = actorUserId,
                Entity = nameof(Product),
                Details = $"CSV import: {added} added, {updated} updated, {skipped} skipped.",
            });

            db.SaveChanges();
            tx.Commit();
            return OperationResult<CsvImportResult>.Success(
                new CsvImportResult(added, updated, skipped, 0,
                    new[] { $"{added} added, {updated} updated, {skipped} skipped." }));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "CSV import failed and was rolled back.");
            return OperationResult<CsvImportResult>.Failure(
                "A problem occurred during import. Nothing was imported. Please check the file and try again.");
        }
    }

    // ---------------- preview / parsing ----------------

    private CsvImportPreview BuildPreview(SnookerPointDbContext db, string csvContent)
    {
        var fileErrors = new List<string>();
        var headerCells = ParseRows(csvContent, out var dataRows);

        if (headerCells is null)
        {
            fileErrors.Add("The file is empty.");
            return new CsvImportPreview(Array.Empty<string>(), Array.Empty<CsvRowPreview>(), fileErrors);
        }

        var headers = headerCells;
        var headerIndex = BuildHeaderIndex(headers);
        foreach (var required in new[] { "SKU", "ProductName", "SellingPrice" })
        {
            if (!headerIndex.ContainsKey(required.ToLowerInvariant()))
            {
                fileErrors.Add($"The file is missing the required column '{required}'.");
            }
        }

        if (fileErrors.Count > 0)
        {
            return new CsvImportPreview(headers, Array.Empty<CsvRowPreview>(), fileErrors);
        }

        var existingSkus = db.Products.AsNoTracking().Select(p => p.Sku.ToLower()).ToHashSet();
        var existingBarcodes = db.Products.AsNoTracking().Where(p => p.Barcode != null).Select(p => p.Barcode!).ToHashSet();

        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBarcodes = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<CsvRowPreview>();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var lineNumber = i + 2; // header is line 1
            var fields = MapFields(dataRows[i], headerIndex);
            var parsed = ParseRow(fields);
            var errors = new List<string>();

            errors.AddRange(ProductValidation.Validate(
                parsed.Name, parsed.Sku, parsed.Price, parsed.Cost, parsed.ReorderLevel, parsed.OpeningQuantity));

            foreach (var pe in parsed.Errors)
            {
                errors.Add(pe);
            }

            if (!string.IsNullOrWhiteSpace(parsed.Sku) && !seenSkus.Add(parsed.Sku))
            {
                errors.Add("Duplicate SKU within the file.");
            }

            if (parsed.Barcode is not null && !seenBarcodes.Add(parsed.Barcode))
            {
                errors.Add("Duplicate barcode within the file.");
            }

            var dupSku = !string.IsNullOrWhiteSpace(parsed.Sku) && existingSkus.Contains(parsed.Sku.ToLower());
            var dupBarcode = parsed.Barcode is not null && existingBarcodes.Contains(parsed.Barcode);

            rows.Add(new CsvRowPreview(
                lineNumber, parsed.Sku, parsed.Barcode, parsed.Name,
                errors.Count == 0, errors, dupSku, dupBarcode));
        }

        return new CsvImportPreview(headers, rows, fileErrors);
    }

    private static List<string>? ParseRows(string content, out List<List<string>> dataRows)
    {
        dataRows = new List<List<string>>();
        var all = Csv.Parse(content);
        if (all.Count == 0)
        {
            return null;
        }

        var header = all[0];
        for (var i = 1; i < all.Count; i++)
        {
            dataRows.Add(all[i]);
        }

        return header;
    }

    private static Dictionary<string, int> BuildHeaderIndex(List<string> headers)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < headers.Count; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static Dictionary<string, string> MapFields(List<string> row, Dictionary<string, int> headerIndex)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, index) in headerIndex)
        {
            fields[name] = index < row.Count ? row[index] : string.Empty;
        }

        return fields;
    }

    private static ParsedRow ParseRow(Dictionary<string, string> f)
    {
        string Get(string name) => f.TryGetValue(name.ToLowerInvariant(), out var v) ? v?.Trim() ?? string.Empty : string.Empty;

        var errors = new List<string>();

        var name = Get("ProductName");
        var sku = Get("SKU");
        var barcode = ProductValidation.NormalizeBarcode(Get("Barcode"));
        var category = Get("Category");
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "Uncategorised";
        }

        Money price = Money.Zero;
        if (!TryParseMoney(Get("SellingPrice"), out price))
        {
            errors.Add("The selling price is not a valid number.");
        }

        Money? cost = null;
        var costText = Get("PurchaseCost");
        if (!string.IsNullOrWhiteSpace(costText))
        {
            if (TryParseMoney(costText, out var c))
            {
                cost = c;
            }
            else
            {
                errors.Add("The purchase cost is not a valid number.");
            }
        }

        var reorder = ParseDecimal(Get("ReorderLevel"), 0m, errors, "reorder level");
        var opening = ParseDecimal(Get("OpeningQuantity"), 0m, errors, "opening quantity");
        var track = ParseBool(Get("TrackInventory"), true);
        var active = ParseBool(Get("Active"), true);
        var unit = ParseUnit(Get("Unit"));

        return new ParsedRow(
            name, sku, barcode, category, Blank(Get("Brand")), Blank(Get("Variant")), Blank(Get("Size")),
            unit, cost, price, track, opening, reorder, active, errors);
    }

    private int ResolveOrCreateCategory(SnookerPointDbContext db, string name, int actorUserId, DateTimeOffset now)
    {
        var normalized = name.Trim().ToLower();
        var existing = db.Categories.FirstOrDefault(c => c.Name.ToLower() == normalized && c.IsActive);
        if (existing is not null)
        {
            return existing.Id;
        }

        var nextOrder = db.Categories.Any() ? db.Categories.Max(c => c.SortOrder) + 1 : 0;
        var category = new Category
        {
            Name = name.Trim(),
            SortOrder = nextOrder,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        db.Categories.Add(category);
        db.SaveChanges();

        db.AuditEvents.Add(new AuditEvent
        {
            Utc = now,
            Action = AuditActions.CategoryCreated,
            ActorUserId = actorUserId,
            Entity = nameof(Category),
            EntityId = category.Id.ToString(),
            Details = $"Category '{category.Name}' created during CSV import.",
        });

        return category.Id;
    }

    private void ApplyUpdate(SnookerPointDbContext db, Product product, ParsedRow parsed, DateTimeOffset now)
    {
        var categoryId = ResolveOrCreateCategory(db, parsed.Category, product.CategoryId, now);
        product.Name = parsed.Name;
        product.CategoryId = categoryId;
        product.Brand = parsed.Brand;
        product.Variant = parsed.Variant;
        product.Size = parsed.Size;
        product.Unit = parsed.Unit;
        product.Cost = parsed.Cost;
        product.Price = parsed.Price;              // explicit update strategy: the price change is intended
        product.TrackInventory = parsed.TrackInventory;
        product.ReorderLevel = parsed.ReorderLevel;
        product.IsActive = parsed.Active;
        product.UpdatedUtc = now;
    }

    private static bool TryParseMoney(string text, out Money money)
    {
        money = Money.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var rupees))
        {
            money = Money.FromRupees(rupees);
            return true;
        }

        return false;
    }

    private static decimal ParseDecimal(string text, decimal fallback, List<string> errors, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add($"The {label} is not a valid number.");
        return fallback;
    }

    private static bool ParseBool(string text, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "1" => true,
            "false" or "no" or "n" or "0" => false,
            _ => fallback,
        };
    }

    private static ProductUnit ParseUnit(string text) =>
        Enum.TryParse<ProductUnit>(text.Trim(), ignoreCase: true, out var unit) ? unit : ProductUnit.Each;

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParsedRow(
        string Name,
        string Sku,
        string? Barcode,
        string Category,
        string? Brand,
        string? Variant,
        string? Size,
        ProductUnit Unit,
        Money? Cost,
        Money Price,
        bool TrackInventory,
        decimal OpeningQuantity,
        decimal ReorderLevel,
        bool Active,
        List<string> Errors);
}
