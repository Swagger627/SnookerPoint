using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

public class Phase2MigrationTests
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
    public void UpgradingFromPhase0_ToPhase2_AddsSessionTables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-mig2-{Guid.NewGuid():N}.db");
        try
        {
            using (var phase0 = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                phase0.GetService<IMigrator>().Migrate("InitialCreate");
                Assert.False(TableExists(phase0, "TableSessions"));
            }

            using (var latest = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                latest.Database.Migrate();
                Assert.True(TableExists(latest, "AppInfo"));       // Phase 0
                Assert.True(TableExists(latest, "Users"));         // Phase 1
                Assert.True(TableExists(latest, "Shifts"));        // Phase 1
                Assert.True(TableExists(latest, "BillingSettings"));   // Phase 2
                Assert.True(TableExists(latest, "TableSessions"));     // Phase 2
                Assert.True(TableExists(latest, "SessionSegments"));   // Phase 2
                Assert.True(TableExists(latest, "SessionPauses"));     // Phase 2
                Assert.True(TableExists(latest, "SessionAdjustments"));// Phase 2
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
