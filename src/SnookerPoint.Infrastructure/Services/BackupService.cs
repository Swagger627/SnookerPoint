using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Backups;
using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Consistent, verifiable backups and safe restore. Backups snapshot the SQLite database
/// with the online backup API (never a raw WAL copy), bundle the images and receipts folders,
/// and record a manifest with per-file SHA-256 hashes. Restore validates first, always makes a
/// safety backup of the current data, and rolls back the database if the swap fails.
/// </summary>
public sealed class BackupService : IBackupService
{
    internal const int FormatVersion = 1;
    private const string Extension = ".spbak";
    private const string FilePrefix = "SnookerPoint-Backup-";
    private const string AutoPrefix = "SnookerPoint-Backup-AUTO-";
    private const string SafetyPrefix = "SnookerPoint-Backup-SAFETY-";
    private const string ManifestName = "manifest.json";
    private const string DbEntryPath = "Db/snookerpoint.db";
    internal const string RestoreConfirmationPhrase = "RESTORE";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly AppDataPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        IDbContextFactory<SnookerPointDbContext> factory,
        AppDataPaths paths,
        IClock clock,
        ILogger<BackupService> logger)
    {
        _factory = factory;
        _paths = paths;
        _clock = clock;
        _logger = logger;
    }

    public string DefaultBackupsFolder => _paths.Backups;

    // ==================== CREATE ====================

    public OperationResult<BackupInfo> CreateBackup(string? destinationFolder, string? description, int actorUserId, bool automatic = false) =>
        CreateBackupCore(destinationFolder, description, actorUserId, automatic ? AutoPrefix : FilePrefix, automatic);

    private OperationResult<BackupInfo> CreateBackupCore(string? destinationFolder, string? description, int actorUserId, string namePrefix, bool automatic)
    {
        var folder = string.IsNullOrWhiteSpace(destinationFolder) ? _paths.Backups : destinationFolder!;
        string? working = null;
        try
        {
            Directory.CreateDirectory(folder);
            working = Path.Combine(Path.GetTempPath(), $"spbak-{Guid.NewGuid():N}");
            Directory.CreateDirectory(working);

            string dbPath, clubName, schemaVersion;
            using (var db = _factory.CreateDbContext())
            {
                dbPath = db.Database.GetDbConnection().DataSource;
                clubName = db.ClubSettings.AsNoTracking().Select(c => c.ClubName).FirstOrDefault() ?? "Snooker Point";
                schemaVersion = db.Database.GetAppliedMigrations().LastOrDefault() ?? "unknown";
            }

            // 1) Consistent DB snapshot via the online backup API.
            var snapshotPath = Path.Combine(working, "snookerpoint.db");
            SnapshotDatabase(dbPath, snapshotPath);

            // 2) Gather the file set (relative path -> absolute source).
            var files = new List<(string Rel, string Abs)> { (DbEntryPath, snapshotPath) };
            AddFolder(files, _paths.Images, "Images");
            AddFolder(files, _paths.Receipts, "Receipts");

            // 3) Manifest with hashes.
            var manifest = new BackupManifest
            {
                FormatVersion = FormatVersion,
                AppVersion = AppVersion,
                SchemaVersion = schemaVersion,
                ClubName = clubName,
                Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
                Automatic = automatic,
                CreatedUtc = _clock.UtcNow,
                Files = files.Select(f => new BackupFileEntry
                {
                    Path = f.Rel,
                    Sha256 = Hash(f.Abs),
                    Size = new FileInfo(f.Abs).Length,
                }).ToList(),
            };

            // 4) Write the archive.
            var fileName = $"{namePrefix}{_clock.UtcNow.ToLocalTime():yyyyMMdd-HHmmss-fff}{Extension}";
            var destPath = Path.Combine(folder, fileName);
            WriteArchive(destPath, files, manifest);

            var info = ToInfo(destPath, manifest);
            WriteAudit(AuditActions.BackupCreated, actorUserId, "Backup",
                $"Backup created ({(automatic ? "automatic" : "manual")}): {fileName}, {manifest.Files.Count} file(s).");
            return OperationResult<BackupInfo>.Success(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup to {Folder} failed.", folder);
            WriteAudit(AuditActions.BackupFailed, actorUserId, "Backup", $"Backup failed: {ex.GetType().Name}.");
            return OperationResult<BackupInfo>.Failure(
                "The backup could not be created. Your live data is unchanged. Please check the backup folder and free disk space, then try again.");
        }
        finally
        {
            SafeDeleteDir(working);
        }
    }

    // ==================== LIST ====================

    public IReadOnlyList<BackupInfo> ListBackups(string? folder = null)
    {
        var dir = string.IsNullOrWhiteSpace(folder) ? _paths.Backups : folder!;
        if (!Directory.Exists(dir))
        {
            return Array.Empty<BackupInfo>();
        }

        var list = new List<BackupInfo>();
        foreach (var file in Directory.EnumerateFiles(dir, "*" + Extension))
        {
            var manifest = TryReadManifest(file);
            list.Add(manifest is not null
                ? ToInfo(file, manifest)
                : new BackupInfo(file, Path.GetFileName(file), File.GetLastWriteTimeUtc(file), null, "—", "—", "—",
                    SafeSize(file), Path.GetFileName(file).StartsWith(SafetyPrefix, StringComparison.OrdinalIgnoreCase)));
        }

        return list.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    // ==================== VALIDATE ====================

    public BackupValidation Validate(string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
        {
            return new BackupValidation(BackupValidationStatus.Invalid, "The backup file could not be found.", null, Array.Empty<string>());
        }

        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(backupFilePath);
        }
        catch (Exception)
        {
            return new BackupValidation(BackupValidationStatus.Invalid, "This file is not a readable Snooker Point backup.", null, Array.Empty<string>());
        }

        using (zip)
        {
            var included = zip.Entries.Select(e => e.FullName).ToList();

            var manifestEntry = zip.GetEntry(ManifestName);
            if (manifestEntry is null)
            {
                return new BackupValidation(BackupValidationStatus.Invalid, "The backup manifest is missing.", null, included);
            }

            BackupManifest? manifest;
            try
            {
                using var s = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<BackupManifest>(s, JsonOptions);
            }
            catch
            {
                manifest = null;
            }

            if (manifest is null)
            {
                return new BackupValidation(BackupValidationStatus.Invalid, "The backup manifest is unreadable.", null, included);
            }

            var info = ToInfo(backupFilePath, manifest);

            if (manifest.FormatVersion > FormatVersion || manifest.FormatVersion <= 0)
            {
                return new BackupValidation(BackupValidationStatus.UnsupportedVersion,
                    $"This backup was made by a different version (format {manifest.FormatVersion}) and cannot be restored by this app.", info, included);
            }

            // Expected files present?
            if (zip.GetEntry(DbEntryPath) is null)
            {
                return new BackupValidation(BackupValidationStatus.MissingFiles, "The backup is missing its database file.", info, included);
            }

            foreach (var f in manifest.Files)
            {
                if (zip.GetEntry(f.Path) is null)
                {
                    return new BackupValidation(BackupValidationStatus.MissingFiles, $"The backup is missing a file it listed: {f.Path}.", info, included);
                }
            }

            // Hashes match?
            foreach (var f in manifest.Files)
            {
                var entry = zip.GetEntry(f.Path)!;
                if (!string.Equals(HashStream(entry), f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new BackupValidation(BackupValidationStatus.ValidationFailed, $"A backup file failed its integrity check: {f.Path}.", info, included);
                }
            }

            // Database opens + integrity check.
            var integrity = CheckDatabaseIntegrity(zip.GetEntry(DbEntryPath)!);
            if (integrity is not null)
            {
                return new BackupValidation(BackupValidationStatus.ValidationFailed, integrity, info, included);
            }

            return new BackupValidation(BackupValidationStatus.Valid, "This backup is valid and ready to restore.", info, included);
        }
    }

    // ==================== RESTORE ====================

    public OperationResult RestoreBackup(string backupFilePath, string typedConfirmation, int actorUserId)
    {
        if (!string.Equals(typedConfirmation?.Trim(), RestoreConfirmationPhrase, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Failure($"To restore, type {RestoreConfirmationPhrase} to confirm that current data will be replaced.");
        }

        var validation = Validate(backupFilePath);
        if (!validation.IsValid)
        {
            return OperationResult.Failure($"This backup cannot be restored: {validation.Message}");
        }

        string dbPath;
        using (var db = _factory.CreateDbContext())
        {
            dbPath = db.Database.GetDbConnection().DataSource;
        }

        // 1) Always take a safety backup of the CURRENT data first.
        var safety = CreateBackupCore(_paths.Backups, "Safety backup before restore", actorUserId, SafetyPrefix, automatic: false);
        if (safety.Failed)
        {
            return OperationResult.Failure("A safety backup of your current data could not be made, so the restore was cancelled. Nothing was changed.");
        }

        var working = Path.Combine(Path.GetTempPath(), $"sprestore-{Guid.NewGuid():N}");
        var preRestoreDb = dbPath + ".prerestore";
        try
        {
            Directory.CreateDirectory(working);
            ZipFile.ExtractToDirectory(backupFilePath, working);

            var restoredDb = Path.Combine(working, "Db", "snookerpoint.db");
            if (!File.Exists(restoredDb))
            {
                return OperationResult.Failure("This backup cannot be restored: its database file is missing.");
            }

            // Release any pooled SQLite handles before swapping files.
            SqliteConnection.ClearAllPools();

            // Keep the current DB so we can roll back if the swap fails.
            File.Copy(dbPath, preRestoreDb, overwrite: true);

            try
            {
                File.Copy(restoredDb, dbPath, overwrite: true);
                SafeDelete(dbPath + "-wal");
                SafeDelete(dbPath + "-shm");

                ReplaceFolder(Path.Combine(working, "Images"), _paths.Images);
                ReplaceFolder(Path.Combine(working, "Receipts"), _paths.Receipts);
            }
            catch (Exception swapEx)
            {
                _logger.LogError(swapEx, "Restore swap failed; rolling the database back.");
                try
                {
                    File.Copy(preRestoreDb, dbPath, overwrite: true);
                    SafeDelete(dbPath + "-wal");
                    SafeDelete(dbPath + "-shm");
                }
                catch (Exception rbEx)
                {
                    _logger.LogError(rbEx, "Rollback after a failed restore also failed.");
                }

                return OperationResult.Failure("The restore failed while replacing data and your original data has been kept. Please try again.");
            }

            WriteAudit(AuditActions.BackupRestored, actorUserId, "Backup",
                $"Restored from {Path.GetFileName(backupFilePath)} (a safety backup was taken first).");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore from {File} failed.", backupFilePath);
            return OperationResult.Failure("The restore could not be completed. Your current data has been kept.");
        }
        finally
        {
            SafeDelete(preRestoreDb);
            SafeDeleteDir(working);
        }
    }

    // ==================== AUTOMATIC ====================

    public OperationResult<BackupInfo?> RunAutomaticBackupIfDue(int actorUserId)
    {
        try
        {
            ClubSettings? settings;
            using (var db = _factory.CreateDbContext())
            {
                settings = db.ClubSettings.AsNoTracking().FirstOrDefault();
            }

            if (settings is null || !settings.AutoBackupEnabled || !settings.AutoBackupDaily)
            {
                return OperationResult<BackupInfo?>.Success(null);
            }

            var lastLocalDate = settings.LastAutoBackupUtc?.ToLocalTime().Date;
            if (lastLocalDate == _clock.UtcNow.ToLocalTime().Date)
            {
                return OperationResult<BackupInfo?>.Success(null); // already done today
            }

            var folder = string.IsNullOrWhiteSpace(settings.BackupFolder) ? _paths.Backups : settings.BackupFolder!;
            var created = CreateBackup(folder, "Automatic daily backup", actorUserId, automatic: true);
            if (created.Failed)
            {
                return OperationResult<BackupInfo?>.Failure(
                    "Automatic backup did not run. Your data is safe; please check the backup folder and disk space.");
            }

            using (var db = _factory.CreateDbContext())
            {
                var row = db.ClubSettings.FirstOrDefault();
                if (row is not null)
                {
                    row.LastAutoBackupUtc = _clock.UtcNow;
                    db.SaveChanges();
                }
            }

            PruneRetention(folder, Math.Max(1, settings.AutoBackupRetention));
            return OperationResult<BackupInfo?>.Success(created.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic backup failed unexpectedly.");
            return OperationResult<BackupInfo?>.Failure("Automatic backup did not run this time. Your data is safe.");
        }
    }

    /// <summary>Deletes managed backups beyond the retention count. Only recognised managed files; never the last one.</summary>
    internal void PruneRetention(string folder, int retention)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            // Only automatic managed backups are pruned; safety and manual backups are kept.
            var managed = Directory.EnumerateFiles(folder, "*" + Extension)
                .Select(f => new FileInfo(f))
                .Where(f => f.Name.StartsWith(AutoPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            // Never delete the only valid backup, and only files we recognise.
            foreach (var file in managed.Skip(Math.Max(1, retention)))
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not prune old backup {File}.", file.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup retention pruning failed.");
        }
    }

    // ==================== HELPERS ====================

    private static void SnapshotDatabase(string sourceDbPath, string destPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString());
        source.Open();
        using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destPath }.ToString()))
        {
            dest.Open();
            source.BackupDatabase(dest); // online backup API: a consistent snapshot even under WAL
        }

        source.Close();
        SqliteConnection.ClearAllPools();
    }

    private static void AddFolder(List<(string Rel, string Abs)> files, string folder, string prefix)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var abs in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var rel = prefix + "/" + Path.GetRelativePath(folder, abs).Replace('\\', '/');
            files.Add((rel, abs));
        }
    }

    private static void WriteArchive(string destPath, List<(string Rel, string Abs)> files, BackupManifest manifest)
    {
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }

        using var zip = ZipFile.Open(destPath, ZipArchiveMode.Create);
        foreach (var (rel, abs) in files)
        {
            zip.CreateEntryFromFile(abs, rel, CompressionLevel.Optimal);
        }

        var manifestEntry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
        using var stream = manifestEntry.Open();
        JsonSerializer.Serialize(stream, manifest, JsonOptions);
    }

    private static void ReplaceFolder(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (var abs in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, abs);
            var target = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(abs, target, overwrite: true);
        }
    }

    private static string? CheckDatabaseIntegrity(ZipArchiveEntry dbEntry)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"spverify-{Guid.NewGuid():N}.db");
        try
        {
            dbEntry.ExtractToFile(temp, overwrite: true);
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Mode = SqliteOpenMode.ReadOnly }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar() as string;
            conn.Close();
            SqliteConnection.ClearAllPools();
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase) ? null : "The backup database failed its integrity check.";
        }
        catch (Exception)
        {
            return "The backup database could not be opened.";
        }
        finally
        {
            SafeDelete(temp);
        }
    }

    private BackupManifest? TryReadManifest(string backupFilePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(backupFilePath);
            var entry = zip.GetEntry(ManifestName);
            if (entry is null)
            {
                return null;
            }

            using var s = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(s, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static BackupInfo ToInfo(string filePath, BackupManifest manifest) =>
        new(filePath, Path.GetFileName(filePath), manifest.CreatedUtc, manifest.Description,
            manifest.ClubName, manifest.AppVersion, manifest.SchemaVersion, SafeSize(filePath), manifest.Automatic);

    private static string Hash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashStream(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static long SafeSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static void SafeDeleteDir(string? path)
    {
        try { if (path is not null && Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort */ }
    }

    private static string AppVersion => typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    private void WriteAudit(string action, int actorUserId, string entity, string details)
    {
        try
        {
            using var db = _factory.CreateDbContext();
            db.AuditEvents.Add(new AuditEvent
            {
                Utc = _clock.UtcNow,
                Action = action,
                ActorUserId = actorUserId <= 0 ? null : actorUserId,
                Entity = entity,
                Details = details,
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the backup audit event.");
        }
    }

    // ---- Manifest model ----

    internal sealed class BackupManifest
    {
        public int FormatVersion { get; set; }
        public string AppVersion { get; set; } = "";
        public string SchemaVersion { get; set; } = "";
        public string ClubName { get; set; } = "";
        public string? Description { get; set; }
        public bool Automatic { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        /// <summary>
        /// Machine-bound activation (the licence/trial state) is deliberately NOT included in this
        /// backup, so restoring it never clones activation to another computer.
        /// </summary>
        public bool MachineActivationExcluded { get; set; } = true;

        public List<BackupFileEntry> Files { get; set; } = new();
    }

    internal sealed class BackupFileEntry
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
    }
}
