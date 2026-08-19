using Microsoft.EntityFrameworkCore;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>Creates contexts over a fixed set of options (a shared SQLite file).</summary>
public sealed class TestDbContextFactory : IDbContextFactory<SnookerPointDbContext>
{
    private readonly DbContextOptions<SnookerPointDbContext> _options;

    public TestDbContextFactory(string databasePath)
    {
        // Pooling=False so the temporary SQLite file is released for cleanup.
        _options = new DbContextOptionsBuilder<SnookerPointDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
    }

    public SnookerPointDbContext CreateDbContext() => new(_options);
}
