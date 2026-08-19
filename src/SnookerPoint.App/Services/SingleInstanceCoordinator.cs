using System.Threading;

namespace SnookerPoint.App.Services;

/// <summary>
/// Named-mutex single-instance coordinator. Detection uses a per-user named mutex; a named
/// event lets a second launch signal the primary to foreground its window. Uses no drivers,
/// services or kernel components beyond standard synchronization primitives.
/// </summary>
public sealed class SingleInstanceCoordinator : ISingleInstanceCoordinator
{
    private readonly string _mutexName;
    private readonly string _eventName;

    private readonly Mutex _mutex;
    private EventWaitHandle? _activateEvent;
    private Thread? _listener;
    private volatile bool _owns;
    private volatile bool _stopping;

    public event Action? ActivationRequested;

    /// <param name="nameSuffix">Optional suffix to isolate the mutex/event names (used by tests).</param>
    public SingleInstanceCoordinator(string? nameSuffix = null)
    {
        var suffix = string.IsNullOrEmpty(nameSuffix) ? string.Empty : "." + nameSuffix;
        _mutexName = @"Local\SnookerPoint.SingleInstance" + suffix;
        _eventName = @"Local\SnookerPoint.Activate" + suffix;
        _mutex = new Mutex(initiallyOwned: false, _mutexName);
    }

    public bool TryAcquire()
    {
        try
        {
            _owns = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited without releasing (e.g. a crash) — we now own it.
            _owns = true;
        }

        if (_owns)
        {
            StartActivationListener();
        }

        return _owns;
    }

    public void Release()
    {
        _stopping = true;
        try
        {
            _activateEvent?.Set(); // wake the listener so it can exit
        }
        catch
        {
            // ignore
        }

        if (_owns)
        {
            try { _mutex.ReleaseMutex(); } catch { /* not owned */ }
            _owns = false;
        }
    }

    public void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(_eventName, out var handle))
            {
                handle.Set();
                handle.Dispose();
            }
        }
        catch
        {
            // Best-effort: if we can't signal, the caller simply shows a message.
        }
    }

    private void StartActivationListener()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        _listener = new Thread(() =>
        {
            while (!_stopping)
            {
                try
                {
                    if (_activateEvent.WaitOne(TimeSpan.FromMilliseconds(500)) && !_stopping)
                    {
                        ActivationRequested?.Invoke();
                    }
                }
                catch
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "SnookerPoint.SingleInstanceListener",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        Release();
        try { _activateEvent?.Dispose(); } catch { /* ignore */ }
        try { _mutex.Dispose(); } catch { /* ignore */ }
    }
}
