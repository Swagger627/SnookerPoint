namespace SnookerPoint.App.Services;

/// <summary>
/// Ensures only one Snooker Point instance uses the local database at a time. A second launch
/// signals the first to come to the foreground and then exits, so SQLite is never contended.
/// The deliberate restore-restart workflow releases the lock before relaunching, so it is not
/// blocked.
/// </summary>
public interface ISingleInstanceCoordinator : IDisposable
{
    /// <summary>True if this process is the primary instance; false if another already holds the lock.</summary>
    bool TryAcquire();

    /// <summary>Releases the lock (used just before a deliberate restart so the new instance can acquire it).</summary>
    void Release();

    /// <summary>Signals an already-running primary instance to bring its window to the foreground.</summary>
    void SignalExistingInstance();

    /// <summary>Raised on the primary instance when another launch asks it to come to the foreground.</summary>
    event Action? ActivationRequested;
}
