using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Covers the My Account / Security screen behaviour: PIN state-awareness (Add vs Change /
/// Remove), authorising with the current password (never the existing PIN), and clear
/// themed success/error feedback that never leaks a secret.
/// </summary>
public class AccountViewModelTests
{
    private const string OwnerPassword = "secret123";

    private static (AccountViewModel Vm, SessionContext Session, FakeDialogService Dialogs, FakeNavigationService Nav, Phase1Environment Env)
        Create(Phase1Environment env, bool mustChange = false)
    {
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, HasPin: false, MustChangePassword: mustChange));
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        var vm = new AccountViewModel(env.AccountSecurity, env.OwnerRecovery, session, dialogs, nav, new FakeThemeService());
        return (vm, session, dialogs, nav, env);
    }

    // ---------- PIN state-aware UI ----------

    [Fact]
    public void AccountWithoutPin_ShowsAddPinState()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        Assert.False(vm.HasPin);
        Assert.Equal("Add PIN", vm.PinCardTitle);
        Assert.Equal("Add PIN", vm.PinActionLabel);
        Assert.True(vm.ShowPinCard);
        Assert.False(vm.ShowRemovePinCard);
    }

    [Fact]
    public void AddPin_UsesCurrentPassword_NotExistingPin_AndEnablesPinLogin()
    {
        using var env = new Phase1Environment();
        var (vm, session, _, _, _) = Create(env);

        // The only credential asked for is the current password — no existing PIN needed.
        vm.PinCurrentPassword = OwnerPassword;
        vm.NewPin = "4321";
        vm.ConfirmPin = "4321";
        vm.ChangePinCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Equal("Your PIN was added successfully.", vm.Feedback.Message);
        Assert.True(vm.HasPin);
        Assert.True(session.CurrentUser!.HasPin);
        Assert.Equal("Change PIN", vm.PinCardTitle);
        Assert.True(vm.ShowRemovePinCard);
        Assert.Equal(string.Empty, vm.PinCurrentPassword);      // fields cleared
        Assert.Equal(string.Empty, vm.NewPin);

        Assert.True(env.Auth.LoginWithPin("owner", "4321").Succeeded);
    }

    [Fact]
    public void AddPin_WithWrongPassword_IsRejected()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        vm.PinCurrentPassword = "not-my-password";
        vm.NewPin = "4321";
        vm.ConfirmPin = "4321";
        vm.ChangePinCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.False(vm.HasPin);
    }

    [Fact]
    public void AddPin_WithMismatchedConfirmation_ShowsError()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        vm.PinCurrentPassword = OwnerPassword;
        vm.NewPin = "4321";
        vm.ConfirmPin = "9999";
        vm.ChangePinCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Equal("The new PINs do not match.", vm.Feedback.Message);
        Assert.False(vm.HasPin);
    }

    [Fact]
    public void AccountWithPin_ShowsChangePinState_AndChangeRequiresCurrentPassword()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        // Give it a PIN first.
        vm.PinCurrentPassword = OwnerPassword;
        vm.NewPin = "4321";
        vm.ConfirmPin = "4321";
        vm.ChangePinCommand.Execute(null);
        Assert.True(vm.HasPin);

        // A change now still authorises with the password, not the old PIN.
        vm.PinCurrentPassword = "wrong";
        vm.NewPin = "5678";
        vm.ConfirmPin = "5678";
        vm.ChangePinCommand.Execute(null);
        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);

        vm.PinCurrentPassword = OwnerPassword;
        vm.NewPin = "5678";
        vm.ConfirmPin = "5678";
        vm.ChangePinCommand.Execute(null);
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Equal("Your PIN was changed successfully.", vm.Feedback.Message);
    }

    // ---------- Remove PIN ----------

    [Fact]
    public void RemovePin_WithoutConfirmation_DoesNothing()
    {
        using var env = new Phase1Environment();
        var (vm, _, dialogs, _, _) = Create(env);
        AddPin(vm);
        dialogs.ConfirmResult = false;

        vm.RemovePinCurrentPassword = OwnerPassword;
        vm.RemovePinCommand.Execute(null);

        Assert.True(vm.HasPin);
        Assert.Null(vm.Feedback.Message);
    }

    [Fact]
    public void RemovePin_RequiresCurrentPassword()
    {
        using var env = new Phase1Environment();
        var (vm, _, dialogs, _, _) = Create(env);
        AddPin(vm);
        dialogs.ConfirmResult = true;

        vm.RemovePinCurrentPassword = "wrong";
        vm.RemovePinCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.True(vm.HasPin);
    }

    [Fact]
    public void RemovePin_Succeeds_DisablesPinLogin_AndReturnsToAddState()
    {
        using var env = new Phase1Environment();
        var (vm, session, dialogs, _, _) = Create(env);
        AddPin(vm);
        dialogs.ConfirmResult = true;

        vm.RemovePinCurrentPassword = OwnerPassword;
        vm.RemovePinCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Equal("Your PIN was removed successfully.", vm.Feedback.Message);
        Assert.False(vm.HasPin);
        Assert.False(session.CurrentUser!.HasPin);
        Assert.Equal("Add PIN", vm.PinCardTitle);
        Assert.False(vm.ShowRemovePinCard);
        Assert.False(env.Auth.LoginWithPin("owner", "4321").Succeeded);
    }

    // ---------- Password ----------

    [Fact]
    public void ChangePassword_Success_ShowsMessage_AndClearsFields()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, nav, _) = Create(env);

        vm.CurrentPassword = OwnerPassword;
        vm.NewPassword = "newpass1";
        vm.ConfirmPassword = "newpass1";
        vm.ChangePasswordCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Equal("Your password was changed successfully.", vm.Feedback.Message);
        Assert.Equal(string.Empty, vm.CurrentPassword);
        Assert.Equal(string.Empty, vm.NewPassword);
        Assert.False(nav.HomeShown); // not forced → stays on the screen
        Assert.True(env.Auth.LoginWithPassword("owner", "newpass1").Succeeded);
    }

    [Fact]
    public void ChangePassword_WrongCurrent_ShowsError()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        vm.CurrentPassword = "wrong";
        vm.NewPassword = "newpass1";
        vm.ConfirmPassword = "newpass1";
        vm.ChangePasswordCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.True(env.Auth.LoginWithPassword("owner", OwnerPassword).Succeeded); // unchanged
    }

    [Fact]
    public void ChangePassword_Mismatch_ShowsError_AndKeepsFields()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        vm.CurrentPassword = OwnerPassword;
        vm.NewPassword = "newpass1";
        vm.ConfirmPassword = "different1";
        vm.ChangePasswordCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Equal("The new passwords do not match.", vm.Feedback.Message);
        Assert.Equal("newpass1", vm.NewPassword); // not cleared, so the slip can be fixed
    }

    [Fact]
    public void ChangePassword_SameAsCurrent_ShowsError()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        vm.CurrentPassword = OwnerPassword;
        vm.NewPassword = OwnerPassword;
        vm.ConfirmPassword = OwnerPassword;
        vm.ChangePasswordCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Contains("different", vm.Feedback.Message);
    }

    [Fact]
    public void ForcedPasswordChange_NavigatesHome_OnSuccess()
    {
        using var env = new Phase1Environment();
        var (vm, _, dialogs, nav, _) = Create(env, mustChange: true);
        Assert.True(vm.IsForcedChange);

        vm.CurrentPassword = OwnerPassword;
        vm.NewPassword = "newpass1";
        vm.ConfirmPassword = "newpass1";
        vm.ChangePasswordCommand.Execute(null);

        Assert.True(nav.HomeShown);
        Assert.NotNull(dialogs.LastInfo);
    }

    // ---------- Secret hygiene ----------

    [Fact]
    public void FeedbackMessages_NeverContainTheSecret()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        vm.PinCurrentPassword = OwnerPassword;
        vm.NewPin = "4321";
        vm.ConfirmPin = "4321";
        vm.ChangePinCommand.Execute(null);
        Assert.DoesNotContain("4321", vm.Feedback.Message);

        vm.CurrentPassword = OwnerPassword;
        vm.NewPassword = "topsecret9";
        vm.ConfirmPassword = "topsecret9";
        vm.ChangePasswordCommand.Execute(null);
        Assert.DoesNotContain("topsecret9", vm.Feedback.Message);
    }

    private static void AddPin(AccountViewModel vm)
    {
        vm.PinCurrentPassword = OwnerPassword;
        vm.NewPin = "4321";
        vm.ConfirmPin = "4321";
        vm.ChangePinCommand.Execute(null);
    }
}
