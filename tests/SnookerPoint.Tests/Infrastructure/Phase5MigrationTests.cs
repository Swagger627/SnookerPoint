using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

public class Phase5MigrationTests
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
    public void UpgradingFromPhase4_ToPhase5_AddsBookingsTable_AndKeepsExisting()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-mig5-{Guid.NewGuid():N}.db");
        try
        {
            using (var phase4 = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                phase4.GetService<IMigrator>().Migrate("Phase4SalesAndPayments");
                Assert.True(TableExists(phase4, "Sales"));
                Assert.False(TableExists(phase4, "Bookings"));
            }

            using (var latest = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                latest.Database.Migrate();
                Assert.True(TableExists(latest, "Users"));           // Phase 1
                Assert.True(TableExists(latest, "TableSessions"));   // Phase 2 preserved
                Assert.True(TableExists(latest, "Products"));        // Phase 3 preserved
                Assert.True(TableExists(latest, "Sales"));           // Phase 4 preserved
                Assert.True(TableExists(latest, "Bookings"));        // Phase 5
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
