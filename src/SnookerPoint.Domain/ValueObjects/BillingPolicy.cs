using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.ValueObjects;

/// <summary>
/// The billing rules that turn billable time into a chargeable duration. Snapshotted
/// onto a session when it starts, so later settings changes never alter an existing
/// session. Durations are whole minutes; all derived maths uses integer seconds.
/// </summary>
public readonly record struct BillingPolicy(
    BillingMethod Method,
    int RoundingIncrementMinutes,
    int MinimumBillableMinutes,
    int GracePeriodMinutes)
{
    /// <summary>A sensible default: exact billing, no minimum, no grace.</summary>
    public static BillingPolicy Default => new(BillingMethod.Exact, 5, 0, 0);

    public long GraceSeconds => (long)GracePeriodMinutes * 60;
    public long MinimumSeconds => (long)MinimumBillableMinutes * 60;
    public long RoundingIncrementSeconds => (long)RoundingIncrementMinutes * 60;

    /// <summary>Validates the policy, returning friendly errors (empty when valid).</summary>
    public static IReadOnlyList<string> Validate(
        BillingMethod method,
        int roundingIncrementMinutes,
        int minimumBillableMinutes,
        int gracePeriodMinutes)
    {
        var errors = new List<string>();

        if (method == BillingMethod.RoundUp && roundingIncrementMinutes <= 0)
        {
            errors.Add("The rounding increment must be greater than zero.");
        }

        if (minimumBillableMinutes < 0)
        {
            errors.Add("Minimum billable minutes cannot be negative.");
        }

        if (gracePeriodMinutes < 0)
        {
            errors.Add("The grace period cannot be negative.");
        }

        return errors;
    }

    public string Summary()
    {
        var basis = Method == BillingMethod.Exact
            ? "Exact time"
            : $"Round up to {RoundingIncrementMinutes} min";
        var extras = new List<string>();
        if (GracePeriodMinutes > 0)
        {
            extras.Add($"{GracePeriodMinutes} min grace");
        }

        if (MinimumBillableMinutes > 0)
        {
            extras.Add($"min {MinimumBillableMinutes} min");
        }

        return extras.Count == 0 ? basis : $"{basis} · {string.Join(" · ", extras)}";
    }
}
