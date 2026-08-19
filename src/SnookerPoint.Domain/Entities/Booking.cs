using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A table reservation. Times are stored in UTC and displayed in local time. A booking
/// never creates a payment, sale or deposit; it optionally becomes a live table session
/// via the normal session workflow, and is then permanently linked to that session.
/// </summary>
public sealed class Booking
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public int TableId { get; set; }

    /// <summary>The reservation start, stored in UTC.</summary>
    public DateTimeOffset StartUtc { get; set; }

    /// <summary>Expected duration in minutes; the slot end is Start + this.</summary>
    public int DurationMinutes { get; set; }

    public int? PlayerCount { get; set; }

    public string? Notes { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Scheduled;

    /// <summary>The live/finished table session this booking was started into (permanent link).</summary>
    public int? LinkedSessionId { get; set; }

    public string? CancelReason { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>The reservation slot end (UTC).</summary>
    public DateTimeOffset EndUtc => StartUtc.AddMinutes(DurationMinutes);
}
