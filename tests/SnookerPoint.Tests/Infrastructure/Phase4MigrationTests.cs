using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.Infrastructure;

public class Phase4MigrationTests
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
    public void UpgradingFromPhase3_ToPhase4_AddsSalesTables_AndKeepsExisting()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-mig4-{Guid.NewGuid():N}.db");
        try
        {
            using (var phase3 = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                phase3.GetService<IMigrator>().Migrate("Phase3ProductsAndInventory");
                Assert.True(TableExists(phase3, "Products"));
                Assert.False(TableExists(phase3, "Sales"));
            }

            using (var latest = new SnookerPointDbContext(OptionsFor(dbPath)))
            {
                latest.Database.Migrate();
                Assert.True(TableExists(latest, "Users"));           // Phase 1
                Assert.True(TableExists(latest, "TableSessions"));   // Phase 2 preserved
                Assert.True(TableExists(latest, "Products"));        // Phase 3 preserved
                Assert.True(TableExists(latest, "Sales"));           // Phase 4
                Assert.True(TableExists(latest, "SaleLines"));       // Phase 4
                Assert.True(TableExists(latest, "SalePayments"));    // Phase 4
                Assert.True(TableExists(latest, "PaymentMethods"));  // Phase 4
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
