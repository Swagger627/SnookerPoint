namespace SnookerPoint.Domain.Enums;

/// <summary>
/// How the chargeable duration is derived from the billable seconds. The 5/10/15/30
/// minute presets and any custom value are expressed via the rounding increment;
/// the method only distinguishes exact billing from round-up billing.
/// </summary>
public enum BillingMethod
{
    /// <summary>Charge exactly for the billable time (proportional to the second).</summary>
    Exact = 0,

    /// <summary>Round the chargeable duration up to the next rounding increment.</summary>
    RoundUp = 1,
}
