using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Audit;
using SnookerPoint.Application.Reporting;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The audit viewer: filter append-only audit events by date, user, action, module and
/// reference, page through results, and export the current filter to CSV. Audit records never
/// contain secrets. Viewing needs ViewAuditLog; exporting needs ExportReports.
/// </summary>
public partial class AuditViewModel : ObservableObject
{
    private const int PageSize = 100;

    private readonly IAuditQueryService _audit;
    private readonly ICsvExportService _csv;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    public AuditViewModel(
        IAuditQueryService audit,
        ICsvExportService csv,
        ISessionContext session,
        IPermissionService permissions,
        INavigationService navigation,
        IThemeService theme)
    {
        _audit = audit;
        _csv = csv;
        _session = session;
        _permissions = permissions;
        _navigation = navigation;
        _theme = theme;

        Actions = new List<string> { "All actions" };
        Actions.AddRange(_audit.GetActionNames());
        Modules = new List<string> { "All modules" };
        Modules.AddRange(_audit.GetModules());
        Actors = new List<ActorChoice> { new(null, "All users") };
        Actors.AddRange(_audit.GetActors().Select(a => new ActorChoice(a.UserId, a.DisplayName)));

        _selectedAction = Actions[0];
        _selectedModule = Modules[0];
        _selectedActor = Actors[0];

        Refresh();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<AuditRow> Rows { get; } = new();
    public List<string> Actions { get; }
    public List<string> Modules { get; }
    public List<ActorChoice> Actors { get; }

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;
    public bool CanExport => Has(Permission.ExportReports);
    public bool IsEmpty => Rows.Count == 0;

    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string _selectedAction;
    [ObservableProperty] private string _selectedModule;
    [ObservableProperty] private ActorChoice _selectedActor;
    [ObservableProperty] private int _page;
    [ObservableProperty] private int _totalCount;

    public string PageText => TotalCount == 0
        ? "No events"
        : $"Showing {Page * PageSize + 1}–{Math.Min((Page + 1) * PageSize, TotalCount)} of {TotalCount}";

    public bool CanPrev => Page > 0;
    public bool CanNext => (Page + 1) * PageSize < TotalCount;

    private int UserId => _session.CurrentUser!.Id;

    private AuditFilter BuildFilter() => new(
        FromUtc: FromDate is { } f ? new DateTimeOffset(DateTime.SpecifyKind(f.Date, DateTimeKind.Local)).ToUniversalTime() : null,
        ToUtc: ToDate is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t.Date.AddDays(1), DateTimeKind.Local)).ToUniversalTime() : null,
        ActorUserId: SelectedActor?.UserId,
        Action: SelectedAction == Actions[0] ? null : SelectedAction,
        Module: SelectedModule == Modules[0] ? null : SelectedModule,
        Reference: string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim());

    partial void OnSelectedActionChanged(string value) => ResetAndRefresh();
    partial void OnSelectedModuleChanged(string value) => ResetAndRefresh();
    partial void OnSelectedActorChanged(ActorChoice value) => ResetAndRefresh();
    partial void OnFromDateChanged(DateTime? value) => ResetAndRefresh();
    partial void OnToDateChanged(DateTime? value) => ResetAndRefresh();
    partial void OnReferenceChanged(string value) => ResetAndRefresh();

    private void ResetAndRefresh()
    {
        Page = 0;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var filter = BuildFilter();
        TotalCount = _audit.Count(filter);

        Rows.Clear();
        foreach (var e in _audit.Query(filter, Page * PageSize, PageSize))
        {
            Rows.Add(new AuditRow(e));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
    }

    [RelayCommand]
    private void Next()
    {
        if (CanNext)
        {
            Page++;
            Refresh();
        }
    }

    [RelayCommand]
    private void Prev()
    {
        if (CanPrev)
        {
            Page--;
            Refresh();
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FromDate = null;
        ToDate = null;
        Reference = string.Empty;
        SelectedAction = Actions[0];
        SelectedModule = Modules[0];
        SelectedActor = Actors[0];
    }

    [RelayCommand]
    private void Export()
    {
        Feedback.Clear();
        if (!CanExport)
        {
            Feedback.Error("You do not have permission to export the audit log.");
            return;
        }

        var filter = BuildFilter();
        var total = _audit.Count(filter);
        var all = _audit.Query(filter, 0, Math.Max(total, 1));
        if (all.Count == 0)
        {
            Feedback.Warning("There are no audit events matching the current filters.");
            return;
        }

        var headers = new[] { "Timestamp (local)", "User", "Action", "Module", "Reference", "Summary" };
        var rows = all.Select(e => (IReadOnlyList<string>)new[]
        {
            e.Utc.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss", CultureInfo.CurrentCulture),
            e.ActorDisplayName ?? "—",
            e.Action,
            e.Module,
            e.Reference ?? "—",
            e.Details ?? "—",
        }).ToList();

        var result = _csv.Export(new CsvDocument("Audit-log", headers, rows), null, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Feedback.Success($"Exported to {result.Value}");
    }

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    private bool Has(Permission p) => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, p);
}

/// <summary>An actor filter choice (null = all users).</summary>
public sealed record ActorChoice(int? UserId, string DisplayName);

/// <summary>A displayed audit row.</summary>
public sealed class AuditRow
{
    private readonly AuditEventLine _line;

    public AuditRow(AuditEventLine line)
    {
        _line = line;
    }

    public string When => _line.Utc.ToLocalTime().ToString("dd MMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture);
    public string User => _line.ActorDisplayName ?? "—";
    public string Action => _line.Action;
    public string Module => _line.Module;
    public string Reference => _line.Reference ?? "—";
    public string Details => _line.Details ?? "—";
}
