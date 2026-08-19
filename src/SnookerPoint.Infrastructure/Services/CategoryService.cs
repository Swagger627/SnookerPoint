using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Manages product categories. Requires <see cref="Permission.ManageProducts"/>. Active
/// names are unique case-insensitively, a category with products is deactivated rather
/// than deleted, and every change is audited.
/// </summary>
public sealed class CategoryService : ICategoryService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<CategoryService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<CategoryListItem> GetAll(bool includeInactive = true)
    {
        using var db = _factory.CreateDbContext();

        var counts = db.Products.AsNoTracking()
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.CategoryId, x => x.Count);

        var query = db.Categories.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return query
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToList()
            .Select(c => new CategoryListItem(
                c.Id, c.Name, c.SortOrder, c.IsActive, counts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public OperationResult<int> Create(string name, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId) is { } denied)
        {
            return OperationResult<int>.Failure(denied);
        }

        if (ProductValidation.ValidateCategoryName(name) is { } error)
        {
            return OperationResult<int>.Failure(error);
        }

        var clean = name.Trim();
        if (ActiveNameClashes(db, clean, excludeId: null))
        {
            return OperationResult<int>.Failure($"A category named '{clean}' already exists.");
        }

        var now = _clock.UtcNow;
        var nextOrder = db.Categories.Any() ? db.Categories.Max(c => c.SortOrder) + 1 : 0;
        var category = new Category
        {
            Name = clean,
            SortOrder = nextOrder,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        db.Categories.Add(category);
        db.SaveChanges();

        WriteAudit(db, AuditActions.CategoryCreated, actorUserId, category.Id, $"Category '{clean}' created.");
        db.SaveChanges();
        return OperationResult<int>.Success(category.Id);
    }

    public OperationResult Update(int id, string name, int sortOrder, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        if (ProductValidation.ValidateCategoryName(name) is { } error)
        {
            return OperationResult.Failure(error);
        }

        var category = db.Categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
        {
            return OperationResult.Failure("That category was not found.");
        }

        var clean = name.Trim();
        if (category.IsActive && ActiveNameClashes(db, clean, excludeId: id))
        {
            return OperationResult.Failure($"A category named '{clean}' already exists.");
        }

        var changes = new List<string>();
        if (!string.Equals(category.Name, clean, StringComparison.Ordinal))
        {
            changes.Add($"name '{category.Name}' → '{clean}'");
            category.Name = clean;
        }

        if (category.SortOrder != sortOrder)
        {
            changes.Add($"order {category.SortOrder} → {sortOrder}");
            category.SortOrder = sortOrder;
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success();
        }

        category.UpdatedUtc = _clock.UtcNow;
        WriteAudit(db, AuditActions.CategoryUpdated, actorUserId, category.Id,
            $"Category updated: {string.Join(", ", changes)}.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult SetActive(int id, bool active, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var category = db.Categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
        {
            return OperationResult.Failure("That category was not found.");
        }

        if (category.IsActive == active)
        {
            return OperationResult.Success();
        }

        // Reactivating must not collide with another active category of the same name.
        if (active && ActiveNameClashes(db, category.Name, excludeId: id))
        {
            return OperationResult.Failure($"A category named '{category.Name}' is already active.");
        }

        category.IsActive = active;
        category.UpdatedUtc = _clock.UtcNow;

        WriteAudit(db,
            active ? AuditActions.CategoryActivated : AuditActions.CategoryDeactivated,
            actorUserId, category.Id,
            $"Category '{category.Name}' {(active ? "activated" : "deactivated")}.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    private static bool ActiveNameClashes(SnookerPointDbContext db, string name, int? excludeId)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return db.Categories.Any(c =>
            c.IsActive &&
            (excludeId == null || c.Id != excludeId) &&
            c.Name.ToLower() == normalized);
    }

    private string? Guard(SnookerPointDbContext db, int actorUserId)
    {
        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        return actor is not null && _permissions.HasPermission(actor, Permission.ManageProducts)
            ? null
            : "You do not have permission to manage products.";
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int actorUserId, int categoryId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(Category),
            EntityId = categoryId.ToString(),
            Details = details,
        });
    }
}
