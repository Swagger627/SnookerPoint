using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Bookings;

/// <summary>
/// Table reservations. Prevents overlapping bookings on a table (blocking statuses only),
/// supports edit/cancel/check-in/no-show with audit, and starts an existing booking into a
/// live table session via the normal session workflow — permanently linking the two, never
/// starting the same booking twice, and requiring an open shift. Bookings never create a
/// payment, sale or deposit. All times stored in UTC.
/// </summary>
public interface IBookingService
{
    IReadOnlyList<BookingListItem> GetBookings(BookingFilter filter);

    /// <summary>The next upcoming (Scheduled/CheckedIn) bookings, soonest first.</summary>
    IReadOnlyList<BookingListItem> GetUpcoming(int count);

    BookingListItem? Get(int bookingId);

    OperationResult<int> Create(CreateBookingRequest request, int actorUserId);

    OperationResult Update(UpdateBookingRequest request, int actorUserId);

    OperationResult Cancel(int bookingId, string reason, int actorUserId);

    OperationResult CheckIn(int bookingId, int actorUserId);

    OperationResult MarkNoShow(int bookingId, int actorUserId);

    /// <summary>
    /// Starts the booking into a live table session (Hourly or Fixed) using the existing
    /// session workflow, then permanently links the booking. Requires an open shift and
    /// cannot start the same booking twice.
    /// </summary>
    OperationResult<int> StartSession(int bookingId, BillingType billingType, Money? fixedAmount, int actorUserId, int shiftId);

    /// <summary>Free active tables (not reserved for the slot and not currently in use) as alternatives.</summary>
    IReadOnlyList<AlternativeTable> GetAlternativeTables(int bookingId);
}
