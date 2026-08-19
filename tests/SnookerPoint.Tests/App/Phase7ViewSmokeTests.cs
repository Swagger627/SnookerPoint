using System.Threading;
using System.Windows;
using SnookerPoint.App.Controls;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.App.ViewModels;
using SnookerPoint.App.Views;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>Loads the Phase 7 Activation view in both dark and light themes.</summary>
[Collection("WpfSmoke")]
public class Phase7ViewSmokeTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void ActivationView_LoadsWithoutResourceErrors(string themeName)
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
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, false));

        // Expired trial so the recovery actions are visible too.
        var licensing = new FakeLicensingService { Evaluation = FakeLicensingService.Expired() };
        var vm = new ActivationViewModel(licensing, env.ClubSettings, env.Backups, env.Health, session,
            new FakeDialogService(), new FakeNavigationService(), new ThemeService(app), new FakeApplicationControl());

        var view = new ActivationView { DataContext = vm };
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
    }
}
