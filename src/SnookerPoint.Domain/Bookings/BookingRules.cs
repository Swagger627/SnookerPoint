using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Bookings;

/// <summary>
/// Pure booking rules: which statuses block a table's time slot, whether two time ranges
/// overlap, whether a booking can be started or checked in, and No-Show eligibility. No
/// dependencies, so it is trivially testable and shared by the service and UI.
/// </summary>
public static class BookingRules
{
    /// <summary>True for statuses that reserve the table's slot (block conflicting bookings).</summary>
    public static bool Blocks(BookingStatus status) => status switch
    {
        BookingStatus.Scheduled => true,
        BookingStatus.CheckedIn => true,
        BookingStatus.Started => true,
        _ => false, // Completed, Cancelled, NoShow do not block
    };

    /// <summary>Half-open overlap test: [aStart, aEnd) intersects [bStart, bEnd).</summary>
    public static bool Overlaps(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd) =>
        aStart < bEnd && bStart < aEnd;

    /// <summary>A booking can be checked in while it is Scheduled.</summary>
    public static bool CanCheckIn(BookingStatus status) => status == BookingStatus.Scheduled;

    /// <summary>A booking can be started into a session while Scheduled or Checked In.</summary>
    public static bool CanStart(BookingStatus status) =>
        status is BookingStatus.Scheduled or BookingStatus.CheckedIn;

    /// <summary>A booking can be edited/cancelled while it has not been started or closed.</summary>
    public static bool CanEdit(BookingStatus status) =>
        status is BookingStatus.Scheduled or BookingStatus.CheckedIn;

    /// <summary>
    /// A booking is eligible to be marked No Show when it is still open (Scheduled/CheckedIn)
    /// and its start time has passed.
    /// </summary>
    public static bool IsNoShowEligible(BookingStatus status, DateTimeOffset startUtc, DateTimeOffset now) =>
        (status is BookingStatus.Scheduled or BookingStatus.CheckedIn) && startUtc <= now;
}
