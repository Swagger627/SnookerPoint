using System.Threading;
using System.Windows;
using SnookerPoint.App.Controls;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.App.ViewModels;
using SnookerPoint.App.Views;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Loads the Phase 6 Reports, Backup, Admin, Settings and Audit views at runtime with the
/// real resources, in both dark and light themes.
/// </summary>
[Collection("WpfSmoke")]
public class Phase6ViewSmokeTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Phase6Views_LoadWithoutResourceErrors(string themeName)
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
        var (ownerId, shiftId, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(2), 60, customerName: "Ahmed");
        env.Backups.CreateBackup(null, "smoke", ownerId);

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, false));
        var permissions = new PermissionService();
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        var theme = new ThemeService(app);

        RenderView(new ReportsView { DataContext = new ReportsViewModel(env.Reporting, env.Csv, env.StaffManagement, env.PaymentMethods, env.TableManagement, env.Products, env.Categories, session, permissions, dialogs, nav, theme, env.Clock) });
        RenderView(new BackupView { DataContext = new BackupViewModel(env.Backups, session, permissions, dialogs, nav, theme, new FakeApplicationControl()) });
        RenderView(new AdminView { DataContext = new AdminViewModel(env.Health, session, permissions, dialogs, nav, theme) });
        RenderView(new SettingsView { DataContext = new SettingsViewModel(env.OperationalSettings, session, permissions, dialogs, nav, theme, new FakeLicensingService(), new FakeLicenseGate()) });
        RenderView(new AuditView { DataContext = new AuditViewModel(env.Audit, env.Csv, session, permissions, nav, theme) });
    }

    private static void RenderView(FrameworkElement view)
    {
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
    }
}
