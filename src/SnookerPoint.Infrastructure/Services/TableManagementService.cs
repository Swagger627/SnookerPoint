using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Configures playing tables (add/rename/type/rate/activate). Requires the
/// <see cref="Permission.ManageTables"/> capability, saves the whole layout in one
/// transaction, never hard-deletes, and audits every change. Editing a table's rate
/// only affects future sessions — live and completed sessions keep their snapshotted
/// segment rate, so this service never touches session data.
/// </summary>
public sealed class TableManagementService : ITableManagementService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<TableManagementService> _logger;

    public TableManagementService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<TableManagementService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<TableListItem> GetAll()
    {
        using var db = _factory.CreateDbContext();

        var tables = db.PoolTables.AsNoTracking()
            .OrderByDescending(t => t.IsActive)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToList();

        var inUseTableIds = db.TableSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
            .Select(s => s.CurrentTableId)
            .Distinct()
            .ToHashSet();

        return tables
            .Select(t => new TableListItem(
                t.Id, t.Name, t.Type, t.HourlyRate, t.IsActive, inUseTableIds.Contains(t.Id), t.SortOrder))
            .ToList();
    }

    public OperationResult SaveLayout(IReadOnlyList<TableDraft> drafts, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (actor is null || !_permissions.HasPermission(actor, Permission.ManageTables))
        {
            return OperationResult.Failure("You do not have permission to manage tables.");
        }

        var existing = db.PoolTables.ToList();
        var inUseTableIds = db.TableSessions
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
            .Select(s => s.CurrentTableId)
            .Distinct()
            .ToHashSet();

        var errors = Validate(drafts, existing, inUseTableIds);
        if (errors.Count > 0)
        {
            return OperationResult.Failure(errors);
        }

        var now = _clock.UtcNow;
        var byId = existing.ToDictionary(t => t.Id);
        var nextSortOrder = existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder) + 1;

        using var tx = db.Database.BeginTransaction();
        try
        {
            foreach (var draft in drafts)
            {
                var name = draft.Name.Trim();

                if (draft.Id is null)
                {
                    var table = new PoolTable
                    {
                        Name = name,
                        Type = draft.Type,
                        HourlyRate = draft.HourlyRate,
                        IsActive = draft.IsActive,
                        SortOrder = nextSortOrder++,
                        CreatedUtc = now,
                        UpdatedUtc = now,
                    };
                    db.PoolTables.Add(table);
                    db.SaveChanges();
                    WriteAudit(db, AuditActions.TableAdded, actorUserId, table.Id,
                        $"Table '{table.Name}' added ({table.Type}, {table.HourlyRate.Format()}/hr).");
                    continue;
                }

                if (!byId.TryGetValue(draft.Id.Value, out var current))
                {
                    // A referenced table no longer exists — treat as a validation failure.
                    tx.Rollback();
                    return OperationResult.Failure("One of the tables could not be found. Please reload and try again.");
                }

                UpdateExisting(db, current, draft, name, actorUserId, now);
            }

            db.SaveChanges();
            tx.Commit();
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Saving the table layout failed and was rolled back.");
            return OperationResult.Failure("Something went wrong while saving. No changes were saved. Please try again.");
        }
    }

    private void UpdateExisting(SnookerPointDbContext db, PoolTable current, TableDraft draft, string name, int actorUserId, DateTimeOffset now)
    {
        var changes = new List<string>();

        if (!string.Equals(current.Name, name, StringComparison.Ordinal))
        {
            changes.Add($"name '{current.Name}' → '{name}'");
            current.Name = name;
        }

        if (current.Type != draft.Type)
        {
            changes.Add($"type {current.Type} → {draft.Type}");
            current.Type = draft.Type;
        }

        if (current.HourlyRate != draft.HourlyRate)
        {
            changes.Add($"rate {current.HourlyRate.Format()} → {draft.HourlyRate.Format()}");
            current.HourlyRate = draft.HourlyRate;
        }

        var activationChanged = current.IsActive != draft.IsActive;
        if (activationChanged)
        {
            current.IsActive = draft.IsActive;
        }

        if (changes.Count == 0 && !activationChanged)
        {
            return;
        }

        current.UpdatedUtc = now;

        if (changes.Count > 0)
        {
            WriteAudit(db, AuditActions.TableUpdated, actorUserId, current.Id,
                $"Table '{current.Name}' updated: {string.Join(", ", changes)}.");
        }

        if (activationChanged)
        {
            WriteAudit(db,
                current.IsActive ? AuditActions.TableActivated : AuditActions.TableDeactivated,
                actorUserId, current.Id,
                $"Table '{current.Name}' {(current.IsActive ? "activated" : "deactivated")}.");
        }
    }

    private static List<string> Validate(
        IReadOnlyList<TableDraft> drafts, List<PoolTable> existing, HashSet<int> inUseTableIds)
    {
        var errors = new List<string>();
        var byId = existing.ToDictionary(t => t.Id);

        if (drafts.Any(d => d.HourlyRate.IsNegative))
        {
            errors.Add("Table rates cannot be negative.");
        }

        var activeDrafts = drafts.Where(d => d.IsActive).ToList();

        if (activeDrafts.Count == 0)
        {
            errors.Add("Please keep at least one table active.");
        }

        if (activeDrafts.Any(d => string.IsNullOrWhiteSpace(d.Name)))
        {
            errors.Add("Every active table needs a name.");
        }

        var duplicateNames = activeDrafts
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .GroupBy(d => d.Name.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.First().Name.Trim())
            .ToList();
        if (duplicateNames.Count > 0)
        {
            errors.Add($"Active table names must be different. Duplicate: {string.Join(", ", duplicateNames)}.");
        }

        // A table currently in use cannot be deactivated.
        foreach (var draft in drafts)
        {
            if (draft.Id is { } id && byId.TryGetValue(id, out var current)
                && current.IsActive && !draft.IsActive && inUseTableIds.Contains(id))
            {
                errors.Add($"'{current.Name}' is in use right now and cannot be deactivated. Finish its session first.");
            }
        }

        return errors;
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int actorUserId, int tableId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(PoolTable),
            EntityId = tableId.ToString(),
            Details = details,
        });
    }
}
