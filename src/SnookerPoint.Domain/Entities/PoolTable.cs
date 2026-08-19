using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A physical playing table's configuration. Phase 1 captures the configuration
/// only; live timing/charging is a later phase.
/// </summary>
public sealed class PoolTable
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public TableType Type { get; set; } = TableType.Snooker;

    /// <summary>Hourly play rate, stored as integer minor units (paisa).</summary>
    public Money HourlyRate { get; set; } = Money.Zero;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
