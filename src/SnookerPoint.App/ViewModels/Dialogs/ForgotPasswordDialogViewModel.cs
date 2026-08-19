using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the "Forgot password or PIN?" dialog. Staff accounts are told to ask an Owner
/// or Administrator to reset them. When an Owner recovery code exists, an offline Owner
/// recovery form is offered (username + recovery code + new password + optional PIN).
/// </summary>
public partial class ForgotPasswordDialogViewModel : ObservableObject
{
    public ForgotPasswordDialogViewModel(ForgotPasswordContext context)
    {
        OwnerRecoveryAvailable = context.Status.OwnerHasRecoveryCode;
    }

    /// <summary>True when an Owner has a recovery code, so offline recovery is possible.</summary>
    public bool OwnerRecoveryAvailable { get; }

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _recoveryCode = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _newPin = string.Empty;
    [ObservableProperty] private string _confirmPin = string.Empty;
    [ObservableProperty] private bool _revealSecrets;
    [ObservableProperty] private string? _errorMessage;

    public ForgotRecoveryInput? Result { get; private set; }

    public bool TryConfirm()
    {
        if (!OwnerRecoveryAvailable)
        {
            // Nothing to submit; the dialog is informational for staff accounts.
            return false;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Please enter the Owner username.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RecoveryCode))
        {
            ErrorMessage = "Please enter the recovery code.";
            return false;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "The new password and confirmation do not match.";
            return false;
        }

        var pin = string.IsNullOrWhiteSpace(NewPin) ? null : NewPin.Trim();
        if (pin is not null && !string.Equals(pin, ConfirmPin?.Trim(), StringComparison.Ordinal))
        {
            ErrorMessage = "The PIN and confirmation do not match.";
            return false;
        }

        Result = new ForgotRecoveryInput(Username.Trim(), RecoveryCode.Trim(), NewPassword, pin);
        return true;
    }
}
