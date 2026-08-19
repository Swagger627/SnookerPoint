using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Staff;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the credential dialog used to reset a password or set/change/remove a PIN for
/// an existing account. The current secret is never shown; the operator enters a new
/// value (or, for a PIN, leaves it blank to remove).
/// </summary>
public partial class SetCredentialDialogViewModel : ObservableObject
{
    public SetCredentialDialogViewModel(SetCredentialContext context)
    {
        IsPin = context.IsPin;
        SecretName = context.IsPin ? "PIN" : "password";
        Title = context.IsPin ? $"Set PIN — {context.StaffName}" : $"Reset password — {context.StaffName}";
        Prompt = context.IsPin
            ? "Enter a new PIN, or leave both boxes blank to remove the PIN."
            : "Enter a new password for this account.";
    }

    public bool IsPin { get; }
    public string SecretName { get; }
    public string Title { get; }
    public string Prompt { get; }

    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private string _confirmValue = string.Empty;
    [ObservableProperty] private bool _reveal;
    [ObservableProperty] private string? _errorMessage;

    public SetCredentialInput? Result { get; private set; }

    public bool TryConfirm()
    {
        var value = Value?.Trim() ?? string.Empty;
        var confirm = ConfirmValue?.Trim() ?? string.Empty;

        if (IsPin)
        {
            // Blank in both boxes means "remove the PIN".
            if (value.Length == 0 && confirm.Length == 0)
            {
                Result = new SetCredentialInput(null);
                return true;
            }

            if (StaffCredentialRules.ValidatePin(value) is { } pinError)
            {
                ErrorMessage = pinError;
                return false;
            }

            if (!string.Equals(value, confirm, StringComparison.Ordinal))
            {
                ErrorMessage = "The PIN and confirmation do not match.";
                return false;
            }

            Result = new SetCredentialInput(value);
            return true;
        }

        if (StaffCredentialRules.ValidatePassword(Value) is { } pwdError)
        {
            ErrorMessage = pwdError;
            return false;
        }

        if (!string.Equals(Value, ConfirmValue, StringComparison.Ordinal))
        {
            ErrorMessage = "The password and confirmation do not match.";
            return false;
        }

        Result = new SetCredentialInput(Value);
        return true;
    }
}
