namespace SnookerPoint.App.Services;

/// <summary>
/// Controls application-lifetime actions that a view-model cannot perform directly, such as a
/// clean restart after a database restore. Implementations must never pass secrets on the
/// command line.
/// </summary>
public interface IApplicationControl
{
    /// <summary>
    /// Starts a fresh instance of the application from the current executable (with no
    /// arguments) and shuts the current instance down. Returns false if a new instance could
    /// not be started, in which case nothing is shut down and the caller should tell the user
    /// to reopen the app manually.
    /// </summary>
    bool RestartApplication();

    /// <summary>Cleanly exits the application.</summary>
    void Exit();
}
