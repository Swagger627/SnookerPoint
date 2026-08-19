using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.Services;

/// <summary>A selectable table (active) for the booking editor and start dialogs.</summary>
public sealed record BookingTableOption(int TableId, string Name, Money HourlyRate)
{
    public string Display => $"{Name} · {HourlyRate.Format()}/hr";
}

/// <summary>Seeds the booking editor. For a new booking most values are defaults.</summary>
public sealed record BookingEditorContext(
    bool IsEdit,
    IReadOnlyList<BookingTableOption> Tables,
    string CustomerName,
    string? Phone,
    int? TableId,
    DateTimeOffset StartLocal,
    int DurationMinutes,
    int? PlayerCount,
    string? Notes);

/// <summary>What the booking editor returns. Start is local; the caller converts to UTC.</summary>
public sealed record BookingEditorResult(
    string CustomerName,
    string? Phone,
    int TableId,
    DateTimeOffset StartLocal,
    int DurationMinutes,
    int? PlayerCount,
    string? Notes);

/// <summary>
/// Seeds the "start this booking" dialog: the reserved table, whether it is currently in
/// use, and the tables the operator may actually start on (the reserved table when free,
/// plus any free alternatives).
/// </summary>
public sealed record BookingStartContext(
    string CustomerName,
    string ReservedTableName,
    bool ReservedInUse,
    IReadOnlyList<BookingTableOption> TableChoices);

/// <summary>The chosen table and billing for starting a booking into a live session.</summary>
public sealed record BookingStartResult(int TableId, BillingType BillingType, Money? FixedAmount);
