using System.IO.Compression;
using System.Text;
using SnookerPoint.Application.Backups;
using SnookerPoint.Application.Settings;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class BackupServiceTests
{
    // ---------- Create ----------

    [Fact]
    public void CreateBackup_ProducesArchive_WithRequiredFiles()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.Backups.CreateBackup(null, "Nightly", ownerId);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(File.Exists(result.Value!.FilePath));

        using var zip = ZipFile.OpenRead(result.Value.FilePath);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("Db/snookerpoint.db"));

        Assert.Single(env.Backups.ListBackups());
    }

    [Fact]
    public void CreatedBackup_ValidatesAsValid()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var backup = env.Backups.CreateBackup(null, null, ownerId).Value!;

        var validation = env.Backups.Validate(backup.FilePath);
        Assert.Equal(BackupValidationStatus.Valid, validation.Status);
        Assert.True(validation.IsValid);
    }

    // ---------- Validation negatives ----------

    [Fact]
    public void Validate_RejectsNonBackupFile()
    {
        using var env = new Phase1Environment();
        var path = Path.Combine(env.Paths.Backups, "not-a-backup.spbak");
        Directory.CreateDirectory(env.Paths.Backups);
        File.WriteAllText(path, "this is not a zip");

        Assert.Equal(BackupValidationStatus.Invalid, env.Backups.Validate(path).Status);
    }

    [Fact]
    public void Validate_RejectsUnsupportedVersion()
    {
        using var env = new Phase1Environment();
        var path = CraftBackup(env.Paths.Backups, "SnookerPoint-Backup-bad1.spbak",
            manifestJson: "{\"FormatVersion\":99,\"AppVersion\":\"1\",\"SchemaVersion\":\"x\",\"ClubName\":\"T\",\"Automatic\":false,\"CreatedUtc\":\"2026-01-01T00:00:00+00:00\",\"Files\":[]}",
            includeDb: true);

        Assert.Equal(BackupValidationStatus.UnsupportedVersion, env.Backups.Validate(path).Status);
    }

    [Fact]
    public void Validate_RejectsMissingDatabase()
    {
        using var env = new Phase1Environment();
        var path = CraftBackup(env.Paths.Backups, "SnookerPoint-Backup-bad2.spbak",
            manifestJson: "{\"FormatVersion\":1,\"AppVersion\":\"1\",\"SchemaVersion\":\"x\",\"ClubName\":\"T\",\"Automatic\":false,\"CreatedUtc\":\"2026-01-01T00:00:00+00:00\",\"Files\":[]}",
            includeDb: false);

        Assert.Equal(BackupValidationStatus.MissingFiles, env.Backups.Validate(path).Status);
    }

    // ---------- Restore ----------

    [Fact]
    public void Restore_ReplacesData_WithBackupContents()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        env.SeedProduct(ownerId, shiftId, "ORIGINAL", 60);

        var backup = env.Backups.CreateBackup(null, null, ownerId).Value!;

        // Add data AFTER the backup.
        env.SeedProduct(ownerId, shiftId, "AFTER", 99);
        Assert.Equal(2, ProductCount(env));

        var restore = env.Backups.RestoreBackup(backup.FilePath, "RESTORE", ownerId);
        Assert.True(restore.Succeeded, restore.ErrorMessage);

        // The post-backup product is gone; the original remains.
        Assert.Equal(1, ProductCount(env));
        Assert.True(HasProductSku(env, "ORIGINAL"));
        Assert.False(HasProductSku(env, "AFTER"));
    }

    [Fact]
    public void Restore_TakesASafetyBackupFirst()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var backup = env.Backups.CreateBackup(null, null, ownerId).Value!;
        env.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(env.Backups.RestoreBackup(backup.FilePath, "RESTORE", ownerId).Succeeded);

        Assert.Contains(env.Backups.ListBackups(), b => b.FileName.Contains("SAFETY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Restore_WithWrongConfirmation_FailsAndKeepsData()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        env.SeedProduct(ownerId, shiftId, "ORIGINAL", 60);
        var backup = env.Backups.CreateBackup(null, null, ownerId).Value!;
        env.SeedProduct(ownerId, shiftId, "AFTER", 99);

        var restore = env.Backups.RestoreBackup(backup.FilePath, "nope", ownerId);
        Assert.True(restore.Failed);
        Assert.Equal(2, ProductCount(env)); // nothing changed
    }

    [Fact]
    public void Restore_OfInvalidBackup_FailsAndKeepsData()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        env.SeedProduct(ownerId, shiftId, "ORIGINAL", 60);
        var bad = Path.Combine(env.Paths.Backups, "SnookerPoint-Backup-invalid.spbak");
        Directory.CreateDirectory(env.Paths.Backups);
        File.WriteAllText(bad, "garbage");

        var restore = env.Backups.RestoreBackup(bad, "RESTORE", ownerId);
        Assert.True(restore.Failed);
        Assert.Equal(1, ProductCount(env));
        Assert.True(HasProductSku(env, "ORIGINAL"));
    }

    // ---------- Automatic + retention ----------

    [Fact]
    public void AutomaticBackup_WhenDisabled_DoesNothing_AndDoesNotThrow()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.Backups.RunAutomaticBackupIfDue(ownerId);
        Assert.True(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Empty(env.Backups.ListBackups());
    }

    [Fact]
    public void CreateBackup_ToInvalidFolder_FailsGracefully()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        // A path containing characters that are invalid on Windows.
        var result = env.Backups.CreateBackup("Z:\\<>invalid?\\path", null, ownerId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Retention_PrunesOnlyAutomaticBackups_KeepingManualAndForeignFiles()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        // A manual backup and a foreign file that must never be deleted.
        env.Backups.CreateBackup(null, "manual", ownerId);
        var foreign = Path.Combine(env.Paths.Backups, "keep-me.txt");
        File.WriteAllText(foreign, "not a backup");

        // Three automatic backups (advance the clock for distinct file names).
        for (var i = 0; i < 3; i++)
        {
            env.Clock.Advance(TimeSpan.FromSeconds(2));
            env.Backups.CreateBackup(null, "auto", ownerId, automatic: true);
        }

        // Enable retention = 2 and run the due automatic backup (creates a 4th, prunes to 2).
        Assert.True(env.OperationalSettings.UpdateBackupSettings(new BackupSettingsInput(true, true, false, 2, null), ownerId).Succeeded);
        env.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True(env.Backups.RunAutomaticBackupIfDue(ownerId).Succeeded);

        var autos = env.Backups.ListBackups().Count(b => b.Automatic);
        Assert.Equal(2, autos);
        Assert.True(File.Exists(foreign));
        Assert.Contains(env.Backups.ListBackups(), b => b.FileName.Contains("AUTO", StringComparison.OrdinalIgnoreCase) == false && !b.Automatic);
    }

    // ---------- Helpers ----------

    private static int ProductCount(Phase1Environment env)
    {
        using var db = env.NewContext();
        return db.Products.Count();
    }

    private static bool HasProductSku(Phase1Environment env, string sku)
    {
        using var db = env.NewContext();
        return db.Products.Any(p => p.Sku == sku);
    }

    private static string CraftBackup(string folder, string fileName, string manifestJson, bool includeDb)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifest = zip.CreateEntry("manifest.json");
        using (var s = manifest.Open())
        {
            var bytes = Encoding.UTF8.GetBytes(manifestJson);
            s.Write(bytes, 0, bytes.Length);
        }

        if (includeDb)
        {
            var db = zip.CreateEntry("Db/snookerpoint.db");
            using var s = db.Open();
            var bytes = Encoding.UTF8.GetBytes("not a real db");
            s.Write(bytes, 0, bytes.Length);
        }

        return path;
    }
}
