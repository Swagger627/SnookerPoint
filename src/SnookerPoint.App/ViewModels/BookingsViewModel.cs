using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Bookings;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Shifts;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// Bookings and table reservations: create/edit/cancel, customer check-in, mark no-shows,
/// and start a booking into a live table session (Hourly or Fixed) via the normal session
/// workflow. Search by date, table, status, customer name and phone. Times are shown in
/// local time and stored in UTC. All mutating actions require the ManageBookings capability
/// and surface clear success/error feedback.
/// </summary>
public partial class BookingsViewModel : ObservableObject
{
    private readonly IBookingService _bookings;
    private readonly ITableManagementService _tableManagement;
    private readonly IShiftService _shifts;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IClock _clock;
    private readonly SnookerPoint.App.Licensing.ILicenseGate _gate;

    private IReadOnlyList<BookingTableOption> _activeTables = Array.Empty<BookingTableOption>();

    public BookingsViewModel(
        IBookingService bookings,
        ITableManagementService tableManagement,
        IShiftService shifts,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme,
        IClock clock,
        SnookerPoint.App.Licensing.ILicenseGate gate)
    {
        _bookings = bookings;
        _tableManagement = tableManagement;
        _shifts = shifts;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _clock = clock;
        _gate = gate;

        StatusFilters = new[]
        {
            new BookingStatusFilter(null, "All statuses"),
            new BookingStatusFilter(BookingStatus.Scheduled, "Scheduled"),
            new BookingStatusFilter(BookingStatus.CheckedIn, "Checked in"),
            new BookingStatusFilter(BookingStatus.Started, "Started"),
            new BookingStatusFilter(BookingStatus.Completed, "Completed"),
            new BookingStatusFilter(BookingStatus.Cancelled, "Cancelled"),
            new BookingStatusFilter(BookingStatus.NoShow, "No show"),
        };
        _selectedStatus = StatusFilters[0];

        LoadTables();
        _selectedTable = TableFilters.FirstOrDefault();
        Refresh();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<BookingRow> Rows { get; } = new();
    public IReadOnlyList<BookingStatusFilter> StatusFilters { get; }
    public ObservableCollection<TableFilterOption> TableFilters { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;
    public bool IsEmpty => Rows.Count == 0;

    public bool CanManage => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageBookings);

    private int UserId => _session.CurrentUser!.Id;

    [ObservableProperty] private string _searchName = string.Empty;
    [ObservableProperty] private string _searchPhone = string.Empty;
    [ObservableProperty] private TableFilterOption? _selectedTable;
    [ObservableProperty] private BookingStatusFilter _selectedStatus;
    [ObservableProperty] private DateTime? _filterDate;

    partial void OnSearchNameChanged(string value) => Refresh();
    partial void OnSearchPhoneChanged(string value) => Refresh();
    partial void OnSelectedTableChanged(TableFilterOption? value) => Refresh();
    partial void OnSelectedStatusChanged(BookingStatusFilter value) => Refresh();
    partial void OnFilterDateChanged(DateTime? value) => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        var filter = new BookingFilter(
            OnDateLocal: FilterDate is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d.Date, DateTimeKind.Local)) : null,
            TableId: SelectedTable?.TableId,
            Status: SelectedStatus?.Value,
            CustomerName: string.IsNullOrWhiteSpace(SearchName) ? null : SearchName.Trim(),
            Phone: string.IsNullOrWhiteSpace(SearchPhone) ? null : SearchPhone.Trim());

        var now = _clock.UtcNow;
        Rows.Clear();
        foreach (var item in _bookings.GetBookings(filter))
        {
            Rows.Add(new BookingRow(item, CanManage, now));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void NewBooking()
    {
        Feedback.Clear();
        if (!EnsureCanManage() || !EnsureTables() || !_gate.EnsureCanOperate())
        {
            return;
        }

        var start = RoundedNextHalfHourLocal();
        var context = new BookingEditorContext(
            IsEdit: false, Tables: _activeTables,
            CustomerName: string.Empty, Phone: null, TableId: _activeTables[0].TableId,
            StartLocal: start, DurationMinutes: 60, PlayerCount: null, Notes: null);

        var result = _dialogs.ShowBookingEditor(context);
        if (result is null)
        {
            return;
        }

        var create = _bookings.Create(new CreateBookingRequest(
            result.CustomerName, result.Phone, result.TableId, result.StartLocal.ToUniversalTime(),
            result.DurationMinutes, result.PlayerCount, result.Notes), UserId);
        if (create.Failed)
        {
            Feedback.Error(create.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success($"Booking created for {result.CustomerName}.");
    }

    [RelayCommand]
    private void Edit(BookingRow? row)
    {
        Feedback.Clear();
        if (row is null || !EnsureCanManage() || !EnsureTables())
        {
            return;
        }

        var context = new BookingEditorContext(
            IsEdit: true, Tables: _activeTables,
            CustomerName: row.CustomerName, Phone: row.PhoneValue, TableId: row.TableId,
            StartLocal: row.StartLocal, DurationMinutes: row.DurationMinutes,
            PlayerCount: row.PlayerCountValue, Notes: row.NotesValue);

        var result = _dialogs.ShowBookingEditor(context);
        if (result is null)
        {
            return;
        }

        var update = _bookings.Update(new UpdateBookingRequest(
            row.Id, result.CustomerName, result.Phone, result.TableId, result.StartLocal.ToUniversalTime(),
            result.DurationMinutes, result.PlayerCount, result.Notes), UserId);
        if (update.Failed)
        {
            Feedback.Error(update.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success("Booking updated.");
    }

    [RelayCommand]
    private void CheckIn(BookingRow? row)
    {
        Feedback.Clear();
        if (row is null || !EnsureCanManage())
        {
            return;
        }

        var result = _bookings.CheckIn(row.Id, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success($"{row.CustomerName} checked in.");
    }

    [RelayCommand]
    private void Cancel(BookingRow? row)
    {
        Feedback.Clear();
        if (row is null || !EnsureCanManage())
        {
            return;
        }

        if (!_dialogs.Confirm("Cancel booking", $"Cancel the booking for {row.CustomerName}?"))
        {
            return;
        }

        var result = _bookings.Cancel(row.Id, "Cancelled by staff", UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success("Booking cancelled.");
    }

    [RelayCommand]
    private void MarkNoShow(BookingRow? row)
    {
        Feedback.Clear();
        if (row is null || !EnsureCanManage())
        {
            return;
        }

        var result = _bookings.MarkNoShow(row.Id, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success($"{row.CustomerName} marked as a no-show.");
    }

    [RelayCommand]
    private void Start(BookingRow? row)
    {
        Feedback.Clear();
        if (row is null || !EnsureCanManage() || !_gate.EnsureCanOperate())
        {
            return;
        }

        var shift = _shifts.GetCurrentShift(UserId);
        if (shift is null)
        {
            Feedback.Error("Open a shift before starting a booking.");
            return;
        }

        // Offer the reserved table when it is free, plus any free alternatives.
        var choices = new List<BookingTableOption>();
        if (!row.TableCurrentlyInUse)
        {
            choices.Add(TableOption(row.TableId, row.TableName));
        }

        foreach (var alt in _bookings.GetAlternativeTables(row.Id))
        {
            choices.Add(TableOption(alt.TableId, alt.TableName));
        }

        if (choices.Count == 0)
        {
            Feedback.Error($"{row.TableName} is occupied and no alternative tables are free right now.");
            return;
        }

        var choice = _dialogs.ShowBookingStart(new BookingStartContext(
            row.CustomerName, row.TableName, row.TableCurrentlyInUse, choices));
        if (choice is null)
        {
            return;
        }

        // If the operator picked a free alternative, move the reservation there first.
        if (choice.TableId != row.TableId)
        {
            var move = _bookings.Update(new UpdateBookingRequest(
                row.Id, row.CustomerName, row.PhoneValue, choice.TableId, row.StartLocal.ToUniversalTime(),
                row.DurationMinutes, row.PlayerCountValue, row.NotesValue), UserId);
            if (move.Failed)
            {
                Feedback.Error(move.ErrorMessage);
                return;
            }
        }

        var start = _bookings.StartSession(row.Id, choice.BillingType, choice.FixedAmount, UserId, shift.ShiftId);
        if (start.Failed)
        {
            Feedback.Error(start.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success($"Session started for {row.CustomerName}. Open Tables to manage it.");
    }

    [RelayCommand]
    private void OpenTables() => _navigation.ShowTables();

    [RelayCommand]
    private void ClearFilters()
    {
        SearchName = string.Empty;
        SearchPhone = string.Empty;
        SelectedTable = TableFilters.FirstOrDefault();
        SelectedStatus = StatusFilters[0];
        FilterDate = null;
    }

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    // ---------- Helpers ----------

    private void LoadTables()
    {
        var active = _tableManagement.GetAll().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToList();
        _activeTables = active.Select(t => new BookingTableOption(t.Id, t.Name, t.HourlyRate)).ToList();

        TableFilters.Clear();
        TableFilters.Add(new TableFilterOption(null, "All tables"));
        foreach (var t in active)
        {
            TableFilters.Add(new TableFilterOption(t.Id, t.Name));
        }
    }

    private BookingTableOption TableOption(int tableId, string fallbackName)
    {
        var match = _activeTables.FirstOrDefault(t => t.TableId == tableId);
        return match ?? new BookingTableOption(tableId, fallbackName, SnookerPoint.Domain.ValueObjects.Money.Zero);
    }

    private bool EnsureCanManage()
    {
        if (CanManage)
        {
            return true;
        }

        Feedback.Error("You do not have permission to manage bookings.");
        return false;
    }

    private bool EnsureTables()
    {
        if (_activeTables.Count > 0)
        {
            return true;
        }

        Feedback.Error("Add an active table before creating bookings.");
        return false;
    }

    private static DateTimeOffset RoundedNextHalfHourLocal()
    {
        var now = DateTimeOffset.Now;
        var minutes = now.Minute < 30 ? 30 : 60;
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset).AddMinutes(minutes);
    }
}

/// <summary>A status filter choice.</summary>
public sealed record BookingStatusFilter(BookingStatus? Value, string Label);

/// <summary>A table filter choice (null table = all tables).</summary>
public sealed record TableFilterOption(int? TableId, string Name);

/// <summary>A read-only bookings-list row with per-status action availability.</summary>
public sealed class BookingRow
{
    private readonly BookingListItem _item;

    public BookingRow(BookingListItem item, bool canManage, DateTimeOffset nowUtc)
    {
        _item = item;

        var scheduledOrCheckedIn = item.Status is BookingStatus.Scheduled or BookingStatus.CheckedIn;
        ShowCheckIn = canManage && item.Status == BookingStatus.Scheduled;
        ShowEdit = canManage && scheduledOrCheckedIn;
        ShowStart = canManage && scheduledOrCheckedIn;
        ShowCancel = canManage && item.Status is BookingStatus.Scheduled or BookingStatus.CheckedIn or BookingStatus.Started;
        ShowNoShow = canManage && scheduledOrCheckedIn && item.StartUtc <= nowUtc;
    }

    public int Id => _item.Id;
    public int TableId => _item.TableId;
    public string CustomerName => _item.CustomerName;
    public string? PhoneValue => _item.Phone;
    public string Phone => string.IsNullOrWhiteSpace(_item.Phone) ? "—" : _item.Phone!;
    public string TableName => _item.TableName;
    public int DurationMinutes => _item.DurationMinutes;
    public int? PlayerCountValue => _item.PlayerCount;
    public string? NotesValue => _item.Notes;
    public bool TableCurrentlyInUse => _item.TableCurrentlyInUse;

    public DateTimeOffset StartLocal => _item.StartUtc.ToLocalTime();
    public string WhenText => _item.StartUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture);
    public string EndText => _item.EndUtc.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture);
    public string DurationText => $"{_item.DurationMinutes} min";
    public string PlayersText => _item.PlayerCount is { } p ? p.ToString(CultureInfo.CurrentCulture) : "—";
    public string Notes => string.IsNullOrWhiteSpace(_item.Notes) ? string.Empty : _item.Notes!;

    public string StatusText => _item.Status switch
    {
        BookingStatus.Scheduled => "Scheduled",
        BookingStatus.CheckedIn => "Checked in",
        BookingStatus.Started => "Started",
        BookingStatus.Completed => "Completed",
        BookingStatus.Cancelled => "Cancelled",
        BookingStatus.NoShow => "No show",
        _ => _item.Status.ToString(),
    };

    public Brush StatusBrush => new SolidColorBrush(_item.Status switch
    {
        BookingStatus.Scheduled => Color.FromRgb(0x3B, 0x82, 0xF6),  // blue
        BookingStatus.CheckedIn => Color.FromRgb(0x14, 0xB8, 0xA6),  // teal
        BookingStatus.Started => Color.FromRgb(0xF5, 0x9E, 0x0B),    // amber
        BookingStatus.Completed => Color.FromRgb(0x22, 0xC5, 0x5E),  // green
        BookingStatus.Cancelled => Color.FromRgb(0x9C, 0xA3, 0xAF),  // grey
        BookingStatus.NoShow => Color.FromRgb(0xEF, 0x44, 0x44),     // red
        _ => Color.FromRgb(0x9C, 0xA3, 0xAF),
    });

    public bool ShowCheckIn { get; }
    public bool ShowEdit { get; }
    public bool ShowStart { get; }
    public bool ShowCancel { get; }
    public bool ShowNoShow { get; }
}
