using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The My Account / Security screen. A signed-in user changes their own password, adds
/// or changes their PIN, or removes it — each after re-entering their current password.
/// When the account was issued a temporary password, the screen opens in forced mode and
/// only lets the user continue once a new password is set. Owners can also generate an
/// offline recovery code here.
/// </summary>
public partial class AccountViewModel : ObservableObject
{
    private readonly IAccountSecurityService _account;
    private readonly IOwnerRecoveryService _recovery;
    private readonly ISessionContext _session;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    public AccountViewModel(
        IAccountSecurityService account,
        IOwnerRecoveryService recovery,
        ISessionContext session,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme)
    {
        _account = account;
        _recovery = recovery;
        _session = session;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;

        IsForcedChange = session.CurrentUser?.MustChangePassword ?? false;
        _hasPin = session.CurrentUser?.HasPin ?? false;
    }

    private AuthenticatedUser User => _session.CurrentUser
        ?? throw new InvalidOperationException("No signed-in user.");

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    public bool IsForcedChange { get; }
    public bool CanLeave => !IsForcedChange;
    public bool IsOwner => _session.CurrentUser?.Role == UserRole.Owner;

    /// <summary>The one feedback banner shown at the top of the screen.</summary>
    public FeedbackViewModel Feedback { get; } = new();

    // ---- PIN state-aware UI ----

    /// <summary>Whether the signed-in account currently has a PIN.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinCardTitle))]
    [NotifyPropertyChangedFor(nameof(PinActionLabel))]
    [NotifyPropertyChangedFor(nameof(ShowRemovePinCard))]
    private bool _hasPin;

    /// <summary>The add/change PIN card is available whenever the user isn't forced to change a password.</summary>
    public bool ShowPinCard => !IsForcedChange;

    /// <summary>Remove PIN is only offered when a PIN exists (and not during a forced change).</summary>
    public bool ShowRemovePinCard => HasPin && !IsForcedChange;

    public string PinCardTitle => HasPin ? "Change PIN" : "Add PIN";
    public string PinActionLabel => HasPin ? "Change PIN" : "Add PIN";

    // Change password
    [ObservableProperty] private string _currentPassword = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private bool _revealPassword;

    // Change PIN
    [ObservableProperty] private string _pinCurrentPassword = string.Empty;
    [ObservableProperty] private string _newPin = string.Empty;
    [ObservableProperty] private string _confirmPin = string.Empty;
    [ObservableProperty] private bool _revealPin;

    // Remove PIN
    [ObservableProperty] private string _removePinCurrentPassword = string.Empty;

    // Recovery code (Owner)
    [ObservableProperty] private string _recoveryCurrentPassword = string.Empty;

    // ==================== COMMANDS ====================

    [RelayCommand]
    private void ChangePassword()
    {
        Feedback.Clear();

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            // A mismatch is a re-typing slip — keep what was entered so it can be fixed.
            Feedback.Error("The new passwords do not match.");
            return;
        }

        OperationResult result;
        try
        {
            result = _account.ChangePassword(User.Id, CurrentPassword, NewPassword);
        }
        catch
        {
            Feedback.Error("We could not change your password. Please try again.");
            return;
        }

        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;

        // Reflect the cleared must-change flag in the session.
        _session.SignIn(User with { MustChangePassword = false });

        if (IsForcedChange)
        {
            _dialogs.ShowInfo("Password changed", "Your password was changed successfully. Welcome back!");
            _navigation.ShowHome();
            return;
        }

        Feedback.Success("Your password was changed successfully.");
    }

    [RelayCommand]
    private void ChangePin()
    {
        Feedback.Clear();

        if (!string.Equals(NewPin, ConfirmPin, StringComparison.Ordinal))
        {
            Feedback.Error("The new PINs do not match.");
            return;
        }

        var wasAdding = !HasPin;

        OperationResult result;
        try
        {
            result = _account.ChangePin(User.Id, PinCurrentPassword, NewPin);
        }
        catch
        {
            Feedback.Error("We could not save your PIN. Please try again.");
            return;
        }

        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        PinCurrentPassword = NewPin = ConfirmPin = string.Empty;

        // The account now has a PIN — refresh the screen state and the session so PIN
        // login is available immediately after the next logout.
        _session.SignIn(User with { HasPin = true });
        HasPin = true;

        Feedback.Success(wasAdding
            ? "Your PIN was added successfully."
            : "Your PIN was changed successfully.");
    }

    [RelayCommand]
    private void RemovePin()
    {
        Feedback.Clear();

        if (!_dialogs.Confirm("Remove PIN", "Remove your PIN? PIN login will be disabled until you set a new one."))
        {
            return;
        }

        OperationResult result;
        try
        {
            result = _account.RemovePin(User.Id, RemovePinCurrentPassword);
        }
        catch
        {
            Feedback.Error("We could not remove your PIN. Please try again.");
            return;
        }

        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        RemovePinCurrentPassword = string.Empty;

        _session.SignIn(User with { HasPin = false });
        HasPin = false;

        Feedback.Success("Your PIN was removed successfully.");
    }

    [RelayCommand]
    private void GenerateRecoveryCode()
    {
        Feedback.Clear();

        if (!IsOwner)
        {
            return;
        }

        var result = _recovery.RegenerateCode(User.Id, RecoveryCurrentPassword);
        if (result.Failed || result.Value is null)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        RecoveryCurrentPassword = string.Empty;
        _dialogs.ShowRecoveryCode(result.Value);
        Feedback.Success("A new recovery code was generated. Your previous code no longer works.");
    }

    [RelayCommand]
    private void Home()
    {
        if (CanLeave)
        {
            _navigation.ShowHome();
        }
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();
}
