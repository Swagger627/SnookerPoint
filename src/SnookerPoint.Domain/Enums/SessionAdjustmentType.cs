namespace SnookerPoint.Domain.Enums;

/// <summary>The kind of audited correction applied to a session.</summary>
public enum SessionAdjustmentType
{
    /// <summary>Corrected the recorded start time.</summary>
    StartTimeCorrection = 0,

    /// <summary>Corrected a pause or resume timestamp.</summary>
    PauseResumeCorrection = 1,

    /// <summary>Corrected the hourly rate snapshot of a segment.</summary>
    RateCorrection = 2,

    /// <summary>An approved monetary adjustment to the final charge.</summary>
    ChargeAdjustment = 3,

    /// <summary>Voided a session created by mistake.</summary>
    Void = 4,

    /// <summary>Corrected the fixed-amount snapshot of a fixed-billing session.</summary>
    FixedAmountCorrection = 5,

    /// <summary>Changed the session's billing type (Hourly ↔ Fixed).</summary>
    BillingTypeCorrection = 6,
}
