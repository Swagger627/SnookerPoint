namespace SnookerPoint.Infrastructure.Persistence;

/// <summary>
/// Foundational metadata row describing the installed database. This is NOT a
/// business entity — it exists only so the initial migration pipeline creates and
/// round-trips a real table. Business tables (orders, products, sales, sessions,
/// etc.) are introduced in later phases.
/// </summary>
public sealed class AppInfo
{
    public int Id { get; set; }

    /// <summary>Logical schema version, bumped as migrations are added.</summary>
    public string SchemaVersion { get; set; } = "0";

    /// <summary>Application version that first created this database (UTC record).</summary>
    public string AppVersion { get; set; } = "0.1.0";

    /// <summary>ISO-8601 UTC timestamp when the database was initialised.</summary>
    public string InstalledUtc { get; set; } = string.Empty;
}
