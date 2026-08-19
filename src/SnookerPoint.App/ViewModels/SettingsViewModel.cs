using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Settings;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Sales;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// Operational settings, organised into sections: club profile, tax &amp; service charge (0%
/// and disabled by default; new sales only), automatic backups, and quick links to the other
/// management areas. Each save is permission-gated, validated and audited. A live checkout
/// preview shows how tax/service would affect a sample bill before saving.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private const long SampleBillRupees = 1000;

    private readonly IOperationalSettingsService _settings;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly SnookerPoint.App.Licensing.ILicensingService _licensing;
    private readonly SnookerPoint.App.Licensing.ILicenseGate _gate;

    public SettingsViewModel(
        IOperationalSettingsService settings,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme,
        SnookerPoint.App.Licensing.ILicensingService licensing,
        SnookerPoint.App.Licensing.ILicenseGate gate)
    {
        _settings = settings;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _licensing = licensing;
        _gate = gate;

        ReceiptWidths = new[] { 58, 80 };
        Load();
        LoadLicence();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public IReadOnlyList<int> ReceiptWidths { get; }

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    public bool CanManageSettings => Has(Permission.ManageSettings);
    public bool CanConfigureTax => Has(Permission.ConfigureTaxService);
    public bool CanManageBackupSettings => Has(Permission.ManageBackupSettings);

    // ---------- Licence section ----------
    // Owner and Administrator (ManageSettings) may view licence details; only the Owner may
    // replace an already-valid lifetime licence by default.
    public bool CanViewLicence => Has(Permission.ManageSettings);
    public bool CanReplaceLicence => _session.CurrentUser?.Role == UserRole.Owner;

    [ObservableProperty] private string _licenceStatus = string.Empty;
    [ObservableProperty] private string _licenceDetail = string.Empty;
    [ObservableProperty] private string _installationCode = string.Empty;
    [ObservableProperty] private bool _isLicensed;

    public string ActivateButtonText => IsLicensed ? "Replace / re-import licence" : "Activate licence";
    public bool ShowActivateButton => IsLicensed ? CanReplaceLicence : CanViewLicence;

    private void LoadLicence()
    {
        var e = _licensing.Evaluate();
        InstallationCode = e.Machine.InstallationCode;
        IsLicensed = e.Status == SnookerPoint.Licensing.LicenseStatus.Licensed;

        if (IsLicensed && e.License is { } lic)
        {
            LicenceStatus = "Activated — Lifetime licence";
            LicenceDetail = $"Licence ID: {lic.LicenseId}\nCustomer/club: {lic.CustomerName}\nIssued: {lic.IssuedUtc.ToLocalTime():dd MMM yyyy}\nMachine status: Matches this computer";
        }
        else if (e.Status is SnookerPoint.Licensing.LicenseStatus.Active or SnookerPoint.Licensing.LicenseStatus.ExpiringSoon)
        {
            LicenceStatus = "Free trial";
            LicenceDetail = e.FriendlyRemaining +
                (e.TrialStartUtc is { } s ? $"\nStarted: {s.ToLocalTime():dd MMM yyyy}" : string.Empty) +
                (e.TrialExpiryUtc is { } x ? $"\nExpires: {x.ToLocalTime():dd MMM yyyy}" : string.Empty);
        }
        else
        {
            LicenceStatus = "Not activated";
            LicenceDetail = "Activate Snooker Point to unlock the app after the trial.";
        }

        OnPropertyChanged(nameof(ActivateButtonText));
        OnPropertyChanged(nameof(ShowActivateButton));
    }

    [RelayCommand]
    private void OpenActivation() => _navigation.ShowActivation();

    // Club profile
    [ObservableProperty] private string _clubName = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private int _receiptWidth = 58;
    [ObservableProperty] private bool _autoPrintReceipt;

    // Tax & service
    [ObservableProperty] private bool _taxEnabled;
    [ObservableProperty] private string _taxPercent = "0";
    [ObservableProperty] private bool _serviceEnabled;
    [ObservableProperty] private string _servicePercent = "0";

    // Backups
    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private bool _autoBackupDaily;
    [ObservableProperty] private bool _autoBackupOnClose;
    [ObservableProperty] private string _retention = "7";
    [ObservableProperty] private string _backupFolder = string.Empty;
    [ObservableProperty] private string _lastAutoBackup = string.Empty;

    // Tax/service preview
    [ObservableProperty] private string _previewText = string.Empty;

    private int UserId => _session.CurrentUser!.Id;

    partial void OnTaxEnabledChanged(bool value) => UpdatePreview();
    partial void OnTaxPercentChanged(string value) => UpdatePreview();
    partial void OnServiceEnabledChanged(bool value) => UpdatePreview();
    partial void OnServicePercentChanged(string value) => UpdatePreview();

    private void Load()
    {
        var s = _settings.Get();
        if (s is null)
        {
            return;
        }

        ClubName = s.ClubName;
        Address = s.Address ?? string.Empty;
        Phone = s.Phone ?? string.Empty;
        ReceiptWidth = s.ReceiptWidthMm;
        AutoPrintReceipt = s.AutoPrintReceipt;

        TaxEnabled = s.TaxEnabled;
        TaxPercent = s.TaxPercent.ToString(CultureInfo.InvariantCulture);
        ServiceEnabled = s.ServiceChargeEnabled;
        ServicePercent = s.ServiceChargePercent.ToString(CultureInfo.InvariantCulture);

        AutoBackupEnabled = s.AutoBackupEnabled;
        AutoBackupDaily = s.AutoBackupDaily;
        AutoBackupOnClose = s.AutoBackupOnClose;
        Retention = s.AutoBackupRetention.ToString(CultureInfo.InvariantCulture);
        BackupFolder = s.BackupFolder ?? string.Empty;
        LastAutoBackup = s.LastAutoBackupUtc?.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture) ?? "Never";

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var tax = TaxEnabled && decimal.TryParse(TaxPercent, NumberStyles.Any, CultureInfo.InvariantCulture, out var tp) ? tp : 0m;
        var service = ServiceEnabled && decimal.TryParse(ServicePercent, NumberStyles.Any, CultureInfo.InvariantCulture, out var sp) ? sp : 0m;
        var totals = SaleMath.ComputeWithRates(Money.FromRupees(SampleBillRupees), Money.Zero, DiscountKind.None, 0, tax, service);
        PreviewText = $"Sample bill {Money.FromRupees(SampleBillRupees).Format()}  +  tax {totals.Tax.Format()}  +  service {totals.Service.Format()}  =  {totals.Total.Format()}";
    }

    [RelayCommand]
    private void SaveProfile()
    {
        Feedback.Clear();
        if (!_gate.EnsureCanOperate() || !Confirm("Save club profile?"))
        {
            return;
        }

        var result = _settings.UpdateClubProfile(
            new ClubProfileInput(ClubName, Nullable(Address), Nullable(Phone), ReceiptWidth, AutoPrintReceipt), UserId);
        Report(result, "Club profile saved.");
    }

    [RelayCommand]
    private void SaveTaxService()
    {
        Feedback.Clear();
        if (!_gate.EnsureCanOperate())
        {
            return;
        }

        if (!decimal.TryParse(TaxPercent, NumberStyles.Any, CultureInfo.InvariantCulture, out var tp) ||
            !decimal.TryParse(ServicePercent, NumberStyles.Any, CultureInfo.InvariantCulture, out var sp))
        {
            Feedback.Error("Enter tax and service percentages as numbers (e.g. 5).");
            return;
        }

        if (!_dialogs.Confirm("Update tax & service",
                $"Apply these charges to NEW sales only?\n\n{PreviewText}"))
        {
            return;
        }

        var result = _settings.UpdateTaxService(new TaxServiceInput(TaxEnabled, tp, ServiceEnabled, sp), UserId);
        Report(result, "Tax and service charge saved. This affects new sales only.");
    }

    [RelayCommand]
    private void SaveBackupSettings()
    {
        Feedback.Clear();
        if (!_gate.EnsureCanOperate())
        {
            return;
        }

        if (!int.TryParse(Retention, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keep) || keep < 1)
        {
            Feedback.Error("Keep at least one backup (retention must be 1 or more).");
            return;
        }

        var result = _settings.UpdateBackupSettings(
            new BackupSettingsInput(AutoBackupEnabled, AutoBackupDaily, AutoBackupOnClose, keep, Nullable(BackupFolder)), UserId);
        Report(result, "Backup settings saved.");
    }

    [RelayCommand]
    private void ChooseBackupFolder()
    {
        var picked = _dialogs.PickFolder(string.IsNullOrWhiteSpace(BackupFolder) ? null : BackupFolder);
        if (picked is not null)
        {
            BackupFolder = picked;
        }
    }

    [RelayCommand] private void OpenTables() => _navigation.ShowManageTables();
    [RelayCommand] private void OpenStaff() => _navigation.ShowStaff();
    [RelayCommand] private void OpenAudit() => _navigation.ShowAudit();
    [RelayCommand] private void OpenBackup() => _navigation.ShowBackup();
    [RelayCommand] private void OpenAdmin() => _navigation.ShowAdmin();
    [RelayCommand] private void OpenAccount() => _navigation.ShowAccount();
    [RelayCommand] private void Home() => _navigation.ShowHome();
    [RelayCommand] private void ToggleTheme() => _theme.Toggle();

    private bool Confirm(string message) => _dialogs.Confirm("Save settings", message);

    private void Report(SnookerPoint.Application.Common.OperationResult result, string success)
    {
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Load();
        Feedback.Success(success);
    }

    private static string? Nullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool Has(Permission p) => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, p);
}
