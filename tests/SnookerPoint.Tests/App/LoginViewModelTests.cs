using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

public class LoginViewModelTests
{
    private static (LoginViewModel Vm, FakeAuthenticationService Auth, SessionContext Session, FakeNavigationService Nav) Create()
    {
        var auth = new FakeAuthenticationService();
        var session = new SessionContext();
        var nav = new FakeNavigationService();
        var recovery = new FakeOwnerRecoveryService();
        var dialogs = new FakeDialogService();
        return (new LoginViewModel(auth, session, nav, recovery, dialogs), auth, session, nav);
    }

    [Fact]
    public void PasswordMode_SubmitsThePassword()
    {
        var (vm, auth, _, _) = Create();
        vm.Username = "owner";
        vm.Password = "pw12345";

        vm.LoginCommand.Execute(null);

        Assert.Equal("password", auth.LastMethod);
        Assert.Equal("owner", auth.LastUsername);
        Assert.Equal("pw12345", auth.LastSecret);
    }

    [Fact]
    public void PinMode_SubmitsThePin_NotThePassword()
    {
        var (vm, auth, _, _) = Create();
        vm.Username = "owner";
        vm.Password = "pw12345";     // typed in password mode
        vm.UsePin = true;            // switch to PIN — password should be cleared
        vm.Pin = "4321";

        vm.LoginCommand.Execute(null);

        Assert.Equal("pin", auth.LastMethod);
        Assert.Equal("4321", auth.LastSecret);
        Assert.Equal(string.Empty, vm.Password); // no cross-use
    }

    [Fact]
    public void SwitchingModes_ClearsPreviousCredential_ButKeepsUsername()
    {
        var (vm, _, _, _) = Create();
        vm.Username = "boss";
        vm.Password = "secret";

        vm.UsePin = true;
        Assert.Equal(string.Empty, vm.Password);
        Assert.Equal("boss", vm.Username);

        vm.Pin = "1234";
        vm.UsePin = false;
        Assert.Equal(string.Empty, vm.Pin);
        Assert.Equal("boss", vm.Username);
    }

    [Fact]
    public void ShowSecretLabel_ReflectsModeAndRevealState()
    {
        var (vm, _, _, _) = Create();

        Assert.Equal("Show password", vm.ShowSecretLabel);
        vm.ShowPassword = true;
        Assert.Equal("Hide password", vm.ShowSecretLabel);

        vm.UsePin = true; // switching resets reveal
        Assert.Equal("Show PIN", vm.ShowSecretLabel);
    }

    [Fact]
    public void SuccessfulLogin_SignsIn_ClearsSecrets_AndNavigatesHome()
    {
        var (vm, auth, session, nav) = Create();
        auth.ShouldSucceed = true;
        vm.Username = "owner";
        vm.Password = "pw12345";

        vm.LoginCommand.Execute(null);

        Assert.True(session.IsAuthenticated);
        Assert.True(nav.HomeShown);
        Assert.Equal(string.Empty, vm.Password);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void FailedLogin_ShowsFriendlyError_AndDoesNotNavigate()
    {
        var (vm, auth, session, nav) = Create();
        auth.ShouldSucceed = false;
        vm.Username = "owner";
        vm.Password = "wrong";

        vm.LoginCommand.Execute(null);

        Assert.False(session.IsAuthenticated);
        Assert.False(nav.HomeShown);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("incorrect", vm.ErrorMessage!);
    }

    [Fact]
    public void EmptyUsername_ShowsError_AndDoesNotCallAuth()
    {
        var (vm, auth, _, _) = Create();
        vm.Username = "   ";
        vm.Password = "whatever";

        vm.LoginCommand.Execute(null);

        Assert.Null(auth.LastMethod);
        Assert.NotNull(vm.ErrorMessage);
    }
}
