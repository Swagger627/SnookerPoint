using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the booking editor. Collects customer name, phone, table, date, start time,
/// expected duration, players and notes. Times are entered and shown in local time; the
/// caller converts the returned start to UTC before storing.
/// </summary>
public partial class BookingEditorDialogViewModel : ObservableObject
{
    public BookingEditorDialogViewModel(BookingEditorContext context)
    {
        Title = context.IsEdit ? "Edit booking" : "New booking";
        ConfirmText = context.IsEdit ? "Save booking" : "Create booking";
        Tables = context.Tables;

        _customerName = context.CustomerName;
        _phone = context.Phone ?? string.Empty;
        _selectedTable = context.TableId is { } id
            ? Tables.FirstOrDefault(t => t.TableId == id) ?? Tables.FirstOrDefault()
            : Tables.FirstOrDefault();
        _date = context.StartLocal.LocalDateTime.Date;
        _startTime = context.StartLocal.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        _durationMinutes = context.DurationMinutes.ToString(CultureInfo.InvariantCulture);
        _playerCount = context.PlayerCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _notes = context.Notes ?? string.Empty;
    }

    public string Title { get; }
    public string ConfirmText { get; }
    public IReadOnlyList<BookingTableOption> Tables { get; }

    [ObservableProperty] private string _customerName;
    [ObservableProperty] private string _phone;
    [ObservableProperty] private BookingTableOption? _selectedTable;
    [ObservableProperty] private DateTime _date;
    [ObservableProperty] private string _startTime;
    [ObservableProperty] private string _durationMinutes;
    [ObservableProperty] private string _playerCount;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private string? _errorMessage;

    public BookingEditorResult? Result { get; private set; }

    public bool TryConfirm()
    {
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            ErrorMessage = "Please enter the customer's name.";
            return false;
        }

        if (SelectedTable is null)
        {
            ErrorMessage = "Please choose a table.";
            return false;
        }

        if (!TryParseTime(StartTime, out var time))
        {
            ErrorMessage = "Please enter a start time such as 18:30.";
            return false;
        }

        if (!int.TryParse(DurationMinutes?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration) || duration <= 0)
        {
            ErrorMessage = "Please enter an expected duration in minutes (greater than zero).";
            return false;
        }

        int? players = null;
        if (!string.IsNullOrWhiteSpace(PlayerCount))
        {
            if (!int.TryParse(PlayerCount.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) || p <= 0)
            {
                ErrorMessage = "Number of players must be a whole number greater than zero.";
                return false;
            }

            players = p;
        }

        var localStart = new DateTimeOffset(DateTime.SpecifyKind(Date.Date.Add(time), DateTimeKind.Local));

        Result = new BookingEditorResult(
            CustomerName.Trim(),
            string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
            SelectedTable.TableId,
            localStart,
            duration,
            players,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());
        return true;
    }

    private static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var formats = new[] { "HH:mm", "H:mm", "h:mm tt", "h:mmtt", "hh:mm tt" };
        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out parsed))
        {
            time = parsed.TimeOfDay;
            return true;
        }

        return false;
    }
}
