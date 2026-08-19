using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Covers the Bookings screen view-model: creating, editing, checking in, cancelling and
/// starting bookings through the dialog service, filter wiring, and permission gating.
/// </summary>
public class BookingsViewModelTests
{
    private static (BookingsViewModel Vm, FakeDialogService Dialogs, FakeNavigationService Nav, int OwnerId, System.Collections.Generic.List<int> TableIds)
        Create(Phase1Environment env, UserRole role = UserRole.Owner)
    {
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        var actorId = ownerId;
        if (role != UserRole.Owner)
        {
            using var db = env.NewContext();
            var user = new User { DisplayName = role.ToString(), Username = role.ToString().ToLower(), Role = role, PasswordHash = "x", IsActive = true };
            db.Users.Add(user);
            db.SaveChanges();
            actorId = user.Id;
        }

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(actorId, role.ToString(), role.ToString().ToLower(), role, HasPin: false));
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        var vm = new BookingsViewModel(env.Bookings, env.TableManagement, env.Shifts, session,
            new PermissionService(), dialogs, nav, new FakeThemeService(), env.Clock, new FakeLicenseGate());
        return (vm, dialogs, nav, ownerId, tableIds);
    }

    private static BookingEditorResult Editor(Phase1Environment env, int tableId, string name = "Ali", int minutes = 60) =>
        new(name, "0300-1234567", tableId, env.Clock.UtcNow.AddHours(3).ToLocalTime(), minutes, 2, "VIP");

    [Fact]
    public void NewBooking_CreatesBooking_AndShowsSuccess()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _, tableIds) = Create(env);
        dialogs.BookingEditorResult = Editor(env, tableIds[0], "Ahmed");

        vm.NewBookingCommand.Execute(null);

        Assert.Single(vm.Rows);
        Assert.Equal("Ahmed", vm.Rows[0].CustomerName);
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
    }

    [Fact]
    public void NewBooking_ShowsError_WhenOverlapping()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId, tableIds) = Create(env);

        // Existing booking that the new one will overlap.
        env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(3), 60);
        dialogs.BookingEditorResult = Editor(env, tableIds[0]); // same table, same start

        vm.NewBookingCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Contains("overlap", vm.Feedback.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckIn_UpdatesRowStatus()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, ownerId, tableIds) = Create(env);
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(3), 60);
        vm.RefreshCommand.Execute(null);

        vm.CheckInCommand.Execute(vm.Rows.First(r => r.Id == id));

        Assert.Equal(BookingStatus.CheckedIn, env.Bookings.Get(id)!.Status);
    }

    [Fact]
    public void Cancel_WithConfirmation_CancelsBooking()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId, tableIds) = Create(env);
        dialogs.ConfirmResult = true;
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(3), 60);
        vm.RefreshCommand.Execute(null);

        vm.CancelCommand.Execute(vm.Rows.First(r => r.Id == id));

        Assert.Equal(BookingStatus.Cancelled, env.Bookings.Get(id)!.Status);
    }

    [Fact]
    public void Start_FromBooking_StartsHourlySession_AndLinks()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId, tableIds) = Create(env);
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(1), 60);
        vm.RefreshCommand.Execute(null);
        dialogs.BookingStartResult = new BookingStartResult(tableIds[0], BillingType.Hourly, null);

        vm.StartCommand.Execute(vm.Rows.First(r => r.Id == id));

        var booking = env.Bookings.Get(id)!;
        Assert.Equal(BookingStatus.Started, booking.Status);
        Assert.NotNull(booking.LinkedSessionId);
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
    }

    [Fact]
    public void Start_ShowsError_WhenNoShiftOpen()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId, tableIds) = Create(env);
        var id = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(1), 60);
        vm.RefreshCommand.Execute(null);

        // Close the owner's shift so none is open.
        var shift = env.Shifts.GetCurrentShift(ownerId)!;
        env.Shifts.CloseShift(shift.ShiftId, Money.Zero, null);
        dialogs.BookingStartResult = new BookingStartResult(tableIds[0], BillingType.Hourly, null);

        vm.StartCommand.Execute(vm.Rows.First(r => r.Id == id));

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Equal(BookingStatus.Scheduled, env.Bookings.Get(id)!.Status);
    }

    [Fact]
    public void StatusFilter_LimitsRows()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, ownerId, tableIds) = Create(env);
        var a = env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(2), 60);
        env.SeedBooking(ownerId, tableIds[1], env.Clock.UtcNow.AddHours(4), 60);
        env.Bookings.CheckIn(a, ownerId);

        vm.SelectedStatus = vm.StatusFilters.First(s => s.Value == BookingStatus.CheckedIn);

        Assert.Single(vm.Rows);
        Assert.Equal(a, vm.Rows[0].Id);
    }

    [Fact]
    public void FloorStaff_CannotCreateBookings()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _, tableIds) = Create(env, UserRole.FloorStaff);
        dialogs.BookingEditorResult = Editor(env, tableIds[0]);

        vm.NewBookingCommand.Execute(null);

        Assert.False(vm.CanManage);
        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Empty(vm.Rows);
    }
}
