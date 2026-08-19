namespace SnookerPoint.Domain.Entities;

/// <summary>
/// An append-only audit record. Never stores secrets (passwords, PINs, hashes).
/// The actor is optional because some events — such as a failed login for an
/// unknown username — have no authenticated user.
/// </summary>
public sealed class AuditEvent
{
    public int Id { get; set; }

    public DateTimeOffset Utc { get; set; }

    /// <summary>The action name (see <see cref="AuditActions"/>).</summary>
    public string Action { get; set; } = string.Empty;

    public int? ActorUserId { get; set; }
    public User? Actor { get; set; }

    /// <summary>Optional entity type the event relates to (e.g. "Shift", "User").</summary>
    public string? Entity { get; set; }

    /// <summary>Optional identifier of the related entity.</summary>
    public string? EntityId { get; set; }

    /// <summary>Optional human-readable detail. Must never contain secrets.</summary>
    public string? Details { get; set; }
}
