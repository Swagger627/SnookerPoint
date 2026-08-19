using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Settings;

/// <summary>A read-only view of the global billing settings.</summary>
public sealed record BillingSettingsView(
    BillingMethod Method,
    int RoundingIncrementMinutes,
    int MinimumBillableMinutes,
    int GracePeriodMinutes)
{
    public BillingPolicy ToPolicy() =>
        new(Method, RoundingIncrementMinutes, MinimumBillableMinutes, GracePeriodMinutes);

    public string Summary() => ToPolicy().Summary();
}

/// <summary>Reads and updates the global billing settings.</summary>
public interface IBillingSettingsService
{
    BillingSettingsView Get();

    OperationResult Update(
        BillingMethod method,
        int roundingIncrementMinutes,
        int minimumBillableMinutes,
        int gracePeriodMinutes,
        int actorUserId);
}
