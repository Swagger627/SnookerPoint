using System.Threading;
using System.Windows;
using System.Windows.Controls;
using SnookerPoint.App.Controls;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.App.ViewModels;
using SnookerPoint.App.Views;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Application.Security;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// STA smoke tests for the reusable scrolling behaviour and the reorganised Home screen,
/// rendered with the real resources in both themes.
/// </summary>
[Collection("WpfSmoke")]
public class ScrollBehaviorSmokeTests
{
    [Fact]
    public void SmoothScrollBehavior_AttachesAndDetaches_WithoutError()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var scrollViewer = new ScrollViewer();
                SmoothScrollBehavior.SetEnabled(scrollViewer, true);
                Assert.True(SmoothScrollBehavior.GetEnabled(scrollViewer));

                // Detaching must also be clean (unsubscribes the handler).
                SmoothScrollBehavior.SetEnabled(scrollViewer, false);
                Assert.False(SmoothScrollBehavior.GetEnabled(scrollViewer));
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

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void HomeView_WithScrollBehaviour_RendersInBothThemes(string themeName)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
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

                var vm = new HomeViewModel(session, env.Shifts, env.Auth, new PermissionService(), env.ClubSettings,
                    env.Audit, env.OwnerRecovery, env.Bookings, new FakeLicensingService(), new FakeLicenseGate(), new FakeThemeService(), new FakeDialogService(), new FakeNavigationService());
                var view = new HomeView { DataContext = vm };
                view.Measure(new Size(1200, 900));
                view.Arrange(new Rect(0, 0, 1200, 900));
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
}
