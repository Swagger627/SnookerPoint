using SnookerPoint.Application.Bookings;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class BookingServiceTests
{
    private static int CreateUser(Phase1Environment env, UserRole role)
    {
        using var db = env.NewContext();
        var user = new User
        {
            DisplayName = role.ToString(),
            Username = role + "-" + Guid.NewGuid().ToString("N")[..6],
            Role = role,
            PasswordHash = "x",
            IsActive = true,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private static DateTimeOffset At(Phase1Environment env, int hoursFromNow) =>
        env.Clock.UtcNow.AddHours(hoursFromNow);

    // ---------- Create & validation ----------

    [Fact]
    public void Create_StoresBooking_AsScheduled_WithNoSaleOrPayment()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);

        var create = env.Bookings.Create(new CreateBookingRequest(
            "Ali Khan", "0300-1234567", tableIds[0], At(env, 2), 90, 4, "Corner table"), ownerId);
        Assert.True(create.Succeeded, create.ErrorMessage);

        var booking = env.Bookings.Get(create.Value);
        Assert.NotNull(booking);
        Assert.Equal("Ali Khan", booking!.CustomerName);
        Assert.Equal(BookingStatus.Scheduled, booking.Status);
        Assert.Null(booking.LinkedSessionId);

        // A booking must never create a sale, payment or session.
        using var db = env.NewContext();
        Assert.Empty(db.Sales);
        Assert.Empty(db.SalePayments);
        Assert.Empty(db.TableSessions);
    }

    [Fact]
    public void Create_RejectsEmptyName_AndNonPositiveDuration()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);

        Assert.True(env.Bookings.Create(new CreateBookingRequest(
            " ", null, tableIds[0], At(env, 1), 60, null, null), ownerId).Failed);
        Assert.True(env.Bookings.Create(new CreateBookingRequest(
            "Sara", null, tableIds[0], At(env, 1), 0, null, null), ownerId).Failed);
    }

    // ---------- Overlap prevention ----------

    [Fact]
    public void OverlappingBooking_OnSameTable_IsBlocked()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);

        env.SeedBooking(ownerId, tableIds[0], At(env, 2), durationMinutes: 60);
        var overlap = env.Bookings.Create(new CreateBookingRequest(
            "Bilal", null, tableIds[0], At(env, 2).AddMinutes(30), 60, null, null), ownerId);

        Assert.True(overlap.Failed);
        Assert.Contains("overlap", overlap.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SameTime_OnDifferentTables_IsAllowed()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60);
        var other = env.Bookings.Create(new CreateBookingRequest(
            "Bilal", null, tableIds[1], At(env, 2), 60, null, null), ownerId);

        Assert.True(other.Succeeded, other.ErrorMessage);
    }

    [Fact]
    public void AdjacentBookings_ThatDoNotOverlap_AreAllowed()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);

        env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60);
        // Starts exactly when the first ends — touching but not overlapping.
        var next = env.Bookings.Create(new CreateBookingRequest(
            "Bilal", null, tableIds[0], At(env, 3), 60, null, null), ownerId);

        Assert.True(next.Succeeded, next.ErrorMessage);
    }

    [Fact]
    public void CancelledOrNoShowBooking_DoesNotBlock_TheSlot()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);

        var first = env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60);
        Assert.True(env.Bookings.Cancel(first, "Customer called off", ownerId).Succeeded);

        // The exact same slot is now free because a cancelled booking never blocks.
        var reuse = env.Bookings.Create(new CreateBookingRequest(
            "Bilal", null, tableIds[0], At(env, 2), 60, null, null), ownerId);
        Assert.True(reuse.Succeeded, reuse.ErrorMessage);
    }

    // ---------- Edit / cancel / audit ----------

    [Fact]
    public void Update_ChangesFields_AndWritesAudit()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000);
        var id = env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60, customerName: "Ali");

        var update = env.Bookings.Update(new UpdateBookingRequest(
            id, "Ali Raza", "0311", tableIds[1], At(env, 3), 120, 2, "moved"), ownerId);
        Assert.True(update.Succeeded, update.ErrorMessage);

        var booking = env.Bookings.Get(id)!;
        Assert.Equal("Ali Raza", booking.CustomerName);
        Assert.Equal(tableIds[1], booking.TableId);
        Assert.Equal(120, booking.DurationMinutes);

        using var db = env.NewContext();
        Assert.Contains(db.AuditEvents, a => a.Action == AuditActions.BookingUpdated && a.EntityId == id.ToString());
    }

    [Fact]
    public void Cancel_SetsStatus_AndWritesAudit()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        var id = env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60);

        Assert.True(env.Bookings.Cancel(id, "Double-booked elsewhere", ownerId).Succeeded);
        Assert.Equal(BookingStatus.Cancelled, env.Bookings.Get(id)!.Status);

        using var db = env.NewContext();
        Assert.Contains(db.AuditEvents, a => a.Action == AuditActions.BookingCancelled && a.EntityId == id.ToString());
    }

    // ---------- Check-in ----------

    [Fact]
    public void CheckIn_MovesScheduledToCheckedIn()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        var id = env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60);

        Assert.True(env.Bookings.CheckIn(id, ownerId).Succeeded);
        Assert.Equal(BookingStatus.CheckedIn, env.Bookings.Get(id)!.Status);

        // Cannot check in twice.
        Assert.True(env.Bookings.CheckIn(id, ownerId).Failed);
    }

    // ---------- No-show ----------

    [Fact]
    public void MarkNoShow_OnlyAfterStartTime()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        var id = env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60);

        // Too early — the booking is in the future.
        Assert.True(env.Bookings.MarkNoShow(id, ownerId).Failed);

        // Advance past the start time.
        env.Clock.Advance(TimeSpan.FromHours(3));
        Assert.True(env.Bookings.MarkNoShow(id, ownerId).Succeeded);
        Assert.Equal(BookingStatus.NoShow, env.Bookings.Get(id)!.Status);
    }

    // ---------- Start session from a booking ----------

    [Fact]
    public void StartSession_Hourly_LinksSession_AndMarksStarted()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.Clock.Advance(TimeSpan.FromHours(2));
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow, 60);

        var start = env.Bookings.StartSession(id, BillingType.Hourly, null, ownerId, shiftId);
        Assert.True(start.Succeeded, start.ErrorMessage);
        Assert.True(start.Value > 0);

        var booking = env.Bookings.Get(id)!;
        Assert.Equal(BookingStatus.Started, booking.Status);
        Assert.Equal(start.Value, booking.LinkedSessionId);

        // A live session now exists on that table.
        using var db = env.NewContext();
        var session = db.TableSessions.Single();
        Assert.Equal(BillingType.Hourly, session.BillingType);
        Assert.Equal(SessionStatus.Active, session.Status);
    }

    [Fact]
    public void StartSession_Fixed_UsesFixedAmount()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.Clock.Advance(TimeSpan.FromHours(2));
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow, 60);

        var start = env.Bookings.StartSession(id, BillingType.Fixed, Money.FromRupees(500), ownerId, shiftId);
        Assert.True(start.Succeeded, start.ErrorMessage);

        using var db = env.NewContext();
        var session = db.TableSessions.Single();
        Assert.Equal(BillingType.Fixed, session.BillingType);
        Assert.Equal(Money.FromRupees(500).Paisa, session.FixedAmount!.Value.Paisa);
    }

    [Fact]
    public void StartSession_CannotStartTheSameBookingTwice()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.Clock.Advance(TimeSpan.FromHours(2));
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow, 60);

        Assert.True(env.Bookings.StartSession(id, BillingType.Hourly, null, ownerId, shiftId).Succeeded);
        var again = env.Bookings.StartSession(id, BillingType.Hourly, null, ownerId, shiftId);

        Assert.True(again.Failed);
        Assert.Contains("already been started", again.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartSession_RequiresAnOpenShift()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.Clock.Advance(TimeSpan.FromHours(2));
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow, 60);

        // A non-existent shift id must be rejected by the session workflow.
        var start = env.Bookings.StartSession(id, BillingType.Hourly, null, ownerId, shiftId: 99999);
        Assert.True(start.Failed);

        // The booking is untouched.
        Assert.Equal(BookingStatus.Scheduled, env.Bookings.Get(id)!.Status);
    }

    [Fact]
    public void StartSession_CompletesBooking_WhenLinkedSessionFinishes()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.Clock.Advance(TimeSpan.FromHours(2));
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow, 60);

        var sessionId = env.Bookings.StartSession(id, BillingType.Hourly, null, ownerId, shiftId).Value;
        env.Clock.Advance(TimeSpan.FromMinutes(45));
        Assert.True(env.Sessions.FinishSession(sessionId, ownerId, shiftId, null).Succeeded);

        // On the next read the booking reconciles to Completed.
        Assert.Equal(BookingStatus.Completed, env.Bookings.Get(id)!.Status);

        using var db = env.NewContext();
        Assert.Contains(db.AuditEvents, a => a.Action == AuditActions.BookingCompleted && a.EntityId == id.ToString());
    }

    // ---------- Upcoming ----------

    [Fact]
    public void GetUpcoming_ReturnsSoonestFirst_ExcludingPastAndClosed()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000, 12_000);

        var soon = env.SeedBooking(ownerId, tableIds[0], At(env, 1), 60, customerName: "Soon");
        env.SeedBooking(ownerId, tableIds[1], At(env, 5), 60, customerName: "Later");
        var cancelled = env.SeedBooking(ownerId, tableIds[2], At(env, 2), 60, customerName: "Cancelled");
        env.Bookings.Cancel(cancelled, "n/a", ownerId);

        var upcoming = env.Bookings.GetUpcoming(5);
        Assert.Equal(2, upcoming.Count);
        Assert.Equal("Soon", upcoming[0].CustomerName);
        Assert.Equal("Later", upcoming[1].CustomerName);
        Assert.DoesNotContain(upcoming, b => b.Id == cancelled);
    }

    [Fact]
    public void GetUpcoming_RespectsCount()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        for (var i = 1; i <= 8; i++)
        {
            env.SeedBooking(ownerId, tableIds[0], At(env, i * 2), 60, customerName: $"C{i}");
        }

        Assert.Equal(5, env.Bookings.GetUpcoming(5).Count);
    }

    // ---------- Filters ----------

    [Fact]
    public void GetBookings_FiltersByTableStatusNameAndPhone()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        var a = env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60, customerName: "Ahmed", phone: "0300111");
        env.SeedBooking(ownerId, tableIds[1], At(env, 3), 60, customerName: "Bushra", phone: "0300222");
        env.Bookings.CheckIn(a, ownerId);

        Assert.Single(env.Bookings.GetBookings(new BookingFilter(TableId: tableIds[0])));
        Assert.Single(env.Bookings.GetBookings(new BookingFilter(Status: BookingStatus.CheckedIn)));
        Assert.Single(env.Bookings.GetBookings(new BookingFilter(CustomerName: "bush")));
        Assert.Single(env.Bookings.GetBookings(new BookingFilter(Phone: "111")));
        Assert.Equal(2, env.Bookings.GetBookings(new BookingFilter()).Count);
    }

    [Fact]
    public void GetBookings_FiltersByLocalDate()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);

        var today = env.SeedBooking(ownerId, tableIds[0], At(env, 2), 60, customerName: "Today");
        env.SeedBooking(ownerId, tableIds[0], At(env, 48), 60, customerName: "TwoDays");

        var onToday = env.Bookings.GetBookings(new BookingFilter(OnDateLocal: env.Clock.UtcNow.ToLocalTime()));
        Assert.Single(onToday);
        Assert.Equal(today, onToday[0].Id);
    }

    // ---------- Alternative tables ----------

    [Fact]
    public void GetAlternativeTables_ExcludesInUseAndReservedTables()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000, 12_000);
        env.Clock.Advance(TimeSpan.FromHours(2));
        var slot = env.Clock.UtcNow;

        // The booking is on table 0.
        var id = env.SeedBooking(ownerId, tableIds[0], slot, 60);
        // Table 1 is occupied by a live session.
        Assert.True(env.Sessions.StartSession(new SnookerPoint.Application.Tables.StartSessionRequest(
            tableIds[1], ownerId, shiftId, "In play", null)).Succeeded);
        // Table 2 is reserved for the overlapping slot.
        env.SeedBooking(ownerId, tableIds[2], slot, 60, customerName: "Reserved");

        var alternatives = env.Bookings.GetAlternativeTables(id);

        // Only tables beyond the first three (the default set has five) remain free.
        Assert.DoesNotContain(alternatives, a => a.TableId == tableIds[0]); // own table
        Assert.DoesNotContain(alternatives, a => a.TableId == tableIds[1]); // in use
        Assert.DoesNotContain(alternatives, a => a.TableId == tableIds[2]); // reserved
    }

    // ---------- Permissions ----------

    [Fact]
    public void FloorStaff_CannotManageBookings()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        var floor = CreateUser(env, UserRole.FloorStaff);

        var create = env.Bookings.Create(new CreateBookingRequest(
            "Guest", null, tableIds[0], At(env, 2), 60, null, null), floor);
        Assert.True(create.Failed);
        Assert.Contains("permission", create.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
