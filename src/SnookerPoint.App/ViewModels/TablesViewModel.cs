using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Billing;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Settings;
using SnookerPoint.Application.Shifts;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The live table dashboard. Refreshes each second (display only) from persisted
/// timestamps, filters by status, enforces the open-shift requirement and permissions,
/// and drives start/pause/resume/transfer/finish/correct via the session service.
/// </summary>
public partial class TablesViewModel : ObservableObject, IDisposable
{
    private readonly ITableSessionService _sessions;
    private readonly IShiftService _shifts;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IBillingSettingsService _billing;
    private readonly ISessionBillingCalculator _calculator;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IClock _clock;
    private readonly SnookerPoint.App.Licensing.ILicenseGate _gate;

    private readonly DispatcherTimer _timer;
    private int? _shiftId;

    public TablesViewModel(
        ITableSessionService sessions,
        IShiftService shifts,
        ISessionContext session,
        IPermissionService permissions,
        IBillingSettingsService billing,
        ISessionBillingCalculator calculator,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme,
        IClock clock,
        SnookerPoint.App.Licensing.ILicenseGate gate)
    {
        _sessions = sessions;
        _shifts = shifts;
        _session = session;
        _permissions = permissions;
        _billing = billing;
        _calculator = calculator;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _clock = clock;
        _gate = gate;

        CardsView = CollectionViewSource.GetDefaultView(Cards);
        CardsView.Filter = FilterCard;

        RefreshShift();
        Reload();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public ObservableCollection<TableCardViewModel> Cards { get; } = new();
    public ICollectionView CardsView { get; }

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    [ObservableProperty] private bool _hasOpenShift;
    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private string _emptyMessage = string.Empty;

    public string ShiftStatusText => HasOpenShift ? "Shift open" : "No open shift";

    public bool CanManageBilling =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageBillingSettings);

