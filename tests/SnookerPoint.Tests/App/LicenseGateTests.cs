using SnookerPoint.App.Licensing;
using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

public class LicenseGateTests
{
    [Fact]
    public void Gate_AllowsOperations_WhenTrialActive()
    {
        var licensing = new FakeLicensingService { Evaluation = FakeLicensingService.ActiveTrial() };
        var nav = new FakeNavigationService();
        var gate = new LicenseGate(licensing, nav);

        Assert.True(gate.EnsureCanOperate());
        Assert.False(nav.ActivationShown);
    }

    [Fact]
    public void Gate_BlocksAndRoutesToActivation_WhenExpired()
    {
        var licensing = new FakeLicensingService { Evaluation = FakeLicensingService.Expired() };
        var nav = new FakeNavigationService();
        var gate = new LicenseGate(licensing, nav);

        Assert.False(gate.EnsureCanOperate());
        Assert.True(nav.ActivationShown); // routed to Activation
    }

    [Fact]
    public void Gate_LicensedInstall_AllowsOperations()
    {
        var licensing = new FakeLicensingService { Evaluation = FakeLicensingService.Licensed() };
        var gate = new LicenseGate(licensing, new FakeNavigationService());
        Assert.True(gate.EnsureCanOperate());
    }

    // ---------- Runtime gate blocks new operational work in the VMs ----------

    [Fact]
    public void BlockedGate_PreventsNewBooking()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "Owner", "owner", UserRole.Owner, false));
        var dialogs = new FakeDialogService
        {
            BookingEditorResult = new BookingEditorResult("Ali", null, tableIds[0], env.Clock.UtcNow.AddHours(2).ToLocalTime(), 60, null, null),
        };
        var gate = new FakeLicenseGate { Allow = false };
        var vm = new BookingsViewModel(env.Bookings, env.TableManagement, env.Shifts, session,
            new PermissionService(), dialogs, new FakeNavigationService(), new FakeThemeService(), env.Clock, gate);

        vm.NewBookingCommand.Execute(null);

        Assert.True(gate.EnsureCanOperateCalls > 0);
        Assert.Empty(env.Bookings.GetBookings(new SnookerPoint.Application.Bookings.BookingFilter())); // nothing created
    }

    [Fact]
    public void AllowedGate_PermitsNewBooking()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "Owner", "owner", UserRole.Owner, false));
        var dialogs = new FakeDialogService
        {
            BookingEditorResult = new BookingEditorResult("Ali", null, tableIds[0], env.Clock.UtcNow.AddHours(2).ToLocalTime(), 60, null, null),
        };
        var vm = new BookingsViewModel(env.Bookings, env.TableManagement, env.Shifts, session,
            new PermissionService(), dialogs, new FakeNavigationService(), new FakeThemeService(), env.Clock, new FakeLicenseGate { Allow = true });

        vm.NewBookingCommand.Execute(null);

        Assert.Single(env.Bookings.GetBookings(new SnookerPoint.Application.Bookings.BookingFilter()));
    }
}
