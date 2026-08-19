using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Application.Bookings;

/// <summary>Request to create a booking. Times are given in UTC (the UI converts from local).</summary>
public sealed record CreateBookingRequest(
    string CustomerName,
    string? Phone,
    int TableId,
    DateTimeOffset StartUtc,
    int DurationMinutes,
    int? PlayerCount,
    string? Notes);

/// <summary>Request to edit an existing (not-yet-started) booking.</summary>
public sealed record UpdateBookingRequest(
    int BookingId,
    string CustomerName,
    string? Phone,
    int TableId,
    DateTimeOffset StartUtc,
    int DurationMinutes,
    int? PlayerCount,
    string? Notes);

/// <summary>Filter for the bookings list (all optional).</summary>
public sealed record BookingFilter(
    DateTimeOffset? OnDateLocal = null,
    int? TableId = null,
    BookingStatus? Status = null,
    string? CustomerName = null,
    string? Phone = null);

/// <summary>A booking row for lists and the dashboard.</summary>
public sealed record BookingListItem(
    int Id,
    string CustomerName,
    string? Phone,
    int TableId,
    string TableName,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int DurationMinutes,
    int? PlayerCount,
    string? Notes,
    BookingStatus Status,
    int? LinkedSessionId,
    bool TableCurrentlyInUse);

/// <summary>An alternative free table when the reserved one is occupied.</summary>
public sealed record AlternativeTable(int TableId, string TableName);
