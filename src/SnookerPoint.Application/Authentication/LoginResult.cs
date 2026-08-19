using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Application.Authentication;

/// <summary>A signed-in user's non-secret identity, safe to hold in the UI session.</summary>
public sealed record AuthenticatedUser(
    int Id,
    string DisplayName,
    string Username,
    UserRole Role,
    bool HasPin,
    bool MustChangePassword = false);

/// <summary>Why a login attempt failed.</summary>
public enum LoginFailureReason
{
    InvalidCredentials,
    AccountDisabled,
    AccountLockedOut,
    PinNotSet,
}

/// <summary>The outcome of a login attempt.</summary>
public sealed class LoginResult
{
    private LoginResult(
        bool succeeded,
        AuthenticatedUser? user,
        LoginFailureReason? reason,
        TimeSpan? lockoutRemaining)
    {
        Succeeded = succeeded;
        User = user;
        Reason = reason;
        LockoutRemaining = lockoutRemaining;
    }

    public bool Succeeded { get; }
    public AuthenticatedUser? User { get; }
    public LoginFailureReason? Reason { get; }

    /// <summary>Remaining lockout time when <see cref="Reason"/> is AccountLockedOut.</summary>
    public TimeSpan? LockoutRemaining { get; }

    public static LoginResult Success(AuthenticatedUser user) =>
        new(true, user, null, null);

    public static LoginResult Failure(LoginFailureReason reason, TimeSpan? lockoutRemaining = null) =>
        new(false, null, reason, lockoutRemaining);
}
