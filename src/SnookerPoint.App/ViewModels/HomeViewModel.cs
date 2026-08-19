using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Audit;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Bookings;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Settings;
using SnookerPoint.Application.Shifts;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The Phase 1 logged-in home shell: shift status and actions, cash movements, a
/// beginner-friendly layout with "Coming soon" cards for future modules, and an
/// advanced view (settings summary + Phase 1 audit log) for permitted roles.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly ISessionContext _session;
    private readonly IShiftService _shiftService;
    private readonly IAuthenticationService _auth;
    private readonly IPermissionService _permissions;
    private readonly IClubSettingsService _settingsService;
    private readonly IAuditQueryService _auditService;
    private readonly IOwnerRecoveryService _ownerRecovery;
    private readonly IBookingService _bookingService;
    private readonly SnookerPoint.App.Licensing.ILicensingService _licensing;
    private readonly SnookerPoint.App.Licensing.ILicenseGate _gate;
    private readonly IThemeService _theme;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    private ShiftSummary? _shift;

    public HomeViewModel(
        ISessionContext session,
        IShiftService shiftService,
        IAuthenticationService auth,
        IPermissionService permissions,
        IClubSettingsService settingsService,
        IAuditQueryService auditService,
        IOwnerRecoveryService ownerRecovery,
        IBookingService bookingService,
        SnookerPoint.App.Licensing.ILicensingService licensing,
        SnookerPoint.App.Licensing.ILicenseGate gate,
        IThemeService theme,
        IDialogService dialogs,
        INavigationService navigation)
    {
        _session = session;
        _shiftService = shiftService;
        _auth = auth;
        _permissions = permissions;
        _settingsService = settingsService;
        _auditService = auditService;
        _ownerRecovery = ownerRecovery;
        _bookingService = bookingService;
        _licensing = licensing;
        _gate = gate;
        _theme = theme;
        _dialogs = dialogs;
        _navigation = navigation;

        LoadSettingsSummary();
        LoadUpcomingBookings();
        LoadLicensing();
        RefreshShift();

        // One-time, non-blocking nudge for an Owner who has no recovery code yet.
        ShowRecoveryPrompt = _session.CurrentUser is { } u && _ownerRecovery.NeedsRecoveryCodePrompt(u.Id);
    }

    [ObservableProperty] private bool _showRecoveryPrompt;

    // ---------- Trial banner (restrained) ----------
    [ObservableProperty] private bool _showTrialBanner;
    [ObservableProperty] private bool _trialUrgent;
    [ObservableProperty] private string _trialBannerText = string.Empty;

    private void LoadLicensing()
    {
        var evaluation = _licensing.Evaluate();
        // Only trial states show a banner; a licensed install shows nothing.
        ShowTrialBanner = evaluation.Status is SnookerPoint.Licensing.LicenseStatus.Active or SnookerPoint.Licensing.LicenseStatus.ExpiringSoon;
        TrialUrgent = evaluation.Status == SnookerPoint.Licensing.LicenseStatus.ExpiringSoon;
        TrialBannerText = evaluation.FriendlyRemaining;
    }

    [RelayCommand]
    private void ActivateLicence() => _navigation.ShowActivation();

    private AuthenticatedUser User => _session.CurrentUser
        ?? throw new InvalidOperationException("No signed-in user.");

    // ---------- Identity ----------

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => DescribeRole(_session.CurrentUser?.Role);
    public string ClubName { get; private set; } = "Snooker Point";

    // ---------- Permissions / mode ----------

    public bool CanAccessAdvanced =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.AccessAdvancedMode);

    public bool CanViewAudit =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ViewAuditLog);

    public bool CanManageTables =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageTables);

    public bool CanManageStaff =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageStaff);

    public bool CanViewTables =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ViewTables);

    public bool CanViewProducts =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ViewProducts);

    public bool CanManageProducts =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageProducts);

    public bool CanViewInventory =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ViewInventory);

    public bool CanCreateSale =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.CreateSale);

    public bool CanViewSalesHistory =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ViewSalesHistory);

    public bool CanViewBookings =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ViewBookings);

    public bool CanViewReports =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ViewReports);

    public bool CanManageSettings =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageSettings);

    public bool CanCreateBackup =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.CreateBackup);

    public bool CanRunAdmin =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.RunDatabaseHealthCheck);

    /// <summary>Whether to show the Management section at all (any management capability).</summary>
    public bool CanAccessManagement => CanManageTables || CanManageStaff || CanManageSettings || CanCreateBackup || CanRunAdmin || CanViewAudit;

    [ObservableProperty] private bool _isAdvancedMode;

    partial void OnIsAdvancedModeChanged(bool value)
    {
        if (value)
        {
            LoadAudit();
        }
    }

    // ---------- Shift status ----------

    public bool HasOpenShift => _shift is not null;
    public string ShiftStatusText => _shift is null ? "No open shift" : "Shift open";

    public string OpenedText => _shift is null ? "—" : _shift.OpenedUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt");
    public string OpeningCashText => (_shift?.OpeningCash)?.Format() ?? "—";
    public string CashInText => (_shift?.CashInTotal)?.Format() ?? "—";
    public string CashOutText => (_shift?.CashOutTotal)?.Format() ?? "—";
    public string ExpenseText => (_shift?.ExpenseTotal)?.Format() ?? "—";
    public string DropText => (_shift?.DropTotal)?.Format() ?? "—";
    public string ExpectedCashText => (_shift?.ExpectedCash)?.Format() ?? "—";
    public string? OpeningNote => _shift?.OpeningNote;

    public ObservableCollection<CashMovementLine> CashMovements { get; } = new();

    // ---------- Action availability ----------

    public bool CanOpenShift =>
        !HasOpenShift && _permissions.HasPermission(User.Role, Permission.OpenShift);

    public bool CanCloseShift =>
        HasOpenShift && _permissions.HasPermission(User.Role, Permission.CloseShift);

    public bool CanRecordCash =>
        HasOpenShift && _permissions.HasPermission(User.Role, Permission.RecordCashMovement);

    // ---------- Settings summary (advanced) ----------

    public string SettingsSummary { get; private set; } = string.Empty;
    public string BackupSummary { get; private set; } = string.Empty;

    // ---------- Future modules ----------

    // Only genuinely-unimplemented daily modules remain here; New Sale, Products,
    // Inventory, Tables, Session History and Bookings are live clickable tiles instead.
    public IReadOnlyList<ComingSoonModule> ComingSoonModules { get; } = Array.Empty<ComingSoonModule>();

    public bool HasComingSoonModules => ComingSoonModules.Count > 0;

    // ---------- Upcoming bookings ----------

    public ObservableCollection<UpcomingBookingLine> UpcomingBookings { get; } = new();

    public bool HasUpcomingBookings => UpcomingBookings.Count > 0;

    // ---------- Audit (advanced) ----------

    public ObservableCollection<AuditEventLine> AuditEvents { get; } = new();

    // ---------- Commands ----------

    [RelayCommand]
    private void OpenShift()
    {
        if (!CanOpenShift || !_gate.EnsureCanOperate())
        {
            return;
        }

        var input = _dialogs.ShowOpenShift();
        if (input is null)
        {
            return;
        }

        var result = _shiftService.OpenShift(User.Id, input.OpeningCash, input.Note);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot open shift", result.ErrorMessage);
            return;
        }

        RefreshShift();
    }

    [RelayCommand]
    private void CloseShift()
    {
        if (!CanCloseShift || _shift is null)
        {
            return;
        }

        var input = _dialogs.ShowCloseShift(_shift.ExpectedCash);
        if (input is null)
        {
            return;
        }

        var result = _shiftService.CloseShift(_shift.ShiftId, input.CountedCash, input.Note);
        if (result.Failed || result.Value is null)
        {
            _dialogs.ShowError("Cannot close shift", result.ErrorMessage);
            return;
        }

        var close = result.Value;
        _dialogs.ShowInfo("Shift closed",
            $"Expected: {close.ExpectedCash.Format()}\nCounted: {close.CountedCash.Format()}\nVariance: {close.Variance.Format()}");
        RefreshShift();
    }

    [RelayCommand]
    private void CashIn() => RecordCash(CashMovementType.CashIn);

    [RelayCommand]
    private void CashOut() => RecordCash(CashMovementType.CashOut);

    [RelayCommand]
    private void Expense() => RecordCash(CashMovementType.Expense);

    [RelayCommand]
    private void CashDrop() => RecordCash(CashMovementType.Drop);

    private void RecordCash(CashMovementType type)
    {
        if (!CanRecordCash || _shift is null)
        {
            return;
        }

        var input = _dialogs.ShowCashMovement(type);
        if (input is null)
        {
            return;
        }

        var result = _shiftService.RecordCashMovement(_shift.ShiftId, type, input.Amount, input.Reason, User.Id);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot record cash movement", result.ErrorMessage);
            return;
        }

        RefreshShift();
    }

    [RelayCommand]
    private void OpenTables() => _navigation.ShowTables();

    [RelayCommand]
    private void OpenSessionHistory() => _navigation.ShowSessionHistory();

    [RelayCommand]
    private void OpenAccount() => _navigation.ShowAccount();

    [RelayCommand]
    private void DismissRecoveryPrompt() => ShowRecoveryPrompt = false;

    [RelayCommand]
    private void SetUpRecovery()
    {
        ShowRecoveryPrompt = false;
        _navigation.ShowAccount();
    }

    [RelayCommand]
    private void OpenManageTables()
    {
        if (CanManageTables)
        {
            _navigation.ShowManageTables();
        }
    }

    [RelayCommand]
    private void OpenStaff()
    {
        if (CanManageStaff)
        {
            _navigation.ShowStaff();
        }
    }

    [RelayCommand]
    private void OpenProducts()
    {
        if (CanViewProducts)
        {
            _navigation.ShowProducts();
        }
    }

    [RelayCommand]
    private void OpenCategories()
    {
        if (CanManageProducts)
        {
            _navigation.ShowCategories();
        }
    }

    [RelayCommand]
    private void OpenInventory()
    {
        if (CanViewInventory)
        {
            _navigation.ShowInventory();
        }
    }

    [RelayCommand]
    private void OpenNewSale()
    {
        // Starting a new sale is new operational work — gated by the runtime licence check.
        if (CanCreateSale && _gate.EnsureCanOperate())
        {
            _navigation.ShowNewSale();
        }
    }

    [RelayCommand]
    private void OpenSalesHistory()
    {
        if (CanViewSalesHistory)
        {
            _navigation.ShowSalesHistory();
        }
    }

    [RelayCommand]
    private void OpenBookings()
    {
        if (CanViewBookings)
        {
            _navigation.ShowBookings();
        }
    }

    [RelayCommand]
    private void OpenReports()
    {
        if (CanViewReports)
        {
            _navigation.ShowReports();
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (CanManageSettings)
        {
            _navigation.ShowSettings();
        }
    }

    [RelayCommand]
    private void OpenBackup()
    {
        if (CanCreateBackup)
        {
            _navigation.ShowBackup();
        }
    }

    [RelayCommand]
    private void OpenAdmin()
    {
        if (CanRunAdmin)
        {
            _navigation.ShowAdmin();
        }
    }

    [RelayCommand]
    private void OpenAudit()
    {
        if (CanViewAudit)
        {
            _navigation.ShowAudit();
        }
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    [RelayCommand]
    private void ToggleAdvancedMode()
    {
        if (CanAccessAdvanced)
        {
            IsAdvancedMode = !IsAdvancedMode;
        }
    }

    [RelayCommand]
    private void Help() =>
        _dialogs.ShowInfo("Help",
            "This is the Phase 1 home screen. You can open a shift, record cash movements, and close your shift. " +
            "Tables, sales and other modules are coming in the next updates.");

    [RelayCommand]
    private void Logout()
    {
        if (!_dialogs.Confirm("Log out", "Are you sure you want to log out?"))
        {
            return;
        }

        if (_session.CurrentUser is { } user)
        {
            _auth.Logout(user.Id);
        }

        _session.SignOut();
        _navigation.ShowLogin();
    }

    // ---------- Helpers ----------

    private void RefreshShift()
    {
        _shift = _session.CurrentUser is { } user ? _shiftService.GetCurrentShift(user.Id) : null;

        CashMovements.Clear();
        if (_shift is not null)
        {
            foreach (var line in _shiftService.GetCashMovements(_shift.ShiftId))
            {
                CashMovements.Add(line);
            }
        }

        OnPropertyChanged(nameof(HasOpenShift));
        OnPropertyChanged(nameof(ShiftStatusText));
        OnPropertyChanged(nameof(OpenedText));
        OnPropertyChanged(nameof(OpeningCashText));
        OnPropertyChanged(nameof(CashInText));
        OnPropertyChanged(nameof(CashOutText));
        OnPropertyChanged(nameof(ExpenseText));
        OnPropertyChanged(nameof(DropText));
        OnPropertyChanged(nameof(ExpectedCashText));
        OnPropertyChanged(nameof(OpeningNote));
        OnPropertyChanged(nameof(CanOpenShift));
        OnPropertyChanged(nameof(CanCloseShift));
        OnPropertyChanged(nameof(CanRecordCash));

        if (IsAdvancedMode)
        {
            LoadAudit();
        }
    }

    private void LoadSettingsSummary()
    {
        var settings = _settingsService.Get();
        if (settings is null)
        {
            return;
        }

        ClubName = settings.ClubName;
        SettingsSummary =
            $"{settings.ClubName}  ·  {settings.ActiveTableCount} active table(s)  ·  {settings.ReceiptWidthMm} mm receipts  ·  {settings.CurrencyCode} ({settings.CurrencySymbol})";
        BackupSummary = string.IsNullOrWhiteSpace(settings.BackupFolder)
            ? "Backup folder: not set"
            : $"Backup folder: {settings.BackupFolder}";

        OnPropertyChanged(nameof(ClubName));
        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(BackupSummary));
    }

    private void LoadAudit()
    {
        AuditEvents.Clear();
        foreach (var line in _auditService.GetRecent(100))
        {
            AuditEvents.Add(line);
        }
    }

    private void LoadUpcomingBookings()
    {
        UpcomingBookings.Clear();
        if (!CanViewBookings)
        {
            OnPropertyChanged(nameof(HasUpcomingBookings));
            return;
        }

        foreach (var booking in _bookingService.GetUpcoming(5))
        {
            UpcomingBookings.Add(new UpcomingBookingLine(booking));
        }

        OnPropertyChanged(nameof(HasUpcomingBookings));
    }

    private static string DescribeRole(UserRole? role) => role switch
    {
        UserRole.Owner => "Owner",
        UserRole.Administrator => "Administrator",
        UserRole.Manager => "Manager",
        UserRole.Cashier => "Cashier",
        UserRole.FloorStaff => "Floor Staff",
        _ => string.Empty,
    };
}

/// <summary>A compact upcoming-booking row for the Home dashboard.</summary>
public sealed class UpcomingBookingLine
{
    private readonly BookingListItem _item;

    public UpcomingBookingLine(BookingListItem item)
    {
        _item = item;
    }

    public string CustomerName => _item.CustomerName;
    public string TableName => _item.TableName;
    public string WhenText => _item.StartUtc.ToLocalTime().ToString("ddd dd MMM, h:mm tt", System.Globalization.CultureInfo.CurrentCulture);
    public string StatusText => _item.Status == BookingStatus.CheckedIn ? "Checked in" : "Scheduled";
}
