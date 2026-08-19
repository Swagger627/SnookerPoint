using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Licensing;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Backups;
using SnookerPoint.Application.Diagnostics;
using SnookerPoint.Application.Settings;
using SnookerPoint.Licensing;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The Activation screen. Shows trial status, the copyable Installation Code and the club name,
/// and lets the user import or paste a signed offline licence to activate. When the trial has
/// expired it also offers the only permitted actions — back up data, restore a backup, view
/// diagnostics, and exit — while all operational actions stay blocked (business data is never
/// touched). No cryptographic jargon or raw machine identifiers are shown.
/// </summary>
public partial class ActivationViewModel : ObservableObject
{
    private readonly ILicensingService _licensing;
    private readonly IClubSettingsService _settings;
    private readonly IBackupService _backups;
    private readonly IDatabaseHealthService _health;
    private readonly ISessionContext _session;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IApplicationControl _appControl;

    public ActivationViewModel(
        ILicensingService licensing,
        IClubSettingsService settings,
        IBackupService backups,
        IDatabaseHealthService health,
        ISessionContext session,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme,
        IApplicationControl appControl)
    {
        _licensing = licensing;
        _settings = settings;
        _backups = backups;
        _health = health;
        _session = session;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _appControl = appControl;

        Load();
    }

    public FeedbackViewModel Feedback { get; } = new();

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _remainingText = string.Empty;
    [ObservableProperty] private string _clubName = "Snooker Point";
    [ObservableProperty] private string _installationCode = string.Empty;
    [ObservableProperty] private string _licenseTextInput = string.Empty;
    [ObservableProperty] private bool _isExpiredMode;
    [ObservableProperty] private bool _canGoBack;

    private void Load()
    {
        var evaluation = _licensing.Evaluate();
        InstallationCode = evaluation.Machine.InstallationCode;
        ClubName = _settings.Get()?.ClubName ?? "Snooker Point";
        StatusText = DescribeStatus(evaluation.Status);
        RemainingText = evaluation.Status is LicenseStatus.Active or LicenseStatus.ExpiringSoon ? evaluation.FriendlyRemaining : string.Empty;

        // Blocked states must activate; a still-valid trial may return to the app.
        IsExpiredMode = !evaluation.OperationsAllowed;
        CanGoBack = evaluation.OperationsAllowed;
    }

    [RelayCommand]
    private void CopyInstallationCode()
    {
        try
        {
            System.Windows.Clipboard.SetText(InstallationCode);
            Feedback.Success("Installation Code copied.");
        }
        catch (Exception)
        {
            Feedback.Warning("Could not copy automatically — please select and copy the code shown.");
        }
    }

    [RelayCommand]
    private void ImportLicence()
    {
        var path = _dialogs.PickOpenFile("Import licence", "Licence files (*.spl;*.txt)|*.spl;*.txt|All files (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        try
        {
            LicenseTextInput = System.IO.File.ReadAllText(path);
            Feedback.Success("Licence file loaded. Click Activate to continue.");
        }
        catch (Exception)
        {
            Feedback.Error("That licence file could not be read.");
        }
    }

    [RelayCommand]
    private void Activate()
    {
        Feedback.Clear();
        if (string.IsNullOrWhiteSpace(LicenseTextInput))
        {
            Feedback.Warning("Paste your licence text or import a licence file first.");
            return;
        }

        var outcome = _licensing.Activate(LicenseTextInput);
        if (!outcome.Success)
        {
            Feedback.Error(outcome.Message);
            return;
        }

        _dialogs.ShowInfo("Activation complete", "Snooker Point was activated successfully.");
        Continue();
    }

    [RelayCommand]
    private void CreateBackup()
    {
        Feedback.Clear();
        var result = _backups.CreateBackup(null, "Backup from activation screen", actorUserId: 0);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Feedback.Success($"Backup created: {result.Value!.FileName}");
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        Feedback.Clear();
        var path = _dialogs.PickOpenFile("Restore backup", "Snooker Point backups (*.spbak)|*.spbak|All files (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        var validation = _backups.Validate(path);
        if (!validation.IsValid)
        {
            Feedback.Error($"This backup cannot be restored: {validation.Message}");
            return;
        }

        if (!_dialogs.Confirm("Restore backup", "This will replace current data with the backup, then restart Snooker Point. A safety backup is made first. Continue?"))
        {
            return;
        }

        var result = _backups.RestoreBackup(path, BackupConfirmationPhrase, actorUserId: 0);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        if (!_appControl.RestartApplication())
        {
            _dialogs.ShowInfo("Restore complete", "Your data was restored. Please reopen Snooker Point.");
        }
    }

    [RelayCommand]
    private void Diagnostics()
    {
        Feedback.Clear();
        var result = _health.CreateDiagnosticSummary(null, actorUserId: 0);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Feedback.Success($"Diagnostic summary saved: {result.Value}");
    }

    [RelayCommand]
    private void Back()
    {
        if (CanGoBack)
        {
            Continue();
        }
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    [RelayCommand]
    private void Exit() => _appControl.Exit();

    private void Continue()
    {
        if (_session.CurrentUser is not null)
        {
            _navigation.ShowHome();
        }
        else
        {
            _navigation.ShowLogin();
        }
    }

    private static string DescribeStatus(LicenseStatus status) => status switch
    {
        LicenseStatus.Active => "You are using the free trial.",
        LicenseStatus.ExpiringSoon => "Your trial is ending soon.",
        LicenseStatus.Expired => "Your trial has ended. Activate Snooker Point to continue.",
        LicenseStatus.Licensed => "Snooker Point is activated.",
        LicenseStatus.MachineMismatch => "This licence was created for another computer.",
        LicenseStatus.InvalidLicense => "The saved licence could not be verified. Please activate again.",
        LicenseStatus.LicenseStateError => "Your licence information needs attention. Please activate.",
        _ => "Activate Snooker Point to continue.",
    };

    // The restore confirmation is provided by the explicit Confirm dialog above.
    private const string BackupConfirmationPhrase = "RESTORE";
}
