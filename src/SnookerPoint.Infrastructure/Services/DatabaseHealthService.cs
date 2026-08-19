using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Backups;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Diagnostics;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Read-only database and environment diagnostics. Reports locations, sizes, schema version,
/// folder status, disk space and integrity, and writes a secret-free diagnostic summary. It
/// never exposes SQL or a data-editing surface.
/// </summary>
public sealed class DatabaseHealthService : IDatabaseHealthService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly AppDataPaths _paths;
    private readonly IBackupService _backups;
    private readonly IClock _clock;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(
        IDbContextFactory<SnookerPointDbContext> factory,
        AppDataPaths paths,
        IBackupService backups,
        IClock clock,
        ILogger<DatabaseHealthService> logger)
    {
        _factory = factory;
        _paths = paths;
        _backups = backups;
        _clock = clock;
        _logger = logger;
    }

    public string LogsFolder => _paths.Logs;
    public string BackupsFolder => _paths.Backups;

    public DatabaseHealth GetHealth()
    {
        using var db = _factory.CreateDbContext();
        var dbPath = db.Database.GetDbConnection().DataSource;
        var schema = db.Database.GetAppliedMigrations().LastOrDefault() ?? "unknown";

        var lastBackup = _backups.ListBackups().FirstOrDefault()?.CreatedUtc;
        var lastFailure = db.AuditEvents.AsNoTracking()
            .Where(e => e.Action == AuditActions.BackupFailed)
            .OrderByDescending(e => e.Id)
            .Select(e => (DateTimeOffset?)e.Utc)
            .FirstOrDefault();

        return new DatabaseHealth(
            dbPath,
            SafeSize(dbPath),
            schema,
            lastBackup,
            lastFailure,
            "Run a check to verify",
            Folders(),
            AvailableDiskBytes(dbPath),
            AppVersion);
    }

    public OperationResult<string> RunIntegrityCheck(int actorUserId)
    {
        try
        {
            string dbPath;
            using (var db = _factory.CreateDbContext())
            {
                dbPath = db.Database.GetDbConnection().DataSource;
            }

            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar() as string ?? "unknown";
            conn.Close();

            WriteAudit(AuditActions.DatabaseHealthChecked, actorUserId, $"Integrity check: {result}.");
            return OperationResult<string>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database integrity check failed to run.");
            return OperationResult<string>.Failure("The database check could not be run. Please try again.");
        }
    }

    public OperationResult<IReadOnlyList<FolderStatus>> ValidateManagedFolders(int actorUserId)
    {
        try
        {
            _paths.EnsureLiveDirectories();
            var folders = Folders();
            WriteAudit(AuditActions.DatabaseHealthChecked, actorUserId, "Managed folders validated.");
            return OperationResult<IReadOnlyList<FolderStatus>>.Success(folders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validating managed folders failed.");
            return OperationResult<IReadOnlyList<FolderStatus>>.Failure("The managed folders could not be validated. Check disk space and permissions.");
        }
    }

    public OperationResult<string> CreateDiagnosticSummary(string? destinationFolder, int actorUserId)
    {
        try
        {
            var health = GetHealth();
            var folder = string.IsNullOrWhiteSpace(destinationFolder) ? _paths.Exports : destinationFolder!;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"SnookerPoint-Diagnostics-{_clock.UtcNow.ToLocalTime():yyyyMMdd-HHmmss}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("Snooker Point — Diagnostic Summary");
            sb.AppendLine("(Contains no passwords, PINs, recovery codes or customer secrets.)");
            sb.AppendLine();
            sb.AppendLine($"Generated (local): {_clock.UtcNow.ToLocalTime():dd MMM yyyy HH:mm:ss}");
            sb.AppendLine($"Application version: {health.AppVersion}");
            sb.AppendLine($"Schema/migration version: {health.SchemaVersion}");
            sb.AppendLine($"Database location: {health.DatabaseLocation}");
            sb.AppendLine($"Database size: {FormatBytes(health.DatabaseSizeBytes)}");
            sb.AppendLine($"Available disk space: {FormatBytes(health.AvailableDiskBytes)}");
            sb.AppendLine($"Last successful backup: {(health.LastBackupUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "none recorded")}");
            sb.AppendLine($"Last backup failure: {(health.LastBackupFailureUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "none recorded")}");
            sb.AppendLine();
            sb.AppendLine("Managed folders:");
            foreach (var f in health.Folders)
            {
                sb.AppendLine($"  {f.Name}: {(f.Exists ? "OK" : "MISSING")} — {f.FileCount} file(s), {FormatBytes(f.SizeBytes)} — {f.Path}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            WriteAudit(AuditActions.DiagnosticSummaryCreated, actorUserId, $"Diagnostic summary written to {Path.GetFileName(path)}.");
            return OperationResult<string>.Success(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Writing the diagnostic summary failed.");
            return OperationResult<string>.Failure("The diagnostic summary could not be saved. Please choose another folder.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Writing the diagnostic summary failed unexpectedly.");
            return OperationResult<string>.Failure("The diagnostic summary could not be created.");
        }
    }

    public OperationResult<string> CreateSupportBundle(string? destinationFolder, string? licensingStatusCode, int actorUserId)
    {
        string? working = null;
        try
        {
            var health = GetHealth();
            var folder = string.IsNullOrWhiteSpace(destinationFolder) ? _paths.Exports : destinationFolder!;
            Directory.CreateDirectory(folder);
            var bundlePath = Path.Combine(folder, $"SnookerPoint-Support-{_clock.UtcNow.ToLocalTime():yyyyMMdd-HHmmss}.zip");

            working = Path.Combine(Path.GetTempPath(), $"spsupport-{Guid.NewGuid():N}");
            Directory.CreateDirectory(working);

            // 1) Sanitised summary (no secrets).
            var sb = new StringBuilder();
            sb.AppendLine("Snooker Point — Support Bundle");
            sb.AppendLine("(No passwords, PINs, recovery codes, keys, licence text, raw machine IDs, images, receipts or database are included.)");
            sb.AppendLine();
            sb.AppendLine($"Generated (local): {_clock.UtcNow.ToLocalTime():dd MMM yyyy HH:mm:ss}");
            sb.AppendLine($"Application version: {health.AppVersion}");
            sb.AppendLine($"Windows version: {Environment.OSVersion.VersionString}");
            sb.AppendLine($"Schema/migration version: {health.SchemaVersion}");
            sb.AppendLine($"Licensing status code: {licensingStatusCode ?? "n/a"}");
            sb.AppendLine($"Database size: {FormatBytes(health.DatabaseSizeBytes)}");
            sb.AppendLine($"Available disk space: {FormatBytes(health.AvailableDiskBytes)}");
            sb.AppendLine($"Last successful backup: {(health.LastBackupUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "none recorded")}");
            sb.AppendLine($"Last backup failure: {(health.LastBackupFailureUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "none recorded")}");
            sb.AppendLine();
            sb.AppendLine("Managed folders:");
            foreach (var f in health.Folders)
            {
                sb.AppendLine($"  {f.Name}: {(f.Exists ? "OK" : "MISSING")} — {f.FileCount} file(s), {FormatBytes(f.SizeBytes)}");
            }

            File.WriteAllText(Path.Combine(working, "support-summary.txt"), sb.ToString(), Encoding.UTF8);

            // 2) The most recent few log files (our logs never contain secrets by design).
            if (Directory.Exists(_paths.Logs))
            {
                var logsDir = Path.Combine(working, "Logs");
                Directory.CreateDirectory(logsDir);
                foreach (var log in Directory.EnumerateFiles(_paths.Logs, "*.log")
                             .OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
                {
                    try { File.Copy(log, Path.Combine(logsDir, Path.GetFileName(log)), overwrite: true); }
                    catch { /* skip a locked log */ }
                }
            }

            if (File.Exists(bundlePath))
            {
                File.Delete(bundlePath);
            }

            System.IO.Compression.ZipFile.CreateFromDirectory(working, bundlePath);
            WriteAudit(AuditActions.DiagnosticSummaryCreated, actorUserId, $"Support bundle created: {Path.GetFileName(bundlePath)}.");
            return OperationResult<string>.Success(bundlePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Creating the support bundle failed.");
            return OperationResult<string>.Failure("The support bundle could not be saved. Please choose another folder.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creating the support bundle failed unexpectedly.");
            return OperationResult<string>.Failure("The support bundle could not be created.");
        }
        finally
        {
            try { if (working is not null && Directory.Exists(working)) Directory.Delete(working, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private IReadOnlyList<FolderStatus> Folders() => new[]
    {
        FolderInfoFor("Images", _paths.Images),
        FolderInfoFor("Receipts", _paths.Receipts),
        FolderInfoFor("Exports", _paths.Exports),
        FolderInfoFor("Backups", _paths.Backups),
        FolderInfoFor("Logs", _paths.Logs),
    };

    private static FolderStatus FolderInfoFor(string name, string path)
    {
        if (!Directory.Exists(path))
        {
            return new FolderStatus(name, path, false, 0, 0);
        }

        long size = 0;
        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                count++;
                size += SafeSize(file);
            }
        }
        catch
        {
            // best-effort sizing
        }

        return new FolderStatus(name, path, true, count, size);
    }

    private static long AvailableDiskBytes(string dbPath)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dbPath));
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    private static long SafeSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static string AppVersion => typeof(DatabaseHealthService).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    private void WriteAudit(string action, int actorUserId, string details)
    {
        try
        {
            using var db = _factory.CreateDbContext();
            db.AuditEvents.Add(new AuditEvent
            {
                Utc = _clock.UtcNow,
                Action = action,
                ActorUserId = actorUserId,
                Entity = "Database",
                Details = details,
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the diagnostics audit event.");
        }
    }
}
