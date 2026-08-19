namespace SnookerPoint.Application.Authentication;

/// <summary>
/// Authenticates staff by password or PIN, enforcing account state (disabled),
/// failed-attempt lockout, and audit logging. Never exposes secrets.
/// </summary>
public interface IAuthenticationService
{
    LoginResult LoginWithPassword(string username, string password);

    LoginResult LoginWithPin(string username, string pin);

    /// <summary>Records a logout audit event for the given user.</summary>
    void Logout(int userId);
}

/// <summary>Account-security policy shared by the auth service.</summary>
public static class AccountSecurityPolicy
{
    /// <summary>Failed attempts allowed before a temporary lockout.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long an account stays locked after hitting the threshold.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
}
