using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

public class Phase6MigrationTests
{
    private static DbContextOptions<SnookerPointDbContext> OptionsFor(string path) =>
        new DbContextOptionsBuilder<SnookerPointDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;

    private static bool ColumnExists(SnookerPointDbContext db, string table, string column)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name";
        var p = command.CreateParameter();
        p.ParameterName = "$name";
        p.Value = column;
        command.Parameters.Add(p);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static bool TableExists(SnookerPointDbContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        var p = command.CreateParameter();
        p.ParameterName = "$name";
        p.Value = tableName;
        command.Parameters.Add(p);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    [Fact]
    public void UpgradingFromPhase5_ToPhase6_AddsSettingsColumns_AndKeepsExisting()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-mig6-{Guid.NewGuid():N}.db");
        try
        {
            using (var phase5 = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                phase5.GetService<IMigrator>().Migrate("Phase5Bookings");
                Assert.True(TableExists(phase5, "Bookings"));
                Assert.False(ColumnExists(phase5, "ClubSettings", "TaxEnabled"));
            }

            using (var latest = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                latest.Database.Migrate();
                Assert.True(TableExists(latest, "Users"));      // Phase 1 preserved
                Assert.True(TableExists(latest, "Sales"));      // Phase 4 preserved
                Assert.True(TableExists(latest, "Bookings"));   // Phase 5 preserved
                Assert.True(ColumnExists(latest, "ClubSettings", "TaxEnabled"));
                Assert.True(ColumnExists(latest, "ClubSettings", "TaxPercent"));
                Assert.True(ColumnExists(latest, "ClubSettings", "ServiceChargeEnabled"));
                Assert.True(ColumnExists(latest, "ClubSettings", "AutoBackupEnabled"));
                Assert.True(ColumnExists(latest, "ClubSettings", "AutoBackupRetention"));
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
}
