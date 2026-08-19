using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>A formatted history row.</summary>
public sealed record SessionHistoryRow(
    string Number,
    string DateText,
    string Tables,
    string StartText,
    string FinishText,
    string DurationText,
    string ChargeText,
    string StatusText,
    string StartedBy,
    string FinishedBy);

/// <summary>A named table option for the history filter.</summary>
public sealed record TableOption(int? Id, string Name);

/// <summary>
/// The completed-session history (Awaiting Checkout). Read-only list with basic
/// search/filter by date, table, session number and status. Not the reports module.
/// </summary>
public partial class SessionHistoryViewModel : ObservableObject
{
    private readonly ITableSessionService _sessions;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    public SessionHistoryViewModel(ITableSessionService sessions, INavigationService navigation, IThemeService theme)
    {
        _sessions = sessions;
        _navigation = navigation;
        _theme = theme;

        var tables = new List<TableOption> { new(null, "All tables") };
        tables.AddRange(_sessions.GetDashboard().Select(c => new TableOption(c.TableId, c.Name)));
        TableOptions = tables;
        _selectedTable = tables[0];

        Apply();
    }

    public ObservableCollection<SessionHistoryRow> Items { get; } = new();

    public IReadOnlyList<string> StatusOptions { get; } = new[] { "All", "Awaiting checkout", "Voided" };
    public IReadOnlyList<TableOption> TableOptions { get; }

    [ObservableProperty] private string _searchSessionNumber = string.Empty;
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private string _selectedStatus = "All";
    [ObservableProperty] private TableOption _selectedTable;
    [ObservableProperty] private string _emptyMessage = string.Empty;

    [RelayCommand]
    private void Apply()
    {
        SessionStatus? status = SelectedStatus switch
        {
            "Awaiting checkout" => SessionStatus.Completed,
            "Voided" => SessionStatus.Voided,
            _ => null,
        };

        int? number = int.TryParse(SearchSessionNumber, out var n) ? n : null;
        DateTimeOffset? from = FromDate is { } f ? new DateTimeOffset(f.Date, TimeSpan.Zero) : null;
        DateTimeOffset? to = ToDate is { } t ? new DateTimeOffset(t.Date.AddDays(1), TimeSpan.Zero) : null;

        var filter = new SessionHistoryFilter(from, to, SelectedTable?.Id, number, status);

        Items.Clear();
        foreach (var item in _sessions.GetHistory(filter))
        {
            Items.Add(new SessionHistoryRow(
                $"#{item.SessionNumber}",
                DisplayFormat.LocalDateTime(item.FinishUtc ?? item.StartUtc),
                item.TableNames,
                DisplayFormat.LocalTime(item.StartUtc),
                item.FinishUtc is { } fin ? DisplayFormat.LocalTime(fin) : "—",
                DisplayFormat.DurationShort(item.BillableSeconds),
                item.FinalCharge.Format(),
                item.Status == SessionStatus.Voided ? "Voided" : "Awaiting checkout",
                item.StartedByName,
                item.FinishedByName ?? "—"));
        }

        EmptyMessage = Items.Count == 0 ? "No sessions match these filters." : string.Empty;
    }

    [RelayCommand]
    private void Clear()
    {
        SearchSessionNumber = string.Empty;
        FromDate = null;
        ToDate = null;
        SelectedStatus = "All";
        SelectedTable = TableOptions[0];
        Apply();
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    [RelayCommand]
    private void Tables() => _navigation.ShowTables();

    [RelayCommand]
    private void Home() => _navigation.ShowHome();
}
