using SnookerPoint.App.Navigation;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>Records how it was called and returns a configurable result.</summary>
public sealed class FakeAuthenticationService : IAuthenticationService
{
    public string? LastMethod { get; private set; }
    public string? LastUsername { get; private set; }
    public string? LastSecret { get; private set; }
    public int LogoutUserId { get; private set; } = -1;

    public bool ShouldSucceed { get; set; }

    public LoginResult LoginWithPassword(string username, string password)
    {
        LastMethod = "password";
        LastUsername = username;
        LastSecret = password;
        return Build();
    }

    public LoginResult LoginWithPin(string username, string pin)
    {
        LastMethod = "pin";
        LastUsername = username;
        LastSecret = pin;
        return Build();
    }

    public void Logout(int userId) => LogoutUserId = userId;

    private LoginResult Build() =>
        ShouldSucceed
            ? LoginResult.Success(new AuthenticatedUser(1, "The Owner", LastUsername ?? "owner", UserRole.Owner, true))
            : LoginResult.Failure(LoginFailureReason.InvalidCredentials);
}

/// <summary>Records which screen was requested.</summary>
public sealed class FakeNavigationService : INavigationService
{
    public bool HomeShown { get; private set; }
    public bool LoginShown { get; private set; }
    public bool WizardShown { get; private set; }

    public bool TablesShown { get; private set; }
    public bool HistoryShown { get; private set; }
    public bool ManageTablesShown { get; private set; }
    public bool StaffShown { get; private set; }
    public bool AccountShown { get; private set; }
    public bool ProductsShown { get; private set; }
    public bool CategoriesShown { get; private set; }
    public bool InventoryShown { get; private set; }
    public bool NewSaleShown { get; private set; }
    public bool SalesHistoryShown { get; private set; }
    public bool BookingsShown { get; private set; }
    public bool ReportsShown { get; private set; }
    public bool BackupShown { get; private set; }
    public bool SettingsShown { get; private set; }
    public bool AdminShown { get; private set; }
    public bool AuditShown { get; private set; }
    public bool ActivationShown { get; private set; }

    public void ShowSetupWizard() => WizardShown = true;
    public void ShowLogin() => LoginShown = true;
    public void ShowHome() => HomeShown = true;
    public void ShowTables() => TablesShown = true;
    public void ShowSessionHistory() => HistoryShown = true;
    public void ShowManageTables() => ManageTablesShown = true;
    public void ShowStaff() => StaffShown = true;
    public void ShowAccount() => AccountShown = true;
    public void ShowProducts() => ProductsShown = true;
    public void ShowCategories() => CategoriesShown = true;
    public void ShowInventory() => InventoryShown = true;
    public void ShowNewSale() => NewSaleShown = true;
    public void ShowSalesHistory() => SalesHistoryShown = true;
    public void ShowBookings() => BookingsShown = true;
    public void ShowReports() => ReportsShown = true;
    public void ShowBackup() => BackupShown = true;
    public void ShowSettings() => SettingsShown = true;
    public void ShowAdmin() => AdminShown = true;
    public void ShowAudit() => AuditShown = true;
    public void ShowActivation() => ActivationShown = true;
}

/// <summary>A restart controller that records the request instead of relaunching the process.</summary>
public sealed class FakeApplicationControl : SnookerPoint.App.Services.IApplicationControl
{
    /// <summary>What <see cref="RestartApplication"/> returns (true = a new instance started).</summary>
    public bool RestartResult { get; set; } = true;

    public int RestartCallCount { get; private set; }

    public bool RestartRequested => RestartCallCount > 0;

    public bool RestartApplication()
    {
        RestartCallCount++;
        return RestartResult;
    }

    public int ExitCallCount { get; private set; }

    public void Exit() => ExitCallCount++;
}

/// <summary>A scriptable licensing service for view-model tests.</summary>
public sealed class FakeLicensingService : SnookerPoint.App.Licensing.ILicensingService
{
    public SnookerPoint.Licensing.MachineFingerprint Fingerprint { get; set; } =
        new(1, "TESTHASH", "TEST-CODE-1234-5678");

