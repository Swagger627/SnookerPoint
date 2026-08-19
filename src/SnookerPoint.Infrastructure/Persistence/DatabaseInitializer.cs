using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;

namespace SnookerPoint.Infrastructure.Persistence;

/// <summary>Raised when a database upgrade (migration) fails; the original database is preserved/restored.</summary>
public sealed class DatabaseUpgradeException : Exception
{
    public DatabaseUpgradeException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Brings the database up to date at startup. On an upgrade (pending migrations over an existing
/// database) it takes a verified pre-upgrade safety copy first, applies migrations, runs an
/// integrity check, and only continues if successful; if anything fails it restores the original
/// database and reports a friendly recovery error rather than launching against a half-upgraded DB.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IClock _clock;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbContextFactory<SnookerPointDbContext> factory,
        IClock clock,
        ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _clock = clock;
        _logger = logger;
    }

    public void Initialize()
    {
        using var db = _factory.CreateDbContext();
        var dbPath = db.Database.GetDbConnection().DataSource;

        SafeApplyMigrations(db, dbPath, () => db.Database.Migrate(), _logger);

        // WAL improves resilience to crashes and power loss (§19b).
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        if (!db.AppInfo.Any())
        {
            db.AppInfo.Add(new AppInfo
            {
                SchemaVersion = "1",
                AppVersion = "0.1.0",
                InstalledUtc = _clock.UtcNow.ToString("O"),
            });
            db.SaveChanges();
            _logger.LogInformation("Initialised new database with AppInfo record.");
        }

        // Ensure the single billing-settings row exists (Phase 2 defaults).
        if (!db.BillingSettings.Any())
        {
            db.BillingSettings.Add(new Domain.Entities.BillingSettings
            {
                Id = 1,
                Method = Domain.Enums.BillingMethod.Exact,
                RoundingIncrementMinutes = 5,
                MinimumBillableMinutes = 0,
                GracePeriodMinutes = 0,
                UpdatedUtc = _clock.UtcNow,
            });
            db.SaveChanges();
        }

        _logger.LogInformation("Database ready.");
    }

    /// <summary>
    /// Applies migrations with pre-upgrade safety-backup + rollback semantics. Exposed for testing
    /// (the <paramref name="migrate"/> delegate is normally <c>db.Database.Migrate</c>).
    /// </summary>
    public static void SafeApplyMigrations(SnookerPointDbContext db, string dbPath, Action migrate, ILogger logger)
    {
        var pending = db.Database.GetPendingMigrations().ToList();
        var applied = db.Database.GetAppliedMigrations().ToList();
        var isUpgrade = pending.Count > 0 && applied.Count > 0 && File.Exists(dbPath);

        string? safetyCopy = null;
        if (isUpgrade)
        {
            logger.LogInformation("Upgrade detected ({Count} pending migration(s)); taking a pre-upgrade safety backup.", pending.Count);
            safetyCopy = CreatePreUpgradeCopy(db, dbPath);
        }

        try
        {
            migrate();

            var integrity = CheckIntegrity(dbPath);
            if (integrity is not null)
            {
                throw new DatabaseUpgradeException($"The database failed its integrity check after upgrading: {integrity}.");
            }

            // Success: keep one pre-upgrade copy for support, but it is safe to remove older temp files.
            if (safetyCopy is not null)
            {
                logger.LogInformation("Upgrade completed; pre-upgrade backup retained at {Path}.", safetyCopy);
            }
        }
        catch (Exception ex)
        {
            if (safetyCopy is not null && File.Exists(safetyCopy))
            {
                logger.LogError(ex, "Upgrade failed; restoring the pre-upgrade database.");
                RestoreCopy(safetyCopy, dbPath);
                throw new DatabaseUpgradeException(
                    "Snooker Point could not finish updating its database. Your original data has been kept and restored. " +
                    "Please reopen the app; if it keeps happening, use the backups folder or contact support.", ex);
            }

            if (ex is DatabaseUpgradeException)
            {
                throw;
            }

            throw new DatabaseUpgradeException("Snooker Point could not open its database.", ex);
        }
    }

    private static string CreatePreUpgradeCopy(SnookerPointDbContext db, string dbPath)
    {
        // Flush WAL into the main file, release handles, then copy the single .db file.
        try { db.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { /* best-effort */ }
        db.Database.GetDbConnection().Close();
        SqliteConnection.ClearAllPools();

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var copy = dbPath + $".preupgrade-{stamp}.bak";
        File.Copy(dbPath, copy, overwrite: true);
        return copy;
    }

    private static void RestoreCopy(string safetyCopy, string dbPath)
    {
        SqliteConnection.ClearAllPools();
        File.Copy(safetyCopy, dbPath, overwrite: true);
        SafeDelete(dbPath + "-wal");
        SafeDelete(dbPath + "-shm");
    }

    private static string? CheckIntegrity(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar() as string;
            conn.Close();
            SqliteConnection.ClearAllPools();
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase) ? null : result ?? "unknown";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
