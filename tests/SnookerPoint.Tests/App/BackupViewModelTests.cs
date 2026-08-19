using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

public class BackupViewModelTests
{
    private static (BackupViewModel Vm, FakeDialogService Dialogs, FakeNavigationService Nav, FakeApplicationControl App)
        Create(Phase1Environment env, int ownerId)
    {
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, HasPin: false));
        var dialogs = new FakeDialogService { ConfirmResult = true };
        var nav = new FakeNavigationService();
        var appControl = new FakeApplicationControl();
        var vm = new BackupViewModel(env.Backups, session, new PermissionService(), dialogs, nav, new FakeThemeService(), appControl);
        return (vm, dialogs, nav, appControl);
    }

    [Fact]
    public void SuccessfulRestore_RequestsApplicationRestart()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        env.Backups.CreateBackup(null, "before", ownerId);

        var (vm, _, nav, app) = Create(env, ownerId);
        vm.RestoreConfirmation = "RESTORE";
        vm.RestoreCommand.Execute(vm.Backups.First());

        Assert.True(app.RestartRequested);
        Assert.False(nav.LoginShown); // a successful restart does not fall back to the login screen
    }

    [Fact]
    public void FailedRestore_DoesNotRequestRestart_AndKeepsData()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        env.SeedProduct(ownerId, shiftId, "ORIGINAL", 60);
        env.Backups.CreateBackup(null, "before", ownerId);

        var (vm, _, _, app) = Create(env, ownerId);
        vm.RestoreConfirmation = "wrong-phrase"; // the service rejects this

        vm.RestoreCommand.Execute(vm.Backups.First());

        Assert.False(app.RestartRequested);
        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        using var db = env.NewContext();
        Assert.True(db.Products.Any(p => p.Sku == "ORIGINAL")); // data preserved
    }

    [Fact]
    public void RestartFailure_ShowsFriendlyManualReopenMessage()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        env.Backups.CreateBackup(null, "before", ownerId);

        var (vm, dialogs, nav, app) = Create(env, ownerId);
        app.RestartResult = false; // the new instance could not be started
        vm.RestoreConfirmation = "RESTORE";

        vm.RestoreCommand.Execute(vm.Backups.First());

        Assert.True(app.RestartRequested);
        Assert.NotNull(dialogs.LastInfo);
        Assert.Contains("could not restart", dialogs.LastInfo!, StringComparison.OrdinalIgnoreCase);
        Assert.True(nav.LoginShown); // falls back to a safe signed-out state
    }

    [Fact]
    public void Restart_PassesNoArguments_SoNoSecretsCanLeak()
    {
        // The restart abstraction takes no parameters, so no secret (password/PIN/confirmation)
        // can ever be passed to the new process on the command line.
        var method = typeof(IApplicationControl).GetMethod(nameof(IApplicationControl.RestartApplication));
        Assert.NotNull(method);
        Assert.Empty(method!.GetParameters());
    }
}
