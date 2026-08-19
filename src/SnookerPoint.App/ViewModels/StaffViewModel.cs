using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The Staff Management screen for Owner/Administrator users: view accounts, create and
/// edit them, assign roles, reset passwords, set/change/remove PINs, enable/disable, and
/// clear lockouts. All rules (only an Owner may create/promote an Owner, the last active
/// Owner is protected, unique usernames) are enforced by the staff service.
/// </summary>
public partial class StaffViewModel : ObservableObject
{
    private readonly IStaffManagementService _staff;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    public StaffViewModel(
        IStaffManagementService staff,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme)
    {
        _staff = staff;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;

        Reload();
    }

    public ObservableCollection<StaffRowViewModel> Rows { get; } = new();

    /// <summary>The one feedback banner shown at the top of the screen.</summary>
    public FeedbackViewModel Feedback { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    public bool CanManage =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageStaff);

    private int UserId => _session.CurrentUser!.Id;

    private bool ActorIsOwner => _session.CurrentUser?.Role == UserRole.Owner;

    /// <summary>An Administrator may assign every role except Owner; an Owner may assign all.</summary>
    private IReadOnlyList<UserRole> RoleOptions() => ActorIsOwner
        ? new[] { UserRole.Owner, UserRole.Administrator, UserRole.Manager, UserRole.Cashier, UserRole.FloorStaff }
        : new[] { UserRole.Administrator, UserRole.Manager, UserRole.Cashier, UserRole.FloorStaff };

    // ==================== COMMANDS ====================

    [RelayCommand]
    private void AddStaff()
    {
        if (!CanManage)
        {
            return;
        }

        Feedback.Clear();
        var context = new StaffEditContext(true, string.Empty, string.Empty, UserRole.Cashier, RoleOptions());
        var input = _dialogs.ShowStaffEditor(context);
        if (input is null)
        {
            return;
        }

        var result = _staff.CreateStaff(
            new CreateStaffRequest(input.DisplayName, input.Username, input.Role, input.Password!, input.Pin),
            UserId);
        Report(result, $"Staff account for {input.DisplayName} was created.");
    }

    [RelayCommand]
    private void Edit(StaffRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var context = new StaffEditContext(false, row.DisplayName, row.Username, row.Role, RoleOptions());
        var input = _dialogs.ShowStaffEditor(context);
        if (input is null)
        {
            return;
        }

        var result = _staff.UpdateStaff(
            new UpdateStaffRequest(row.Id, input.DisplayName, input.Username, input.Role),
            UserId);
        Report(result, $"{input.DisplayName}'s account was updated.");
    }

    [RelayCommand]
    private void ResetPassword(StaffRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var input = _dialogs.ShowSetCredential(new SetCredentialContext(false, row.DisplayName));
        if (input is null)
        {
            return;
        }

        var result = _staff.SetPassword(row.Id, input.Value!, UserId);
        Report(result, $"Password reset for {row.DisplayName}. Share the new password securely.");
    }

    [RelayCommand]
    private void TempPassword(StaffRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        if (!_dialogs.Confirm("Issue temporary password",
            $"Issue a temporary password for {row.DisplayName}? They must change it at next login."))
        {
            return;
        }

        var result = _staff.GenerateTemporaryPassword(row.Id, UserId);
        if (result.Failed || result.Value is null)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        // Shown once, copyable, and never written to logs or audit details.
        _dialogs.ShowTemporaryPassword(row.DisplayName, result.Value);
        Reload();
        Feedback.Success($"Temporary password issued for {row.DisplayName}. They must change it at next login.");
    }

    [RelayCommand]
    private void SetPin(StaffRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var input = _dialogs.ShowSetCredential(new SetCredentialContext(true, row.DisplayName));
        if (input is null)
        {
            return;
        }

        var removing = string.IsNullOrEmpty(input.Value);
        var result = _staff.SetPin(row.Id, input.Value, UserId);
        Report(result, removing
            ? $"PIN removed for {row.DisplayName}."
            : $"PIN set for {row.DisplayName}.");
    }

    [RelayCommand]
    private void ToggleActive(StaffRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var enabling = !row.IsActive;
        if (row.IsActive && !_dialogs.Confirm("Disable account",
            $"Disable {row.DisplayName}? They will not be able to log in until re-enabled."))
        {
            return;
        }

        var result = _staff.SetActive(row.Id, !row.IsActive, UserId);
        Report(result, enabling
            ? $"{row.DisplayName}'s account was enabled."
            : $"{row.DisplayName}'s account was disabled.");
    }

    [RelayCommand]
    private void ClearLockout(StaffRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var result = _staff.ClearLockout(row.Id, UserId);
        Report(result, $"{row.DisplayName}'s account was unlocked.");
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void Tables() => _navigation.ShowTables();

    // ==================== HELPERS ====================

    private void Report(OperationResult result, string successMessage)
    {
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Reload();
        Feedback.Success(successMessage);
    }

    private void Reload()
    {
        Rows.Clear();
        foreach (var item in _staff.GetAll())
        {
            Rows.Add(new StaffRowViewModel(item));
        }
    }
}
