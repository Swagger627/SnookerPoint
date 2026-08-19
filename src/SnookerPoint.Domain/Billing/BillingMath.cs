using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Billing;

/// <summary>
/// Pure, deterministic billing arithmetic on integer seconds and integer paisa.
/// No binary floating-point is used for money; the only proportional step uses
/// <see cref="decimal"/> (base-10) and rounds to the nearest paisa away from zero.
///
/// Calculation order (documented and tested):
///   1. Grace   — subtract the grace period from the active (billable) seconds.
///   2. Minimum — raise the result up to the minimum billable duration.
///   3. Rounding— round the chargeable duration up to the increment (or leave exact).
/// </summary>
public static class BillingMath
{
    private const decimal SecondsPerHour = 3600m;

    /// <summary>Step 1: remove the grace period (never below zero).</summary>
    public static long ApplyGrace(long activeSeconds, long graceSeconds) =>
        Math.Max(0, activeSeconds - graceSeconds);

    /// <summary>Step 2: raise up to the minimum billable duration.</summary>
    public static long ApplyMinimum(long seconds, long minimumSeconds) =>
        Math.Max(seconds, minimumSeconds);

    /// <summary>Step 3: round the chargeable duration up to the increment (exact leaves it).</summary>
    public static long ApplyRounding(long seconds, BillingMethod method, long incrementSeconds)
    {
        if (method == BillingMethod.Exact || incrementSeconds <= 0 || seconds <= 0)
        {
            return seconds;
        }

        // Ceiling division to the next whole increment.
        return ((seconds + incrementSeconds - 1) / incrementSeconds) * incrementSeconds;
    }

    /// <summary>Applies grace, then minimum, then rounding to give the chargeable seconds.</summary>
    public static long ChargeableSeconds(long activeSeconds, BillingPolicy policy)
    {
        var afterGrace = ApplyGrace(activeSeconds, policy.GraceSeconds);
        var afterMinimum = ApplyMinimum(afterGrace, policy.MinimumSeconds);
        return ApplyRounding(afterMinimum, policy.Method, policy.RoundingIncrementSeconds);
    }

    /// <summary>The money charged for a duration at an hourly rate, rounded to the nearest paisa.</summary>
    public static Money ChargeForDurationAtRate(long seconds, Money hourlyRate)
    {
        if (seconds <= 0)
        {
            return Money.Zero;
        }

        var paisa = (decimal)seconds / SecondsPerHour * hourlyRate.Paisa;
        return Money.FromPaisa((long)decimal.Round(paisa, 0, MidpointRounding.AwayFromZero));
    }
}
