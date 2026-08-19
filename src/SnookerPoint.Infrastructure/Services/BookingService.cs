using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Bookings;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Bookings;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Table reservations. Prevents overlapping bookings on a table (blocking statuses only),
/// supports edit/cancel/check-in/no-show with audit, and starts a booking into a live
/// session using the existing session workflow — permanently linking the two, requiring an
/// open shift, and never starting the same booking twice. A booking never creates a
/// payment, sale or deposit. Times are stored in UTC. When a linked session finishes, the
/// booking is reconciled to Completed on the next read.
/// </summary>
public sealed class BookingService : IBookingService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly ITableSessionService _sessions;
    private readonly IClock _clock;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        ITableSessionService sessions,
        IClock clock,
        ILogger<BookingService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _sessions = sessions;
        _clock = clock;
        _logger = logger;
    }

    // ==================== READ ====================

    public IReadOnlyList<BookingListItem> GetBookings(BookingFilter filter)
    {
        using var db = _factory.CreateDbContext();
        Reconcile(db);

        var tables = db.PoolTables.AsNoTracking().ToDictionary(t => t.Id, t => t.Name);
        var inUse = LiveTableIds(db);
        var bookings = db.Bookings.AsNoTracking().ToList();

        IEnumerable<Booking> query = bookings;

        if (filter.TableId is { } tableId)
        {
            query = query.Where(b => b.TableId == tableId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(b => b.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.CustomerName))
        {
            var term = filter.CustomerName.Trim();
            query = query.Where(b => b.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Phone))
        {
            var term = filter.Phone.Trim();
            query = query.Where(b => (b.Phone ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.OnDateLocal is { } onDate)
        {
            var localDay = onDate.ToLocalTime().Date;
            query = query.Where(b => b.StartUtc.ToLocalTime().Date == localDay);
        }

        return query
            .OrderBy(b => b.StartUtc)
            .Select(b => Map(b, tables, inUse))
            .ToList();
    }

    public IReadOnlyList<BookingListItem> GetUpcoming(int count)
    {
        using var db = _factory.CreateDbContext();
        Reconcile(db);

        var tables = db.PoolTables.AsNoTracking().ToDictionary(t => t.Id, t => t.Name);
        var inUse = LiveTableIds(db);
        var now = _clock.UtcNow;

        return db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Scheduled || b.Status == BookingStatus.CheckedIn)
            .ToList()
            .Where(b => b.EndUtc >= now)
            .OrderBy(b => b.StartUtc)
            .Take(count)
            .Select(b => Map(b, tables, inUse))
            .ToList();
    }

    public BookingListItem? Get(int bookingId)
    {
        using var db = _factory.CreateDbContext();
        Reconcile(db);
        var booking = db.Bookings.AsNoTracking().FirstOrDefault(b => b.Id == bookingId);
        if (booking is null)
        {
            return null;
        }

        var tables = db.PoolTables.AsNoTracking().ToDictionary(t => t.Id, t => t.Name);
        return Map(booking, tables, LiveTableIds(db));
    }

    // ==================== CREATE / EDIT ====================

    public OperationResult<int> Create(CreateBookingRequest request, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageBookings) is { } denied)
        {
            return OperationResult<int>.Failure(denied);
        }

        if (Validate(request.CustomerName, request.DurationMinutes) is { } error)
        {
            return OperationResult<int>.Failure(error);
        }

        var table = db.PoolTables.FirstOrDefault(t => t.Id == request.TableId);
        if (table is null || !table.IsActive)
        {
            return OperationResult<int>.Failure("Please choose an active table.");
        }

        var start = request.StartUtc;
        var end = start.AddMinutes(request.DurationMinutes);
        if (HasConflict(db, request.TableId, start, end, excludeId: null))
        {
            return OperationResult<int>.Failure($"{table.Name} already has a booking that overlaps this time.");
        }

        var now = _clock.UtcNow;
        var booking = new Booking
        {
            CustomerName = request.CustomerName.Trim(),
            Phone = Clean(request.Phone),
            TableId = request.TableId,
            StartUtc = start,
            DurationMinutes = request.DurationMinutes,
            PlayerCount = request.PlayerCount,
            Notes = Clean(request.Notes),
            Status = BookingStatus.Scheduled,
            CreatedByUserId = actorUserId,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        db.Bookings.Add(booking);
        db.SaveChanges();

        WriteAudit(db, AuditActions.BookingCreated, actorUserId, booking.Id,
            $"Booking for '{booking.CustomerName}' on {table.Name} at {Local(start)}.");
        db.SaveChanges();
        return OperationResult<int>.Success(booking.Id);
    }

    public OperationResult Update(UpdateBookingRequest request, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageBookings) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        if (Validate(request.CustomerName, request.DurationMinutes) is { } error)
        {
            return OperationResult.Failure(error);
        }

        var booking = db.Bookings.FirstOrDefault(b => b.Id == request.BookingId);
        if (booking is null)
        {
            return OperationResult.Failure("That booking was not found.");
        }

        if (!BookingRules.CanEdit(booking.Status))
        {
            return OperationResult.Failure("Only a scheduled or checked-in booking can be edited.");
        }

        var table = db.PoolTables.FirstOrDefault(t => t.Id == request.TableId);
        if (table is null || !table.IsActive)
        {
            return OperationResult.Failure("Please choose an active table.");
        }

        var start = request.StartUtc;
        var end = start.AddMinutes(request.DurationMinutes);
        if (HasConflict(db, request.TableId, start, end, excludeId: booking.Id))
        {
            return OperationResult.Failure($"{table.Name} already has a booking that overlaps this time.");
        }

        booking.CustomerName = request.CustomerName.Trim();
        booking.Phone = Clean(request.Phone);
        booking.TableId = request.TableId;
        booking.StartUtc = start;
        booking.DurationMinutes = request.DurationMinutes;
        booking.PlayerCount = request.PlayerCount;
        booking.Notes = Clean(request.Notes);
        booking.UpdatedUtc = _clock.UtcNow;

        WriteAudit(db, AuditActions.BookingUpdated, actorUserId, booking.Id,
            $"Booking for '{booking.CustomerName}' updated ({table.Name}, {Local(start)}).");
        db.SaveChanges();
        return OperationResult.Success();
    }

    // ==================== STATUS CHANGES ====================

    public OperationResult Cancel(int bookingId, string reason, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageBookings) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var booking = db.Bookings.FirstOrDefault(b => b.Id == bookingId);
        if (booking is null)
        {
            return OperationResult.Failure("That booking was not found.");
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
        {
            return OperationResult.Failure("That booking can no longer be cancelled.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelReason = Clean(reason);
        booking.UpdatedUtc = _clock.UtcNow;

        WriteAudit(db, AuditActions.BookingCancelled, actorUserId, booking.Id,
            $"Booking for '{booking.CustomerName}' cancelled.{(string.IsNullOrWhiteSpace(reason) ? string.Empty : " Reason: " + reason.Trim())}");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult CheckIn(int bookingId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageBookings) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var booking = db.Bookings.FirstOrDefault(b => b.Id == bookingId);
        if (booking is null)
        {
            return OperationResult.Failure("That booking was not found.");
        }

        if (!BookingRules.CanCheckIn(booking.Status))
        {
            return OperationResult.Failure("Only a scheduled booking can be checked in.");
        }

        booking.Status = BookingStatus.CheckedIn;
        booking.UpdatedUtc = _clock.UtcNow;
        WriteAudit(db, AuditActions.BookingCheckedIn, actorUserId, booking.Id, $"'{booking.CustomerName}' checked in.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult MarkNoShow(int bookingId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        if (Guard(db, actorUserId, Permission.ManageBookings) is { } denied)
        {
            return OperationResult.Failure(denied);
        }

        var booking = db.Bookings.FirstOrDefault(b => b.Id == bookingId);
        if (booking is null)
        {
            return OperationResult.Failure("That booking was not found.");
        }

        if (!BookingRules.IsNoShowEligible(booking.Status, booking.StartUtc, _clock.UtcNow))
        {
            return OperationResult.Failure("This booking cannot be marked as a no-show yet.");
        }

        booking.Status = BookingStatus.NoShow;
        booking.UpdatedUtc = _clock.UtcNow;
        WriteAudit(db, AuditActions.BookingNoShow, actorUserId, booking.Id, $"'{booking.CustomerName}' marked as no-show.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    // ==================== START SESSION ====================

    public OperationResult<int> StartSession(int bookingId, BillingType billingType, Money? fixedAmount, int actorUserId, int shiftId)
    {
        // Load and guard the booking first (own context).
        using (var db = _factory.CreateDbContext())
        {
            if (Guard(db, actorUserId, Permission.ManageBookings) is { } denied)
            {
                return OperationResult<int>.Failure(denied);
            }

            var booking = db.Bookings.AsNoTracking().FirstOrDefault(b => b.Id == bookingId);
            if (booking is null)
            {
                return OperationResult<int>.Failure("That booking was not found.");
            }

            if (!BookingRules.CanStart(booking.Status))
            {
                return booking.Status == BookingStatus.Started
                    ? OperationResult<int>.Failure("This booking has already been started.")
                    : OperationResult<int>.Failure("This booking can no longer be started.");
            }
        }

        // Start the session via the existing workflow (enforces StartSession permission,
        // open shift, and the one-live-session-per-table rule).
        BookingListItem? current = Get(bookingId);
        if (current is null)
        {
            return OperationResult<int>.Failure("That booking was not found.");
        }

        var start = _sessions.StartSession(new StartSessionRequest(
            current.TableId, actorUserId, shiftId,
            current.CustomerName, current.Notes, billingType, fixedAmount));
        if (start.Failed)
        {
            return OperationResult<int>.Failure(start.ErrorMessage);
        }

        // Resolve the newly-created live session on that table and link it permanently.
        using var db2 = _factory.CreateDbContext();
        var booking2 = db2.Bookings.FirstOrDefault(b => b.Id == bookingId);
        if (booking2 is null || !BookingRules.CanStart(booking2.Status))
        {
            // A concurrent start won the race; the started session stands on its own.
            return OperationResult<int>.Failure("This booking has already been started.");
        }

        var sessionId = db2.TableSessions
            .Where(s => s.CurrentTableId == current.TableId &&
                        (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
            .Select(s => s.Id)
            .FirstOrDefault();

        booking2.LinkedSessionId = sessionId == 0 ? null : sessionId;
        booking2.Status = BookingStatus.Started;
        booking2.UpdatedUtc = _clock.UtcNow;
        WriteAudit(db2, AuditActions.BookingStarted, actorUserId, booking2.Id,
            $"Booking for '{booking2.CustomerName}' started as a {(billingType == BillingType.Fixed ? "fixed" : "hourly")} session.");
        db2.SaveChanges();

        return OperationResult<int>.Success(sessionId);
    }

    public IReadOnlyList<AlternativeTable> GetAlternativeTables(int bookingId)
    {
        using var db = _factory.CreateDbContext();
        var booking = db.Bookings.AsNoTracking().FirstOrDefault(b => b.Id == bookingId);
        if (booking is null)
        {
            return Array.Empty<AlternativeTable>();
        }

        var inUse = LiveTableIds(db);
        var tables = db.PoolTables.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToList();
        var blocking = db.Bookings.AsNoTracking()
            .Where(b => b.Id != bookingId)
            .ToList()
            .Where(b => BookingRules.Blocks(b.Status))
            .ToList();

        var result = new List<AlternativeTable>();
        foreach (var table in tables)
        {
            if (table.Id == booking.TableId || inUse.Contains(table.Id))
            {
                continue;
            }

            var conflict = blocking.Any(b => b.TableId == table.Id &&
                BookingRules.Overlaps(b.StartUtc, b.EndUtc, booking.StartUtc, booking.EndUtc));
            if (!conflict)
            {
                result.Add(new AlternativeTable(table.Id, table.Name));
            }
        }

        return result;
    }

    // ==================== HELPERS ====================

    /// <summary>Marks Started bookings whose linked session has finished as Completed.</summary>
    private void Reconcile(SnookerPointDbContext db)
    {
        var started = db.Bookings.Where(b => b.Status == BookingStatus.Started && b.LinkedSessionId != null).ToList();
        if (started.Count == 0)
        {
            return;
        }

        var linkedIds = started.Select(b => b.LinkedSessionId!.Value).ToHashSet();
        var finished = db.TableSessions.AsNoTracking()
            .Where(s => linkedIds.Contains(s.Id) &&
                        (s.Status == SessionStatus.Completed || s.Status == SessionStatus.Voided))
            .Select(s => s.Id)
            .ToHashSet();

        var changed = false;
        foreach (var booking in started.Where(b => finished.Contains(b.LinkedSessionId!.Value)))
        {
            booking.Status = BookingStatus.Completed;
            booking.UpdatedUtc = _clock.UtcNow;
            db.AuditEvents.Add(new AuditEvent
            {
                Utc = _clock.UtcNow,
                Action = AuditActions.BookingCompleted,
                ActorUserId = booking.CreatedByUserId,
                Entity = nameof(Booking),
                EntityId = booking.Id.ToString(),
                Details = $"Booking for '{booking.CustomerName}' completed when its session finished.",
            });
            changed = true;
        }

        if (changed)
        {
            db.SaveChanges();
        }
    }

    private static bool HasConflict(SnookerPointDbContext db, int tableId, DateTimeOffset start, DateTimeOffset end, int? excludeId)
    {
        var candidates = db.Bookings.AsNoTracking()
            .Where(b => b.TableId == tableId && (excludeId == null || b.Id != excludeId))
            .ToList()
            .Where(b => BookingRules.Blocks(b.Status));

        return candidates.Any(b => BookingRules.Overlaps(b.StartUtc, b.EndUtc, start, end));
    }

    private static HashSet<int> LiveTableIds(SnookerPointDbContext db) =>
        db.TableSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
            .Select(s => s.CurrentTableId)
            .ToHashSet();

    private static BookingListItem Map(Booking b, IReadOnlyDictionary<int, string> tables, HashSet<int> inUse) =>
        new(b.Id, b.CustomerName, b.Phone, b.TableId, tables.GetValueOrDefault(b.TableId, "—"),
            b.StartUtc, b.EndUtc, b.DurationMinutes, b.PlayerCount, b.Notes, b.Status, b.LinkedSessionId,
            inUse.Contains(b.TableId));

    private static string? Validate(string? customerName, int durationMinutes)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return "Please enter the customer's name.";
        }

        if (durationMinutes <= 0)
        {
            return "Please enter an expected duration greater than zero.";
        }

        return null;
    }

    private static string Local(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string? Guard(SnookerPointDbContext db, int actorUserId, Permission permission)
    {
        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        return actor is not null && _permissions.HasPermission(actor, permission)
            ? null
            : "You do not have permission to manage bookings.";
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int actorUserId, int bookingId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(Booking),
            EntityId = bookingId.ToString(),
            Details = details,
        });
    }
}
