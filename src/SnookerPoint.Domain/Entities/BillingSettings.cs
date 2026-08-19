using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// Single-row global billing configuration. A session snapshots these values when it
/// starts, so changing them later never alters an existing session.
/// </summary>
public sealed class BillingSettings
{
    /// <summary>Fixed primary key — only one settings row (Id = 1).</summary>
    public int Id { get; set; } = 1;

    public BillingMethod Method { get; set; } = BillingMethod.Exact;

    public int RoundingIncrementMinutes { get; set; } = 5;

    public int MinimumBillableMinutes { get; set; }

    public int GracePeriodMinutes { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public BillingPolicy ToPolicy() =>
        new(Method, RoundingIncrementMinutes, MinimumBillableMinutes, GracePeriodMinutes);
}
