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
using SnookerPoint.Application.Settings;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Loads every Phase 2 dialog and view at runtime with the real application
/// resources, catching missing StaticResource keys / XAML load errors that a build
/// does not detect. Requires an STA thread and a WPF Application for resource lookup.
/// </summary>
[Collection("WpfSmoke")]
public class Phase2ViewSmokeTests
{
    [Fact]
    public void AllPhase2DialogsAndViews_LoadWithoutResourceErrors()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                Run();
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

    private static void Run()
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SnookerPoint.App;component/Themes/Styles.xaml"),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SnookerPoint.App;component/Themes/Dark.xaml"),
        });
        app.Resources["BoolToVisible"] = new BoolToVisibilityConverter();
        app.Resources["InverseBoolToVisible"] = new InverseBoolToVisibilityConverter();
        app.Resources["NullToCollapsed"] = new NullOrEmptyToCollapsedConverter();
        app.Resources["AllTrueToVisible"] = new AllTrueToVisibilityConverter();

        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000, 24_000);

        // Start one session (with a completed pause) and finish another so the
        // dashboard, correction dialog and history all have data.
        env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, "Table A guests", "vip"));
        var liveId = env.Sessions.GetDashboard().First(c => c.TableId == tables[0]).Session!.SessionId;
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        env.Sessions.PauseSession(liveId, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(5));
        env.Sessions.ResumeSession(liveId, ownerId, shiftId);

        env.Sessions.StartSession(new StartSessionRequest(tables[1], ownerId, shiftId, null, null));
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        var finishId = env.Sessions.GetDashboard().First(c => c.TableId == tables[1]).Session!.SessionId;
        env.Sessions.FinishSession(finishId, ownerId, shiftId, "done");

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, false));
        var permissions = new PermissionService();
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        // Theme dictionary is already merged above via the component-qualified pack
        // URI; ThemeService.Apply uses an app-relative URI that only resolves when
        // SnookerPoint.App is the entry assembly (i.e. in the real app), so it is not
        // called here.
        var theme = new ThemeService(app);

        // --- Dialogs ---
        _ = new StartSessionDialog(new StartSessionDialogViewModel("Table 1", "Snooker", "Rs 120/hr", "Exact time", "3:00 PM"));
        _ = new TransferDialog(new TransferDialogViewModel("Table 1",
            new[] { new TransferDestination(tables[1], "Table 2", Money.FromPaisa(24_000)) }));
        var summary = env.Sessions.GetSessionSummary(finishId)!;
        _ = new FinishDialog(new FinishDialogViewModel(summary));
        var correctionContext = env.Sessions.GetCorrectionContext(liveId)!;
        _ = new CorrectionDialog(new CorrectionDialogViewModel(correctionContext, env.Calculator));
        _ = new BillingSettingsDialog(new BillingSettingsDialogViewModel(
            new BillingSettingsView(BillingMethod.Exact, 5, 0, 0)));

        var roleOptions = new[] { UserRole.Owner, UserRole.Administrator, UserRole.Manager, UserRole.Cashier, UserRole.FloorStaff };
        _ = new StaffEditDialog(new StaffEditDialogViewModel(
            new StaffEditContext(true, string.Empty, string.Empty, UserRole.Cashier, roleOptions)));
        _ = new SetCredentialDialog(new SetCredentialDialogViewModel(new SetCredentialContext(false, "Someone")));

        // Account-security dialogs
        _ = new RecoveryCodeDialog("ABCD-EFGH-JKLM-NPQR-STUV");
        _ = new TemporaryPasswordDialog("Cashier One", "Temp-ABCD1234");
        _ = new ForgotPasswordDialog(new ForgotPasswordDialogViewModel(
            new ForgotPasswordContext(new SnookerPoint.Application.Security.OwnerRecoveryStatus(true, true))));

        // Feedback banner in each severity
        RenderFeedbackBanners();

        // Start-session dialog with the billing-type selector
        _ = new StartSessionDialog(new StartSessionDialogViewModel("Table 2", "Snooker", "Rs 240/hr", "Exact time", "3:30 PM"));

        // --- Tables dashboard view (with a live card) ---
        using (var tablesVm = new TablesViewModel(env.Sessions, env.Shifts, session, permissions, env.Billing,
            env.Calculator, dialogs, nav, theme, env.Clock, new FakeLicenseGate()))
        {
            var view = new TablesView { DataContext = tablesVm };
            view.Measure(new Size(1200, 900));
            view.Arrange(new Rect(0, 0, 1200, 900));
        }

        // --- History view (with a finished row) ---
        var historyVm = new SessionHistoryViewModel(env.Sessions, nav, theme);
        var historyView = new SessionHistoryView { DataContext = historyVm };
        historyView.Measure(new Size(1200, 900));
        historyView.Arrange(new Rect(0, 0, 1200, 900));

        // --- Manage Tables view ---
        var manageTablesVm = new ManageTablesViewModel(env.TableManagement, session, permissions, dialogs, nav, theme);
        var manageTablesView = new ManageTablesView { DataContext = manageTablesVm };
        manageTablesView.Measure(new Size(1200, 900));
        manageTablesView.Arrange(new Rect(0, 0, 1200, 900));

        // --- Staff view ---
        var staffVm = new StaffViewModel(env.StaffManagement, session, permissions, dialogs, nav, theme);
        var staffView = new StaffView { DataContext = staffVm };
        staffView.Measure(new Size(1200, 900));
        staffView.Arrange(new Rect(0, 0, 1200, 900));

        // --- Account / security view ---
        var accountVm = new AccountViewModel(env.AccountSecurity, env.OwnerRecovery, session, dialogs, nav, theme);
        var accountView = new AccountView { DataContext = accountVm };
        accountView.Measure(new Size(1200, 900));
        accountView.Arrange(new Rect(0, 0, 1200, 900));
    }

    /// <summary>Renders the themed feedback banner in each severity so a missing brush key fails the test.</summary>
    private static void RenderFeedbackBanners()
    {
        foreach (var kind in new[] { FeedbackKind.Success, FeedbackKind.Warning, FeedbackKind.Error })
        {
            var banner = new FeedbackBanner { Kind = kind, Message = "Sample feedback message." };
            banner.Measure(new Size(600, 200));
            banner.Arrange(new Rect(0, 0, 600, 200));
        }
    }

    [Fact]
    public void FeedbackComponents_RenderInLightTheme()
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
                // Merge the Light tokens last so they win for this render.
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/SnookerPoint.App;component/Themes/Light.xaml"),
                });

                RenderFeedbackBanners();
                _ = new RecoveryCodeDialog("ABCD-EFGH-JKLM-NPQR-STUV");
                _ = new TemporaryPasswordDialog("Cashier One", "Temp-ABCD1234");
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
