using SnookerPoint.Application.Authentication;

namespace SnookerPoint.App.Services;

/// <summary>
/// Holds the currently signed-in user for the lifetime of a login session.
/// Cleared on logout. Never stores secrets.
/// </summary>
public interface ISessionContext
{
    AuthenticatedUser? CurrentUser { get; }

    bool IsAuthenticated { get; }

    void SignIn(AuthenticatedUser user);

    void SignOut();
}

/// <summary>Default in-memory session context.</summary>
public sealed class SessionContext : ISessionContext
{
    public AuthenticatedUser? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public void SignIn(AuthenticatedUser user) => CurrentUser = user;

    public void SignOut() => CurrentUser = null;
}