    public bool CanCorrect =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.CorrectSession);

    public bool CanManageTables =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageTables);

    public IReadOnlyList<string> Filters { get; } = new[] { "All", "Available", "In use", "Paused" };

    partial void OnSelectedFilterChanged(string value) => CardsView.Refresh();

    partial void OnHasOpenShiftChanged(bool value) => OnPropertyChanged(nameof(ShiftStatusText));

    private bool FilterCard(object item)
    {
        if (item is not TableCardViewModel card)
        {
            return false;
        }

        return SelectedFilter switch
        {
            "Available" => card.IsAvailable,
            "In use" => card.IsInUse,
            "Paused" => card.IsPaused,
            _ => true,
        };
    }

    // ==================== LIVE TICK ====================

    private void Tick()
    {
        var now = _clock.UtcNow;
        foreach (var card in Cards)
        {
            card.Update(now);
        }
    }

    // ==================== DATA ====================

    private void Reload()
    {
        Cards.Clear();
        foreach (var card in _sessions.GetDashboard())
        {
            Cards.Add(new TableCardViewModel(card, _calculator));
        }

        EmptyMessage = Cards.Count == 0 ? "No active tables. Add tables in setup or settings." : string.Empty;
        CardsView.Refresh();
    }

    private void RefreshShift()
    {
        var shift = _session.CurrentUser is { } u ? _shifts.GetCurrentShift(u.Id) : null;
        _shiftId = shift?.ShiftId;
        HasOpenShift = shift is not null;
    }

    private bool EnsureShift()
    {
        RefreshShift();
        if (HasOpenShift)
        {
            return true;
        }

        _dialogs.ShowInfo("Open a shift first", "You need to open a shift before working with tables. Use the Open Shift button.");
        return false;
    }

    private int UserId => _session.CurrentUser!.Id;

    // ==================== COMMANDS ====================

    [RelayCommand]
    private void OpenShift()
    {
        var input = _dialogs.ShowOpenShift();
        if (input is null)
        {
            return;
        }

        var result = _shifts.OpenShift(UserId, input.OpeningCash, input.Note);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot open shift", result.ErrorMessage);
        }

        RefreshShift();
    }

    [RelayCommand]
    private void Start(TableCardViewModel? card)
    {
        if (card is null || !card.IsAvailable || !EnsureShift() || !_gate.EnsureCanOperate())
        {
            return;
        }

        var settings = _billing.Get();
        var dashboard = _sessions.GetDashboard().FirstOrDefault(c => c.TableId == card.TableId);
        if (dashboard is null)
        {
            return;
        }

        var input = _dialogs.ShowStartSession(card.Name, card.TypeText, dashboard.HourlyRate, settings.Summary());
        if (input is null)
        {
            return;
        }

        var result = _sessions.StartSession(new StartSessionRequest(
            card.TableId, UserId, _shiftId!.Value, input.CustomerLabel, input.Note, input.BillingType, input.FixedAmount));
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot start session", result.ErrorMessage);
        }

        Reload();
    }

    [RelayCommand]
    private void Pause(TableCardViewModel? card)
    {
        if (card?.SessionId is not { } id || !card.IsInUse || !EnsureShift())
        {
            return;
        }

        if (!_dialogs.Confirm("Pause session", $"Pause {card.Name}? Billing stops while paused."))
        {
            return;
        }

        var result = _sessions.PauseSession(id, UserId, _shiftId!.Value);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot pause", result.ErrorMessage);
        }

        Reload();
    }

    [RelayCommand]
    private void Resume(TableCardViewModel? card)
    {
        if (card?.SessionId is not { } id || !card.IsPaused || !EnsureShift())
        {
            return;
        }

        var result = _sessions.ResumeSession(id, UserId, _shiftId!.Value);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot resume", result.ErrorMessage);
        }

        Reload();
    }

    [RelayCommand]
    private void Transfer(TableCardViewModel? card)
    {
        if (card?.SessionId is not { } id || !card.HasSession || !EnsureShift())
        {
            return;
        }

        var destinations = _sessions.GetDashboard()
            .Where(c => c.Status == DashboardStatus.Available && c.TableId != card.TableId)
            .Select(c => new TransferDestination(c.TableId, c.Name, c.HourlyRate))
            .ToList();

        if (destinations.Count == 0)
        {
            _dialogs.ShowInfo("No available tables", "There are no other available tables to transfer to.");
            return;
        }

        var input = _dialogs.ShowTransfer(card.Name, destinations);
        if (input is null)
        {
            return;
        }

        var result = _sessions.TransferSession(id, input.DestinationTableId, UserId, _shiftId!.Value, input.Reason);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot transfer", result.ErrorMessage);
        }

        Reload();
    }

    [RelayCommand]
    private void Finish(TableCardViewModel? card)
    {
        if (card?.SessionId is not { } id || !card.HasSession || !EnsureShift())
        {
            return;
        }

        var preview = _sessions.GetFinishPreview(id);
        if (preview.Failed || preview.Value is null)
        {
            _dialogs.ShowError("Cannot finish", preview.ErrorMessage);
            return;
        }

        var input = _dialogs.ShowFinish(preview.Value);
        if (input is null)
        {
            return;
        }

        var result = _sessions.FinishSession(id, UserId, _shiftId!.Value, input.ClosingNote);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot finish", result.ErrorMessage);
        }

        Reload();
    }

    [RelayCommand]
    private void Correct(TableCardViewModel? card)
    {
        if (card?.SessionId is not { } id || !CanCorrect || !EnsureShift())
        {
            return;
        }

        var context = _sessions.GetCorrectionContext(id);
        if (context is null)
        {
            return;
        }

        var request = _dialogs.ShowCorrection(context);
        if (request is null)
        {
            return;
        }

        var shift = _shiftId!.Value;
        var result = request.Kind switch
        {
            CorrectionKind.StartTime => _sessions.CorrectStartTime(id, request.NewTimestamp, request.Reason, UserId, shift),
            CorrectionKind.PauseStart => _sessions.CorrectPauseStart(request.TargetId!.Value, request.NewTimestamp, request.Reason, UserId, shift),
            CorrectionKind.PauseEnd => _sessions.CorrectPauseEnd(request.TargetId!.Value, request.NewTimestamp, request.Reason, UserId, shift),
            CorrectionKind.SegmentRate => _sessions.CorrectSegmentRate(request.TargetId!.Value, request.NewAmount, request.Reason, UserId, shift),
            CorrectionKind.FixedAmount => _sessions.CorrectFixedAmount(id, request.NewAmount, request.Reason, UserId, shift),
            CorrectionKind.SwitchToFixed => _sessions.CorrectBillingType(id, Domain.Enums.BillingType.Fixed, request.NewAmount, request.Reason, UserId, shift),
            CorrectionKind.SwitchToHourly => _sessions.CorrectBillingType(id, Domain.Enums.BillingType.Hourly, null, request.Reason, UserId, shift),
            CorrectionKind.ChargeAdjustment => _sessions.AddChargeAdjustment(id, request.NewAmount, request.Reason, UserId, shift),
            CorrectionKind.Void => _sessions.VoidSession(id, request.Reason, UserId, shift),
            _ => Application.Common.OperationResult.Failure("Unknown correction."),
        };

        if (result.Failed)
        {
            _dialogs.ShowError("Cannot correct", result.ErrorMessage);
        }

        Reload();
    }

    [RelayCommand]
    private void EditBillingSettings()
    {
        if (!CanManageBilling)
        {
            return;
        }

        var input = _dialogs.ShowBillingSettings(_billing.Get());
        if (input is null)
        {
            return;
        }

        var result = _billing.Update(input.Method, input.RoundingIncrementMinutes, input.MinimumBillableMinutes, input.GracePeriodMinutes, UserId);
        if (result.Failed)
        {
            _dialogs.ShowError("Cannot save settings", result.ErrorMessage);
        }
    }

    [RelayCommand]
    private void RefreshDashboard()
    {
        RefreshShift();
        Reload();
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void History() => _navigation.ShowSessionHistory();

    [RelayCommand]
    private void ManageTables()
    {
        if (CanManageTables)
        {
            _navigation.ShowManageTables();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
