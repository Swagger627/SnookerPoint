using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

public class Phase3MigrationTests
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
    public void UpgradingFromPhase2_ToPhase3_AddsCatalogueTables_AndKeepsExisting()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-mig3-{Guid.NewGuid():N}.db");
        try
        {
            // Bring a database up to the Phase 2 schema only.
            using (var phase2 = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                phase2.GetService<IMigrator>().Migrate("Phase2xBillingTypeAndAccountSecurity");
                Assert.True(TableExists(phase2, "TableSessions"));
                Assert.False(TableExists(phase2, "Products"));
            }

            // Applying the remaining migration upgrades cleanly to Phase 3.
            using (var latest = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                latest.Database.Migrate();
                Assert.True(TableExists(latest, "AppInfo"));         // Phase 0
                Assert.True(TableExists(latest, "Users"));           // Phase 1
                Assert.True(TableExists(latest, "TableSessions"));   // Phase 2 preserved
                Assert.True(TableExists(latest, "Categories"));      // Phase 3
                Assert.True(TableExists(latest, "Products"));        // Phase 3
                Assert.True(TableExists(latest, "StockMovements"));  // Phase 3
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
