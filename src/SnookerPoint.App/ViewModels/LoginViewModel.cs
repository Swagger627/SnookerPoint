using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;

namespace SnookerPoint.App.ViewModels;

/// <summary>Backs the login screen (password or optional PIN, friendly errors, lockout).</summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _auth;
    private readonly ISessionContext _session;
    private readonly INavigationService _navigation;
    private readonly IOwnerRecoveryService _recovery;
    private readonly IDialogService _dialogs;

    public LoginViewModel(
        IAuthenticationService auth,
        ISessionContext session,
        INavigationService navigation,
        IOwnerRecoveryService recovery,
        IDialogService dialogs)
    {
        _auth = auth;
        _session = session;
        _navigation = navigation;
        _recovery = recovery;
        _dialogs = dialogs;
    }

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _pin = string.Empty;

    [ObservableProperty] private bool _usePin;
    [ObservableProperty] private bool _showPassword;

    [ObservableProperty] private string? _errorMessage;

    public bool UsePassword => !UsePin;

    /// <summary>Label for the show/hide toggle, reflecting the current mode.</summary>
    public string ShowSecretLabel => UsePin
        ? (ShowPassword ? "Hide PIN" : "Show PIN")
        : (ShowPassword ? "Hide password" : "Show password");

    partial void OnUsePinChanged(bool value)
    {
        ErrorMessage = null;

        // Clear only the credential from the mode we're leaving — never the username.
        if (value)
        {
            Password = string.Empty;
        }
        else
        {
            Pin = string.Empty;
        }

        // Don't carry a revealed state across a mode switch.
        ShowPassword = false;

        OnPropertyChanged(nameof(UsePassword));
        OnPropertyChanged(nameof(ShowSecretLabel));
    }

    partial void OnShowPasswordChanged(bool value) => OnPropertyChanged(nameof(ShowSecretLabel));

    [RelayCommand]
    private void UsePasswordMode() => UsePin = false;

    [RelayCommand]
    private void UsePinMode() => UsePin = true;

    [RelayCommand]
    private void Login()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Please enter your username.";
            return;
        }

        var result = UsePin
            ? _auth.LoginWithPin(Username, Pin)
            : _auth.LoginWithPassword(Username, Password);

        if (result.Succeeded && result.User is not null)
        {
            _session.SignIn(result.User);
            Password = string.Empty;
            Pin = string.Empty;

            // A temporary password must be changed before doing anything else.
            if (result.User.MustChangePassword)
            {
                _navigation.ShowAccount();
                return;
            }

            _navigation.ShowHome();
            return;
        }

        ErrorMessage = DescribeFailure(result);
    }

    [RelayCommand]
    private void ForgotCredentials()
    {
        ErrorMessage = null;

        var status = _recovery.GetStatus();
        var input = _dialogs.ShowForgotPassword(new ForgotPasswordContext(status));
        if (input is null)
        {
            return;
        }

        var result = _recovery.Recover(input.Username, input.RecoveryCode, input.NewPassword, input.NewPin);
        if (result.Failed || result.Value is null)
        {
            _dialogs.ShowError("Could not recover account", result.ErrorMessage);
            return;
        }

        // Show the replacement code, then let them sign in with the new password.
        _dialogs.ShowRecoveryCode(result.Value.NewRecoveryCode);
        _dialogs.ShowInfo("Account recovered",
            "Your password has been reset. A new recovery code has been issued — keep it safe. You can now sign in.");
        Username = input.Username;
        Password = string.Empty;
        UsePin = false;
    }

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current.Shutdown();

    private string DescribeFailure(LoginResult result) => result.Reason switch
    {
        LoginFailureReason.AccountDisabled =>
            "This account has been disabled. Please contact the club owner.",
        LoginFailureReason.AccountLockedOut =>
            $"Too many attempts. Please wait {FormatRemaining(result.LockoutRemaining)} and try again.",
        LoginFailureReason.PinNotSet =>
            "PIN login is not set up for this account. Sign in with your password and add a PIN in My Account.",
        _ => UsePin
            ? "The username or PIN is incorrect."
            : "The username or password is incorrect.",
    };

    private static string FormatRemaining(TimeSpan? remaining)
    {
        if (remaining is not { } value || value <= TimeSpan.Zero)
        {
            return "a moment";
        }

        var minutes = (int)Math.Ceiling(value.TotalMinutes);
        return minutes <= 1 ? "about a minute" : $"about {minutes} minutes";
    }
}