    public SnookerPoint.Licensing.LicenseEvaluation Evaluation { get; set; } = ActiveTrial();

    public SnookerPoint.Licensing.ActivationOutcome ActivateResult { get; set; } =
        new(true, SnookerPoint.Licensing.LicenseStatus.Licensed, "ACTIVATED", "Snooker Point was activated successfully.");

    public string? LastActivateText { get; private set; }
    public bool TrialStartRequested { get; private set; }

    public SnookerPoint.Licensing.LicenseEvaluation Evaluate() => Evaluation;

    public bool StartTrialIfNeeded()
    {
        TrialStartRequested = true;
        return true;
    }

    public SnookerPoint.Licensing.ActivationOutcome Activate(string? licenseText)
    {
        LastActivateText = licenseText;
        return ActivateResult;
    }

    public SnookerPoint.Licensing.MachineFingerprint GetFingerprint() => Fingerprint;

    public static SnookerPoint.Licensing.LicenseEvaluation ActiveTrial() => new(
        SnookerPoint.Licensing.LicenseStatus.Active,
        new SnookerPoint.Licensing.MachineFingerprint(1, "TESTHASH", "TEST-CODE-1234-5678"),
        "2 days 4 hours remaining.", TimeSpan.FromHours(52),
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(52), null, false, "TRIAL_ACTIVE");

    public static SnookerPoint.Licensing.LicenseEvaluation Expired() => new(
        SnookerPoint.Licensing.LicenseStatus.Expired,
        new SnookerPoint.Licensing.MachineFingerprint(1, "TESTHASH", "TEST-CODE-1234-5678"),
        "Your trial has ended.", TimeSpan.Zero,
        DateTimeOffset.UtcNow.AddHours(-100), DateTimeOffset.UtcNow.AddHours(-28), null, false, "TRIAL_EXPIRED");

    public static SnookerPoint.Licensing.LicenseEvaluation Licensed() => new(
        SnookerPoint.Licensing.LicenseStatus.Licensed,
        new SnookerPoint.Licensing.MachineFingerprint(1, "TESTHASH", "TEST-CODE-1234-5678"),
        "Licensed", null, null, null,
        new SnookerPoint.Licensing.LicensePayload(1, "SNOOKERPOINT", "LIC-1", "Club", "TESTHASH", DateTimeOffset.UtcNow, SnookerPoint.Licensing.LicenseType.Lifetime, null, null, "ECDSA_P256_SHA256"),
        false, "LICENSED");
}

/// <summary>A scriptable central licensing gate for view-model tests.</summary>
public sealed class FakeLicenseGate : SnookerPoint.App.Licensing.ILicenseGate
{
    public bool Allow { get; set; } = true;
    public int EnsureCanOperateCalls { get; private set; }
    public int EvaluateCalls { get; private set; }

    public SnookerPoint.Licensing.LicenseEvaluation Evaluate()
    {
        EvaluateCalls++;
        return Allow ? FakeLicensingService.ActiveTrial() : FakeLicensingService.Expired();
    }

    public bool OperationsAllowed => Allow;

    public bool EnsureCanOperate()
    {
        EnsureCanOperateCalls++;
        return Allow;
    }
}

/// <summary>A no-op owner-recovery service for view-model tests.</summary>
public sealed class FakeOwnerRecoveryService : IOwnerRecoveryService
{
    public OwnerRecoveryStatus GetStatus() => new(false, false);
    public bool NeedsRecoveryCodePrompt(int userId) => false;
    public OperationResult<string> RegenerateCode(int ownerUserId, string currentPassword) =>
        OperationResult<string>.Failure("not supported in tests");
    public OperationResult<OwnerRecoveryResult> Recover(string username, string recoveryCode, string newPassword, string? newPin) =>
        OperationResult<OwnerRecoveryResult>.Failure("not supported in tests");
}
