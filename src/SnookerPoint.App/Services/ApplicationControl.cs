using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SnookerPoint.App.Services;

/// <summary>
/// Restarts the app by launching a new instance from the current executable and shutting the
/// current one down. No arguments (and therefore no secrets) are passed to the new process, so
/// the fresh instance simply performs its normal startup (including any pending migration).
/// </summary>
public sealed class ApplicationControl : IApplicationControl
{
    private readonly ISingleInstanceCoordinator? _singleInstance;

    public ApplicationControl(ISingleInstanceCoordinator? singleInstance = null)
    {
        _singleInstance = singleInstance;
    }

    public bool RestartApplication()
    {
        // Resolve the current executable (the published SnookerPoint.exe / SnookerPoint.App.exe).
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        try
        {
            // Release the single-instance lock first so the relaunched process can acquire it and
            // is not rejected as a "second instance" during the restore-restart.
            _singleInstance?.Release();

            // No command-line arguments: the new instance starts clean; nothing sensitive is passed.
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            });
        }
        catch (Exception)
        {
            return false;
        }

        // Only exit the current process once the new instance has been started.
        System.Windows.Application.Current?.Shutdown();
        return true;
    }

    public void Exit() => System.Windows.Application.Current?.Shutdown();
}
