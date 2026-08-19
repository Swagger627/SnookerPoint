using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The Manage Tables screen for Owner/Administrator/Manager users: view active and
/// inactive tables, add tables, edit name/type/rate, activate/deactivate, apply one
/// rate to selected tables, and save or cancel. Rate changes affect new sessions only.
/// </summary>
public partial class ManageTablesViewModel : ObservableObject
{
    private readonly ITableManagementService _tables;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    public ManageTablesViewModel(
        ITableManagementService tables,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme)
    {
        _tables = tables;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;

        Reload();
    }

    public ObservableCollection<TableEditRowViewModel> Rows { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    public bool CanManage =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageTables);

    [ObservableProperty] private string _bulkRateText = string.Empty;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    private int UserId => _session.CurrentUser!.Id;

    // ==================== COMMANDS ====================

    [RelayCommand]
    private void AddTable()
    {
        var row = new TableEditRowViewModel { Name = $"Table {Rows.Count + 1}" };
        Rows.Add(row);
        StatusMessage = null;
    }

    [RelayCommand]
    private void ApplyRateToSelected()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ErrorMessage = "Tick the tables you want the rate applied to first.";
            return;
        }

        if (!MoneyInput.TryParseRupees(BulkRateText, out Money rate))
        {
            ErrorMessage = "Enter a valid rate (0 or more) to apply.";
            return;
        }

        var text = rate.ToRupees().ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        foreach (var row in selected)
        {
            row.RateText = text;
        }

        ErrorMessage = null;
        StatusMessage = $"Applied {rate.Format()}/hr to {selected.Count} table(s). Remember to Save.";
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanManage)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;

        var drafts = new List<TableDraft>(Rows.Count);
        foreach (var row in Rows)
        {
            if (!row.TryBuildDraft(out var draft, out var error))
            {
                ErrorMessage = error;
                return;
            }

            drafts.Add(draft);
        }

        var result = _tables.SaveLayout(drafts, UserId);
        if (result.Failed)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        Reload();
        StatusMessage = "Tables saved.";
    }

    [RelayCommand]
    private void Cancel()
    {
        Reload();
        StatusMessage = "Changes discarded.";
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void Tables() => _navigation.ShowTables();

    // ==================== HELPERS ====================

    private void Reload()
    {
        Rows.Clear();
        foreach (var item in _tables.GetAll())
        {
            Rows.Add(new TableEditRowViewModel(item));
        }

        ErrorMessage = null;
    }
}
