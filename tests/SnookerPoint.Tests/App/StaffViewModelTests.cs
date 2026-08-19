using System.Linq;
using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Covers the Staff Management screen's credential-feedback: every reset/change reports a
/// clear success or a friendly error through the themed banner, temporary passwords are
/// shown once in a copyable dialog, and no secret leaks into a feedback message.
/// </summary>
public class StaffViewModelTests
{
    private const string GoodPassword = "password1";

    private static (StaffViewModel Vm, FakeDialogService Dialogs, Phase1Environment Env, int OwnerId)
        Create(Phase1Environment env)
    {
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, HasPin: false));
        var dialogs = new FakeDialogService();
        var vm = new StaffViewModel(env.StaffManagement, session, new PermissionService(), dialogs, new FakeNavigationService(), new FakeThemeService());
        return (vm, dialogs, env, ownerId);
    }

    private static int SeedCashier(Phase1Environment env, int ownerId, string username = "cash1") =>
        env.StaffManagement.CreateStaff(new CreateStaffRequest("Cashier One", username, UserRole.Cashier, GoodPassword, null), ownerId).Value;

    private static StaffRowViewModel Row(StaffViewModel vm, string username) =>
        vm.Rows.First(r => r.Username == username);

    [Fact]
    public void CreateStaff_Success_ShowsConfirmation()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _) = Create(env);
        dialogs.StaffEditorResult = new StaffEditInput("New Guy", "newguy", UserRole.Cashier, GoodPassword, null);

        vm.AddStaffCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("created", vm.Feedback.Message);
        Assert.Contains(vm.Rows, r => r.Username == "newguy");
    }

    [Fact]
    public void CreateStaff_DuplicateUsername_ShowsError()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _) = Create(env);
        dialogs.StaffEditorResult = new StaffEditInput("Clash", "owner", UserRole.Cashier, GoodPassword, null);

        vm.AddStaffCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
    }

    [Fact]
    public void ResetPassword_Success_ShowsConfirmation()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId) = Create(env);
        SeedCashier(env, ownerId);
        vm = Rebuild(env, ownerId, dialogs);
        dialogs.SetCredentialResult = new SetCredentialInput("brandnew1");

        vm.ResetPasswordCommand.Execute(Row(vm, "cash1"));

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("Password reset", vm.Feedback.Message);
        Assert.True(env.Auth.LoginWithPassword("cash1", "brandnew1").Succeeded);
    }

    [Fact]
    public void ResetPassword_InvalidPassword_ShowsError()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId) = Create(env);
        SeedCashier(env, ownerId);
        vm = Rebuild(env, ownerId, dialogs);
        dialogs.SetCredentialResult = new SetCredentialInput("123"); // too short

        vm.ResetPasswordCommand.Execute(Row(vm, "cash1"));

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
    }

    [Fact]
    public void SetPin_ThenRemove_ShowConfirmations()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId) = Create(env);
        SeedCashier(env, ownerId);
        vm = Rebuild(env, ownerId, dialogs);

        dialogs.SetCredentialResult = new SetCredentialInput("1234");
        vm.SetPinCommand.Execute(Row(vm, "cash1"));
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("PIN set", vm.Feedback.Message);

        dialogs.SetCredentialResult = new SetCredentialInput(null); // remove
        vm.SetPinCommand.Execute(Row(vm, "cash1"));
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("PIN removed", vm.Feedback.Message);
    }

    [Fact]
    public void TempPassword_ShownOnceInDialog_AndConfirmed_WithoutLeakingIntoBanner()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId) = Create(env);
        SeedCashier(env, ownerId);
        vm = Rebuild(env, ownerId, dialogs);
        dialogs.ConfirmResult = true;

        vm.TempPasswordCommand.Execute(Row(vm, "cash1"));

        Assert.NotNull(dialogs.ShownTemporaryPassword);        // shown once in a copyable dialog
        Assert.Equal("Cashier One", dialogs.ShownTemporaryPasswordStaff);
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.DoesNotContain(dialogs.ShownTemporaryPassword!, vm.Feedback.Message); // banner has no secret
    }

    [Fact]
    public void TempPassword_NotConfirmed_DoesNothing()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId) = Create(env);
        SeedCashier(env, ownerId);
        vm = Rebuild(env, ownerId, dialogs);
        dialogs.ConfirmResult = false;

        vm.TempPasswordCommand.Execute(Row(vm, "cash1"));

        Assert.Null(dialogs.ShownTemporaryPassword);
        Assert.Null(vm.Feedback.Message);
    }

    [Fact]
    public void DisableThenEnable_ShowConfirmations()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId) = Create(env);
        SeedCashier(env, ownerId);
        vm = Rebuild(env, ownerId, dialogs);
        dialogs.ConfirmResult = true;

        vm.ToggleActiveCommand.Execute(Row(vm, "cash1"));
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("disabled", vm.Feedback.Message);

        vm.ToggleActiveCommand.Execute(Row(vm, "cash1"));
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("enabled", vm.Feedback.Message);
    }

    [Fact]
    public void ClearLockout_ShowsConfirmation()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, ownerId) = Create(env);
        SeedCashier(env, ownerId);
        vm = Rebuild(env, ownerId, dialogs);

        vm.ClearLockoutCommand.Execute(Row(vm, "cash1"));

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("unlocked", vm.Feedback.Message);
    }

    /// <summary>Rebuilds the VM so its Rows reflect staff seeded after the first construction.</summary>
    private static StaffViewModel Rebuild(Phase1Environment env, int ownerId, FakeDialogService dialogs)
    {
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, HasPin: false));
        return new StaffViewModel(env.StaffManagement, session, new PermissionService(), dialogs, new FakeNavigationService(), new FakeThemeService());
    }
}
