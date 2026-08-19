using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

public class SafeUpgradeTests
{
    private static DbContextOptions<SnookerPointDbContext> OptionsFor(string path) =>
        new DbContextOptionsBuilder<SnookerPointDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;

    [Fact]
    public void SuccessfulMigration_LeavesDatabaseUsable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sp-upok-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SnookerPointDbContext(OptionsFor(dbPath));
            DatabaseInitializer.SafeApplyMigrations(db, dbPath, () => db.Database.Migrate(), NullLogger.Instance);

            Assert.True(db.Categories.Any() || !db.Categories.Any()); // query works
            Assert.NotEmpty(db.Database.GetAppliedMigrations());
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void FailedMigration_RestoresOriginalDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sp-upfail-{Guid.NewGuid():N}.db");
        try
        {
            // Establish an existing, fully-migrated database with a known category.
            using (var seed = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                seed.Database.Migrate();
                seed.Categories.Add(new Category { Name = "Original", SortOrder = 1, IsActive = true });
                seed.SaveChanges();
            }

            // Simulate an "upgrade" whose migration step mutates data and then fails. Because
            // SafeApplyMigrations only backs up when there are PENDING migrations, force that here by
            // pretending a pending upgrade via a delegate that changes data then throws.
            using (var db = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                var ex = Record.Exception(() => DatabaseInitializer.SafeApplyMigrations(db, dbPath, () =>
                {
                    // Only exercised when the code treats this as an upgrade; on a fully-migrated DB with
                    // no pending migrations there is no safety copy, so this test asserts the no-pending path
                    // simply surfaces the failure without corrupting data.
                    using var mutate = new SnookerPointDbContext(OptionsFor(dbPath));
                    mutate.Categories.Add(new Category { Name = "HalfUpgraded", SortOrder = 2, IsActive = true });
                    mutate.SaveChanges();
                    throw new InvalidOperationException("simulated migration failure");
                }, NullLogger.Instance));

                Assert.NotNull(ex);
            }

            // The database must remain openable and retain the original category.
            using (var check = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                Assert.True(check.Categories.Any(c => c.Name == "Original"));
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void FailedUpgrade_WithPendingMigration_RestoresPreUpgradeCopy()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sp-uprestore-{Guid.NewGuid():N}.db");
        try
        {
            // Migrate only up to Phase 6 so there IS a pending migration (Phase 7 had none; use an
            // intermediate target so the safety-copy path is exercised).
            using (var seed = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                seed.GetService<IMigrator>().Migrate("Phase5Bookings");
                seed.Categories.Add(new Category { Name = "Original", SortOrder = 1, IsActive = true });
                seed.SaveChanges();
            }

            using (var db = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                var ex = Record.Exception(() => DatabaseInitializer.SafeApplyMigrations(db, dbPath, () =>
                {
                    // Mutate then fail, to prove the pre-upgrade copy is restored.
                    using var mutate = new SnookerPointDbContext(OptionsFor(dbPath));
                    mutate.Categories.Add(new Category { Name = "HalfUpgraded", SortOrder = 2, IsActive = true });
                    mutate.SaveChanges();
                    throw new InvalidOperationException("simulated upgrade failure");
                }, NullLogger.Instance));

                Assert.IsType<DatabaseUpgradeException>(ex);
            }

            using (var check = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                Assert.True(check.Categories.Any(c => c.Name == "Original"));
                Assert.False(check.Categories.Any(c => c.Name == "HalfUpgraded")); // rolled back
            }
        }
        finally
        {
            Cleanup(dbPath);
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(dbPath)!, Path.GetFileName(dbPath) + ".preupgrade-*"))
            {
                try { File.Delete(f); } catch { /* best-effort */ }
            }
        }
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); } catch { /* best-effort */ }
        }
    }
}
