namespace SnookerPoint.Infrastructure.Storage;

/// <summary>
/// Resolves and (on demand) creates the per-user writable data directory tree for
/// Snooker Point, under <c>%APPDATA%\SnookerPoint\</c> as specified in §22 of the
/// planning document. Real and Demo/Training data live in separate subtrees so
/// training can never touch live data.
/// </summary>
/// <remarks>
/// Phase 0 establishes the folder layout and the live database path only. The Demo
/// subtree paths are exposed here for later phases but nothing writes to them yet.
/// </remarks>
public sealed class AppDataPaths
{
    private const string AppFolderName = "SnookerPoint";

    public AppDataPaths(string? rootOverride = null, string? machineRootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);

        Db = Path.Combine(Root, "Db");
        Images = Path.Combine(Root, "Images");
        ProductImages = Path.Combine(Images, "Products");
        Backups = Path.Combine(Root, "Backups");
        Receipts = Path.Combine(Root, "Receipts");
        Exports = Path.Combine(Root, "Exports");
        Logs = Path.Combine(Root, "Logs");
        License = Path.Combine(Root, "License");
        MachineLicense = machineRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppFolderName, "License");
        Demo = Path.Combine(Root, "Demo");
    }

    /// <summary>The application data root (<c>%APPDATA%\SnookerPoint</c>).</summary>
    public string Root { get; }

    public string Db { get; }
    public string Images { get; }

    /// <summary>Managed folder for product images (<c>Images\Products</c>).</summary>
    public string ProductImages { get; }

    public string Backups { get; }
    public string Receipts { get; }
    public string Exports { get; }
    public string Logs { get; }

    /// <summary>
    /// Per-user machine-bound licence and trial state (DPAPI CurrentUser). Deliberately excluded
    /// from business backups so a backup never clones activation to another computer.
    /// </summary>
    public string License { get; }

    /// <summary>
    /// Machine-level licence/trial checkpoint (under ProgramData, DPAPI LocalMachine). Shared
    /// across Windows users on the same computer, so switching users does not start a new trial.
    /// Best-effort: the installer grants write access; the app degrades to per-user state if this
    /// location is not writable.
    /// </summary>
    public string MachineLicense { get; }

    /// <summary>Root of the completely separate Demo/Training subtree.</summary>
    public string Demo { get; }

    /// <summary>Full path to the live SQLite database file.</summary>
    public string LiveDatabaseFile => Path.Combine(Db, "snookerpoint.db");

    /// <summary>Full path to the log folder used by the file logger.</summary>
    public string LogFile => Path.Combine(Logs, "snookerpoint-.log");

    /// <summary>Creates the live (non-demo) directory tree if it does not exist.</summary>
    public void EnsureLiveDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Db);
        Directory.CreateDirectory(Images);
        Directory.CreateDirectory(ProductImages);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Receipts);
        Directory.CreateDirectory(Exports);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(License);

        // Machine-level checkpoint is best-effort: a non-admin user without the installer-provided
        // ProgramData folder simply degrades to per-user licence state.
        try
        {
            Directory.CreateDirectory(MachineLicense);
        }
        catch
        {
            // ignore — the licensing layer handles an unwritable machine location
        }
    }
}
