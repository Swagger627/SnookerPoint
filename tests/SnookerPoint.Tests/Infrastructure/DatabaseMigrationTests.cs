using Microsoft.EntityFrameworkCore;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

/// <summary>
/// Verifies the Phase 0 migration pipeline: a brand-new SQLite database can be
/// created purely by applying migrations, and the foundational table round-trips.
/// </summary>
public class DatabaseMigrationTests
{
    private static DbContextOptions<SnookerPointDbContext> OptionsFor(string dbPath) =>
        new DbContextOptionsBuilder<SnookerPointDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

    [Fact]
    public void Migrate_CreatesDatabase_AndAppInfoRoundTrips()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-test-{Guid.NewGuid():N}.db");

        try
        {
            using (var db = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                db.Database.Migrate();

                db.AppInfo.Add(new AppInfo
                {
                    SchemaVersion = "1",
                    AppVersion = "0.1.0",
                    InstalledUtc = DateTimeOffset.UtcNow.ToString("O"),
                });
                db.SaveChanges();
            }

            using (var db = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                var row = Assert.Single(db.AppInfo);
                Assert.Equal("1", row.SchemaVersion);
                Assert.Equal("0.1.0", row.AppVersion);
                Assert.False(string.IsNullOrWhiteSpace(row.InstalledUtc));
            }
        }
        finally
        {
            SafeDelete(dbPath);
            SafeDelete(dbPath + "-wal");
            SafeDelete(dbPath + "-shm");
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the temp database file.
        }
    }
}
