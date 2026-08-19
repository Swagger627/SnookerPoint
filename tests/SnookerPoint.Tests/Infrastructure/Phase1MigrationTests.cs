using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

public class Phase1MigrationTests
{
    private static DbContextOptions<SnookerPointDbContext> OptionsFor(string path) =>
        new DbContextOptionsBuilder<SnookerPointDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;

    private static bool TableExists(SnookerPointDbContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    [Fact]
    public void UpgradingFromPhase0ToPhase1_AddsStaffAndShiftTables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-mig-{Guid.NewGuid():N}.db");
        try
        {
            // Simulate an existing Phase 0 database: migrate only up to InitialCreate.
            using (var phase0 = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                phase0.GetService<IMigrator>().Migrate("InitialCreate");
                Assert.True(TableExists(phase0, "AppInfo"));
                Assert.False(TableExists(phase0, "Users"));
                Assert.False(TableExists(phase0, "Shifts"));
            }

            // Upgrade to the latest (Phase 1) schema.
            using (var phase1 = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                phase1.Database.Migrate();
                Assert.True(TableExists(phase1, "AppInfo"));
                Assert.True(TableExists(phase1, "Users"));
                Assert.True(TableExists(phase1, "Shifts"));
                Assert.True(TableExists(phase1, "CashMovements"));
                Assert.True(TableExists(phase1, "AuditEvents"));
                Assert.True(TableExists(phase1, "ClubSettings"));
                Assert.True(TableExists(phase1, "PoolTables"));
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var p = dbPath + suffix;
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }
        }
    }

    [Fact]
    public void Username_UniqueIndex_RejectsDuplicates()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-mig-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SnookerPointDbContext(OptionsFor(dbPath));
            db.Database.Migrate();

            db.Users.Add(new SnookerPoint.Domain.Entities.User
            {
                DisplayName = "One", Username = "same", Role = UserRole.Owner, PasswordHash = "h",
            });
            db.SaveChanges();

            db.Users.Add(new SnookerPoint.Domain.Entities.User
            {
                DisplayName = "Two", Username = "same", Role = UserRole.Cashier, PasswordHash = "h",
            });

            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var p = dbPath + suffix;
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }
        }
    }
}
