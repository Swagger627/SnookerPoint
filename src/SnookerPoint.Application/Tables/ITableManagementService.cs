using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Tables;

/// <summary>A table as shown on the Manage Tables screen (active and inactive).</summary>
public sealed record TableListItem(
    int Id,
    string Name,
    TableType Type,
    Money HourlyRate,
    bool IsActive,
    bool InUse,
    int SortOrder);

/// <summary>
/// One proposed table in a Save. A null <see cref="Id"/> is a brand-new table; an
/// existing id updates that table. Deletion is never expressed here — a table with
/// history is deactivated (IsActive = false), never removed.
/// </summary>
public sealed record TableDraft(
    int? Id,
    string Name,
    TableType Type,
    Money HourlyRate,
    bool IsActive);

/// <summary>
/// Configures playing tables for Owner/Administrator/Manager users: add, rename,
/// change type and hourly rate, and activate/deactivate. Rate changes take effect for
/// new sessions only — running, paused and completed sessions keep their snapshotted
/// rate. Every change is audited; nothing is ever hard-deleted.
/// </summary>
public interface ITableManagementService
{
    /// <summary>All tables, active first then by sort order, with their in-use state.</summary>
    IReadOnlyList<TableListItem> GetAll();

    /// <summary>
    /// Validates and persists the whole table layout in one transaction: new tables are
    /// added, existing ones updated, and activation toggled. Rejects negative rates,
    /// duplicate active names, blank active names, and deactivating a table that is
    /// currently in use. On any failure nothing is saved.
    /// </summary>
    OperationResult SaveLayout(IReadOnlyList<TableDraft> drafts, int actorUserId);
}
