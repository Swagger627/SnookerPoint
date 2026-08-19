namespace SnookerPoint.Domain.Enums;

/// <summary>
/// The lifecycle of a table reservation. Scheduled, CheckedIn and Started block the table's
/// time slot; Completed, Cancelled and NoShow do not.
/// </summary>
public enum BookingStatus
{
    Scheduled = 0,
    CheckedIn = 1,
    Started = 2,
    Completed = 3,
    Cancelled = 4,
    NoShow = 5,
}
