using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Licensing;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

public class ActivationViewModelTests
{
    private static (ActivationViewModel Vm, FakeLicensingService Lic, FakeDialogService Dialogs, FakeNavigationService Nav)
        Create(Phase1Environment env, bool signedIn, LicenseEvaluation evaluation)
    {
        var session = new SessionContext();
        if (signedIn)
        {
            var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
            session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, false));
        }
        else
        {
            env.SeedOwnerShiftAndTables(12_000);
        }

        var lic = new FakeLicensingService { Evaluation = evaluation };
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        var vm = new ActivationViewModel(lic, env.ClubSettings, env.Backups, env.Health, session,
            dialogs, nav, new FakeThemeService(), new FakeApplicationControl());
        return (vm, lic, dialogs, nav);
    }

    [Fact]
    public void ExpiredTrial_ShowsInstallationCode_AndRecoveryMode()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _) = Create(env, signedIn: false, FakeLicensingService.Expired());

        Assert.True(vm.IsExpiredMode);
        Assert.False(vm.CanGoBack);
        Assert.False(string.IsNullOrWhiteSpace(vm.InstallationCode));
    }

    [Fact]
    public void SuccessfulActivation_PassesPastedText_AndContinues()
    {
        using var env = new Phase1Environment();
        var (vm, lic, dialogs, nav) = Create(env, signedIn: false, FakeLicensingService.Expired());
        lic.ActivateResult = new ActivationOutcome(true, LicenseStatus.Licensed, "ACTIVATED", "Snooker Point was activated successfully.");
        vm.LicenseTextInput = "PASTED-LICENCE-TEXT";

        vm.ActivateCommand.Execute(null);

        Assert.Equal("PASTED-LICENCE-TEXT", lic.LastActivateText);
        Assert.True(nav.LoginShown);       // no user signed in → return to login
        Assert.NotNull(dialogs.LastInfo);
    }

    [Fact]
    public void FailedActivation_ShowsFriendlyError_AndDoesNotContinue()
    {
        using var env = new Phase1Environment();
        var (vm, lic, _, nav) = Create(env, signedIn: false, FakeLicensingService.Expired());
        lic.ActivateResult = new ActivationOutcome(false, LicenseStatus.MachineMismatch, "MACHINE_MISMATCH", "This licence was created for another computer.");
        vm.LicenseTextInput = "SOME-LICENCE";

        vm.ActivateCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Equal("This licence was created for another computer.", vm.Feedback.Message);
        Assert.False(nav.LoginShown);
    }

    [Fact]
    public void ActiveTrial_AllowsGoingBack()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _) = Create(env, signedIn: false, FakeLicensingService.ActiveTrial());

        Assert.False(vm.IsExpiredMode);
        Assert.True(vm.CanGoBack);
    }
}
