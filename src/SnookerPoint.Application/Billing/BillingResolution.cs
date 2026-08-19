using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Billing;

/// <summary>
/// The single rule that turns a session's billing type into a charge. Hourly billing
/// uses the time-based charge from <see cref="ISessionBillingCalculator"/>; Fixed billing
/// ignores elapsed time and uses the snapshotted fixed amount. Approved charge
/// adjustments are added on top in both cases. Pure and side-effect free.
/// </summary>
public static class BillingResolution
{
    /// <summary>The base charge before any adjustments (fixed amount, or the hourly charge).</summary>
    public static Money BaseCharge(BillingType type, Money? fixedAmount, Money hourlyCharge) =>
        type == BillingType.Fixed ? fixedAmount ?? Money.Zero : hourlyCharge;

    /// <summary>The effective charge: base charge plus approved adjustments.</summary>
    public static Money EffectiveCharge(BillingType type, Money? fixedAmount, Money hourlyCharge, Money adjustments) =>
        BaseCharge(type, fixedAmount, hourlyCharge) + adjustments;
}
