using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef</c>) to construct a
/// <see cref="SnookerPointDbContext"/> when adding or applying migrations, without
/// booting the full application host.
/// </summary>
public sealed class SnookerPointDbContextFactory : IDesignTimeDbContextFactory<SnookerPointDbContext>
{
    public SnookerPointDbContext CreateDbContext(string[] args)
    {
        var paths = new AppDataPaths();
        paths.EnsureLiveDirectories();

        var options = new DbContextOptionsBuilder<SnookerPointDbContext>()
            .UseSqlite($"Data Source={paths.LiveDatabaseFile}")
            .Options;

        return new SnookerPointDbContext(options);
    }
}
