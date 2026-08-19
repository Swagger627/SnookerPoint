using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the staff editor dialog. In create mode it collects a display name, username,
/// role and initial password (plus optional PIN); in edit mode it collects only the
/// display name, username and role. Secrets are entered here but never displayed for
/// an existing account.
/// </summary>
public partial class StaffEditDialogViewModel : ObservableObject
{
    public StaffEditDialogViewModel(StaffEditContext context)
    {
        IsNew = context.IsNew;
        _displayName = context.DisplayName;
        _username = context.Username;
        RoleOptions = context.RoleOptions;
        _selectedRole = context.Role;
        Title = context.IsNew ? "Add staff account" : "Edit staff account";
    }

    public bool IsNew { get; }
    public string Title { get; }
    public IReadOnlyList<UserRole> RoleOptions { get; }

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _username;
    [ObservableProperty] private UserRole _selectedRole;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _pin = string.Empty;
    [ObservableProperty] private string _confirmPin = string.Empty;
    [ObservableProperty] private bool _revealSecrets;
    [ObservableProperty] private string? _errorMessage;

    public StaffEditInput? Result { get; private set; }

    public bool TryConfirm()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ErrorMessage = "Please enter a display name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Please enter a username.";
            return false;
        }

        if (!IsNew)
        {
            Result = new StaffEditInput(DisplayName.Trim(), Username.Trim(), SelectedRole, null, null);
            return true;
        }

        if (StaffCredentialRules.ValidatePassword(Password) is { } pwdError)
        {
            ErrorMessage = pwdError;
            return false;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "The password and confirmation do not match.";
            return false;
        }

        var pin = string.IsNullOrWhiteSpace(Pin) ? null : Pin.Trim();
        if (StaffCredentialRules.ValidatePin(pin) is { } pinError)
        {
            ErrorMessage = pinError;
            return false;
        }

        if (pin is not null && !string.Equals(pin, ConfirmPin?.Trim(), StringComparison.Ordinal))
        {
            ErrorMessage = "The PIN and confirmation do not match.";
            return false;
        }

        Result = new StaffEditInput(DisplayName.Trim(), Username.Trim(), SelectedRole, Password, pin);
        return true;
    }
}
