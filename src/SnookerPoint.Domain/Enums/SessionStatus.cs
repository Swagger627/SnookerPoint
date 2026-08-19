namespace SnookerPoint.Domain.Enums;

/// <summary>Lifecycle state of a table session.</summary>
public enum SessionStatus
{
    /// <summary>Running and accruing billable time.</summary>
    Active = 0,

    /// <summary>Temporarily paused; billable time is not accruing.</summary>
    Paused = 1,

    /// <summary>Finished; final charge frozen, awaiting checkout.</summary>
    Completed = 2,

    /// <summary>Cancelled as a mistake via an audited correction; not billable.</summary>
    Voided = 3,
}
