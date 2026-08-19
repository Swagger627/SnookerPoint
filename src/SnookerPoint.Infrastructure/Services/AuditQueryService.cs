using Microsoft.EntityFrameworkCore;
using SnookerPoint.Application.Audit;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Reads audit events for the audit viewer. Records are append-only and never contain
/// secrets. SQLite cannot order/compare a DateTimeOffset column, so filtering by time is
/// done client-side; ordering uses the monotonic Id.
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;

    public AuditQueryService(IDbContextFactory<SnookerPointDbContext> factory)
    {
        _factory = factory;
    }

    public IReadOnlyList<AuditEventLine> GetRecent(int max = 200)
    {
        if (max <= 0)
        {
            max = 200;
        }

        using var db = _factory.CreateDbContext();
        var actors = ActorNames(db);
        return db.AuditEvents.AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(max)
            .ToList()
            .Select(e => Map(e, actors))
            .ToList();
    }

    public IReadOnlyList<AuditEventLine> Query(AuditFilter filter, int skip, int take)
    {
        if (take <= 0)
        {
            take = 100;
        }

        using var db = _factory.CreateDbContext();
        var actors = ActorNames(db);
        return Filtered(db, filter)
            .OrderByDescending(e => e.Id)
            .Skip(Math.Max(0, skip))
            .Take(take)
            .Select(e => Map(e, actors))
            .ToList();
    }

    public int Count(AuditFilter filter)
    {
        using var db = _factory.CreateDbContext();
        return Filtered(db, filter).Count;
    }

    public IReadOnlyList<string> GetActionNames()
    {
        using var db = _factory.CreateDbContext();
        return db.AuditEvents.AsNoTracking().Select(e => e.Action).Distinct().OrderBy(a => a).ToList();
    }

    public IReadOnlyList<string> GetModules()
    {
        using var db = _factory.CreateDbContext();
        return db.AuditEvents.AsNoTracking().Select(e => new { e.Entity, e.Action }).ToList()
            .Select(e => ModuleOf(e.Entity, e.Action))
            .Distinct()
            .OrderBy(m => m)
            .ToList();
    }

    public IReadOnlyList<(int UserId, string DisplayName)> GetActors()
    {
        using var db = _factory.CreateDbContext();
        var actorIds = db.AuditEvents.AsNoTracking().Where(e => e.ActorUserId != null)
            .Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var names = db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName }).ToList();
        return names.Select(n => (n.Id, n.DisplayName)).OrderBy(n => n.DisplayName).ToList();
    }

    private static List<AuditEvent> Filtered(SnookerPointDbContext db, AuditFilter filter)
    {
        var query = db.AuditEvents.AsNoTracking().AsQueryable();

        if (filter.ActorUserId is { } uid)
        {
            query = query.Where(e => e.ActorUserId == uid);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(e => e.Action == filter.Action);
        }

        if (!string.IsNullOrWhiteSpace(filter.Reference))
        {
            var term = filter.Reference.Trim();
            query = query.Where(e => e.EntityId != null && e.EntityId.Contains(term));
        }

        var list = query.ToList();

        if (filter.FromUtc is { } from)
        {
            list = list.Where(e => e.Utc >= from).ToList();
        }

        if (filter.ToUtc is { } to)
        {
            list = list.Where(e => e.Utc < to).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Module))
        {
            list = list.Where(e => ModuleOf(e.Entity, e.Action) == filter.Module).ToList();
        }

        return list;
    }

    private static Dictionary<int, string> ActorNames(SnookerPointDbContext db) =>
        db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);

    private static AuditEventLine Map(AuditEvent e, IReadOnlyDictionary<int, string> actors) =>
        new(e.Utc, e.Action, ModuleOf(e.Entity, e.Action), e.EntityId,
            e.ActorUserId is { } id ? actors.GetValueOrDefault(id) : null, e.Details);

    /// <summary>Groups an event under a friendly module for filtering, derived from its entity/action.</summary>
    private static string ModuleOf(string? entity, string action)
    {
        var key = entity ?? string.Empty;
        if (key.StartsWith("Sale", StringComparison.Ordinal) || action.StartsWith("Sale", StringComparison.Ordinal) || action.StartsWith("Receipt", StringComparison.Ordinal) || action.StartsWith("PaymentMethod", StringComparison.Ordinal))
        {
            return "Sales";
        }

        if (key.Contains("Booking", StringComparison.Ordinal) || action.Contains("Booking", StringComparison.Ordinal))
        {
            return "Bookings";
        }

        if (key.Contains("Shift", StringComparison.Ordinal) || action.Contains("Shift", StringComparison.Ordinal) || action.Contains("CashMovement", StringComparison.Ordinal))
        {
            return "Shifts";
        }

        if (key.Contains("Session", StringComparison.Ordinal) || key.Contains("Table", StringComparison.Ordinal) || action.Contains("Session", StringComparison.Ordinal) || action.Contains("Table", StringComparison.Ordinal) || action.Contains("Billing", StringComparison.Ordinal))
        {
            return "Tables";
        }

        if (key.Contains("Product", StringComparison.Ordinal) || key.Contains("Category", StringComparison.Ordinal) || action.Contains("Product", StringComparison.Ordinal) || action.Contains("Category", StringComparison.Ordinal) || action.Contains("Stock", StringComparison.Ordinal))
        {
            return "Inventory";
        }

        if (action.Contains("Staff", StringComparison.Ordinal) || action.Contains("Account", StringComparison.Ordinal) || action.Contains("Owner", StringComparison.Ordinal) || action.Contains("Login", StringComparison.Ordinal) || action.Contains("Logout", StringComparison.Ordinal) || action.Contains("LockedOut", StringComparison.Ordinal))
        {
            return "Security";
        }

        if (action.Contains("Backup", StringComparison.Ordinal) || action.Contains("Report", StringComparison.Ordinal) || action.Contains("Database", StringComparison.Ordinal) || action.Contains("Diagnostic", StringComparison.Ordinal) || action.Contains("Settings", StringComparison.Ordinal) || action.Contains("TaxService", StringComparison.Ordinal) || action.Contains("Setup", StringComparison.Ordinal))
        {
            return "Administration";
        }

        return "General";
    }
}
