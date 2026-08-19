using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Diagnostics;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// Advanced administration: database and environment health, an integrity check, managed-folder
/// validation, quick access to the logs/backups folders, and a secret-free diagnostic summary.
/// No raw SQL or data-editing surface is exposed.
/// </summary>
public partial class AdminViewModel : ObservableObject
{
    private readonly IDatabaseHealthService _health;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    public AdminViewModel(
        IDatabaseHealthService health,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme)
    {
        _health = health;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;

        Refresh();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<FolderRow> Folders { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;
    public bool CanRun => Has(Permission.RunDatabaseHealthCheck);

    [ObservableProperty] private string _databaseLocation = string.Empty;
    [ObservableProperty] private string _databaseSize = string.Empty;
    [ObservableProperty] private string _schemaVersion = string.Empty;
    [ObservableProperty] private string _appVersion = string.Empty;
    [ObservableProperty] private string _lastBackup = string.Empty;
    [ObservableProperty] private string _lastBackupFailure = string.Empty;
    [ObservableProperty] private string _availableDisk = string.Empty;
    [ObservableProperty] private string _integrityStatus = string.Empty;

    private int UserId => _session.CurrentUser!.Id;

    [RelayCommand]
    private void Refresh()
    {
        var h = _health.GetHealth();
        DatabaseLocation = h.DatabaseLocation;
        DatabaseSize = FormatBytes(h.DatabaseSizeBytes);
        SchemaVersion = h.SchemaVersion;
        AppVersion = h.AppVersion;
        LastBackup = h.LastBackupUtc?.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture) ?? "None recorded";
        LastBackupFailure = h.LastBackupFailureUtc?.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture) ?? "None recorded";
        AvailableDisk = FormatBytes(h.AvailableDiskBytes);
        IntegrityStatus = h.IntegrityStatus;

        Folders.Clear();
        foreach (var f in h.Folders)
        {
            Folders.Add(new FolderRow(f));
        }
    }

    [RelayCommand]
    private void RunCheck()
    {
        Feedback.Clear();
        if (!CanRun)
        {
            Feedback.Error("You do not have permission to run database checks.");
            return;
        }

        var result = _health.RunIntegrityCheck(UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        IntegrityStatus = result.Value!;
        if (string.Equals(result.Value, "ok", StringComparison.OrdinalIgnoreCase))
        {
            Feedback.Success("Database integrity check passed.");
        }
        else
        {
            Feedback.Warning($"Integrity check reported: {result.Value}");
        }
    }

    [RelayCommand]
    private void ValidateFolders()
    {
        Feedback.Clear();
        if (!CanRun)
        {
            Feedback.Error("You do not have permission to run this check.");
            return;
        }

        var result = _health.ValidateManagedFolders(UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success("Managed folders validated.");
    }

    [RelayCommand]
    private void CreateDiagnostic()
    {
        Feedback.Clear();
        if (!CanRun)
        {
            Feedback.Error("You do not have permission to create a diagnostic summary.");
            return;
        }

        var result = _health.CreateDiagnosticSummary(null, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Feedback.Success($"Diagnostic summary saved: {result.Value}");
    }

    [RelayCommand]
    private void CreateSupportBundle()
    {
        Feedback.Clear();
        if (!CanRun)
        {
            Feedback.Error("You do not have permission to create a support bundle.");
            return;
        }

        var result = _health.CreateSupportBundle(null, null, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Feedback.Success($"Support bundle created: {result.Value}");
    }

    [RelayCommand]
    private void OpenLogs() => _dialogs.OpenPath(_health.LogsFolder);

    [RelayCommand]
    private void OpenBackups() => _dialogs.OpenPath(_health.BackupsFolder);

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    private bool Has(Permission p) => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, p);

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.##} {units[u]}";
    }
}

/// <summary>A managed-folder status row.</summary>
public sealed class FolderRow
{
    private readonly FolderStatus _status;

    public FolderRow(FolderStatus status)
    {
        _status = status;
    }

    public string Name => _status.Name;
    public string Path => _status.Path;
    public string State => _status.Exists ? "OK" : "Missing";
    public string Detail => $"{_status.FileCount} file(s)";
}
