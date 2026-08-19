using System.Threading;
using System.Windows;
using SnookerPoint.App.Controls;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.App.ViewModels;
using SnookerPoint.App.ViewModels.Dialogs;
using SnookerPoint.App.Views;
using SnookerPoint.App.Views.Dialogs;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Loads the Phase 5 Bookings view and the booking editor / start dialogs at runtime with
/// the real resources, in both dark and light themes.
/// </summary>
[Collection("WpfSmoke")]
public class Phase5ViewSmokeTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Phase5ViewsAndDialogs_LoadWithoutResourceErrors(string themeName)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                Run(themeName);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

    private static void Run(string themeName)
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SnookerPoint.App;component/Themes/Styles.xaml"),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/SnookerPoint.App;component/Themes/{themeName}.xaml"),
        });
        app.Resources["BoolToVisible"] = new BoolToVisibilityConverter();
        app.Resources["InverseBoolToVisible"] = new InverseBoolToVisibilityConverter();
        app.Resources["NullToCollapsed"] = new NullOrEmptyToCollapsedConverter();

        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000, 12_000);
        env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(2), 60, customerName: "Ahmed", phone: "0300-1234567", players: 4, notes: "Corner");
        env.SeedBooking(ownerId, tableIds[1], env.Clock.UtcNow.AddHours(5), 90, customerName: "Bushra");

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, false));

        var vm = new BookingsViewModel(env.Bookings, env.TableManagement, env.Shifts, session,
            new PermissionService(), new FakeDialogService(), new FakeNavigationService(), new ThemeService(app), env.Clock, new FakeLicenseGate());
        RenderView(new BookingsView { DataContext = vm });

        // Dialogs
        var tables = new[]
        {
            new BookingTableOption(tableIds[0], "Table 1", Money.FromRupees(120)),
            new BookingTableOption(tableIds[1], "Table 2", Money.FromRupees(120)),
        };
        _ = new BookingEditorDialog(new BookingEditorDialogViewModel(new BookingEditorContext(
            IsEdit: false, Tables: tables, CustomerName: string.Empty, Phone: null, TableId: tableIds[0],
            StartLocal: DateTimeOffset.Now.AddHours(2), DurationMinutes: 60, PlayerCount: null, Notes: null)));
        _ = new BookingStartDialog(new BookingStartDialogViewModel(new BookingStartContext(
            "Ahmed", "Table 1", ReservedInUse: true, tables)));
    }

    private static void RenderView(FrameworkElement view)
    {
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
    }
}
