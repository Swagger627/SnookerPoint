namespace SnookerPoint.Application.Audit;

/// <summary>A single audit record for display (never contains secrets).</summary>
public sealed record AuditEventLine(
    DateTimeOffset Utc,
    string Action,
    string Module,
    string? Reference,
    string? ActorDisplayName,
    string? Details);

/// <summary>Filter for the audit viewer (all optional).</summary>
public sealed record AuditFilter(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int? ActorUserId = null,
    string? Action = null,
    string? Module = null,
    string? Reference = null);

/// <summary>Reads audit events for the audit viewer, with filtering and incremental loading.</summary>
public interface IAuditQueryService
{
    /// <summary>The most recent events (used by the Home advanced view).</summary>
    IReadOnlyList<AuditEventLine> GetRecent(int max = 200);

    /// <summary>A page of filtered events, newest first.</summary>
    IReadOnlyList<AuditEventLine> Query(AuditFilter filter, int skip, int take);

    /// <summary>The total number of events matching a filter (for paging).</summary>
    int Count(AuditFilter filter);

    /// <summary>Distinct action names present in the log (for the filter dropdown).</summary>
    IReadOnlyList<string> GetActionNames();

    /// <summary>Distinct modules present in the log (for the filter dropdown).</summary>
    IReadOnlyList<string> GetModules();

    /// <summary>Users who have appeared as an actor (for the filter dropdown).</summary>
    IReadOnlyList<(int UserId, string DisplayName)> GetActors();
}
