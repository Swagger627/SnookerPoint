using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Backups;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// Backup and restore for Owner/Administrator users. Create a backup now (optionally to a
/// chosen folder), review previous backups, validate them, and restore — which always takes a
/// safety backup first and requires a typed confirmation. Restore is permission-restricted.
/// </summary>
public partial class BackupViewModel : ObservableObject
{
    private readonly IBackupService _backups;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IApplicationControl _appControl;

    public BackupViewModel(
        IBackupService backups,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme,
        IApplicationControl appControl)
    {
        _backups = backups;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;
        _appControl = appControl;

        Refresh();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<BackupRow> Backups { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;
    public bool IsEmpty => Backups.Count == 0;

    public bool CanCreate => Has(Permission.CreateBackup);
    public bool CanRestore => Has(Permission.RestoreBackup);

    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _destinationFolder = string.Empty;
    [ObservableProperty] private BackupRow? _selectedBackup;
    [ObservableProperty] private string _restoreConfirmation = string.Empty;

    private int UserId => _session.CurrentUser!.Id;

    [RelayCommand]
    private void Refresh()
    {
        Backups.Clear();
        foreach (var b in _backups.ListBackups())
        {
            Backups.Add(new BackupRow(b));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        var picked = _dialogs.PickFolder(string.IsNullOrWhiteSpace(DestinationFolder) ? _backups.DefaultBackupsFolder : DestinationFolder);
        if (picked is not null)
        {
            DestinationFolder = picked;
        }
    }

    [RelayCommand]
    private void CreateBackup()
    {
        Feedback.Clear();
        if (!CanCreate)
        {
            Feedback.Error("You do not have permission to create backups.");
            return;
        }

        var result = _backups.CreateBackup(
            string.IsNullOrWhiteSpace(DestinationFolder) ? null : DestinationFolder,
            Description, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Description = string.Empty;
        Refresh();
        Feedback.Success($"Backup created: {result.Value!.FileName}");
    }

    [RelayCommand]
    private void Validate(BackupRow? row)
    {
        Feedback.Clear();
        if (row is null)
        {
            return;
        }

        var v = _backups.Validate(row.FilePath);
        row.SetValidation(v.Status.ToString(), v.Message);
        if (v.IsValid)
        {
            Feedback.Success(v.Message);
        }
        else
        {
            Feedback.Warning(v.Message);
        }
    }

    [RelayCommand]
    private void Restore(BackupRow? row)
    {
        Feedback.Clear();
        if (row is null)
        {
            return;
        }

        if (!CanRestore)
        {
            Feedback.Error("You do not have permission to restore backups.");
            return;
        }

        var validation = _backups.Validate(row.FilePath);
        if (!validation.IsValid)
        {
            row.SetValidation(validation.Status.ToString(), validation.Message);
            Feedback.Error($"This backup cannot be restored: {validation.Message}");
            return;
        }

        if (!_dialogs.Confirm("Restore backup",
                $"This will REPLACE all current data with the backup from {row.CreatedText}.\n\nA safety backup of your current data will be made first. Continue?"))
        {
            return;
        }

        // The service validates, takes a safety backup, replaces the files atomically, and
        // preserves the original data on failure. It only reports success once the swap is done.
        var result = _backups.RestoreBackup(row.FilePath, RestoreConfirmation, UserId);
        if (result.Failed)
        {
            // A failed restore never restarts the app; the original data is preserved.
            Feedback.Error(result.ErrorMessage);
            return;
        }

        RestoreConfirmation = string.Empty;

        // On success, restart cleanly: a fresh instance starts (running any migration) and this
        // one exits. If the new instance cannot be started, nothing is shut down.
        if (!_appControl.RestartApplication())
        {
            _dialogs.ShowInfo("Restore complete",
                "Your data was restored successfully, but Snooker Point could not restart automatically. Please close Snooker Point and open it again.");
            _session.SignOut();
            _navigation.ShowLogin();
        }
    }

    [RelayCommand]
    private void OpenBackupsFolder() => _dialogs.OpenPath(_backups.DefaultBackupsFolder);

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    private bool Has(Permission p) => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, p);
}

/// <summary>A backup row for the list.</summary>
public partial class BackupRow : ObservableObject
{
    private readonly BackupInfo _info;

    public BackupRow(BackupInfo info)
    {
        _info = info;
        _validationStatus = "Not checked";
    }

    public string FilePath => _info.FilePath;
    public string FileName => _info.FileName;
    public string CreatedText => _info.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture);
    public string ClubName => _info.ClubName;
    public string Description => string.IsNullOrWhiteSpace(_info.Description) ? "—" : _info.Description!;
    public string SizeText => FormatBytes(_info.SizeBytes);
    public string Kind => _info.Automatic ? "Automatic" : FileName.Contains("SAFETY", StringComparison.OrdinalIgnoreCase) ? "Safety" : "Manual";

    [ObservableProperty] private string _validationStatus;
    [ObservableProperty] private string? _validationMessage;

    public void SetValidation(string status, string message)
    {
        ValidationStatus = status;
        ValidationMessage = message;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.##} {units[u]}";
    }
}
