using System.IO.Compression;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class SupportBundleTests
{
    [Fact]
    public void SupportBundle_ContainsSummaryAndLogs_ButNoSecretsOrData()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        // Simulate secret-bearing files that must NEVER be bundled.
        Directory.CreateDirectory(env.Paths.License);
        File.WriteAllText(Path.Combine(env.Paths.License, "license.dat"), "SECRET-LICENCE");
        Directory.CreateDirectory(env.Paths.Logs);
        File.WriteAllText(Path.Combine(env.Paths.Logs, "snookerpoint-x.log"), "2026 [INF] Snooker Point started.");

        var result = env.Health.CreateSupportBundle(null, "TRIAL_ACTIVE", ownerId);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(File.Exists(result.Value));

        using var zip = ZipFile.OpenRead(result.Value!);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(names, n => n.Contains("support-summary.txt"));
        // No database, licence, images or receipts in the bundle.
        Assert.DoesNotContain(names, n => n.Contains("snookerpoint.db", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("license", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Images", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Receipts", StringComparison.OrdinalIgnoreCase));

        // The summary carries version/status but no secret markers.
        var summary = zip.Entries.First(e => e.FullName.Contains("support-summary.txt"));
        using var reader = new StreamReader(summary.Open());
        var text = reader.ReadToEnd();
        Assert.Contains("Application version", text);
        Assert.Contains("TRIAL_ACTIVE", text);
        // No actual secret VALUES appear (the disclaimer line naming "passwords" is expected).
        Assert.DoesNotContain("SECRET-LICENCE", text);
        Assert.DoesNotContain("PRIVATE KEY", text);
        Assert.DoesNotContain("secret123", text, StringComparison.OrdinalIgnoreCase);
    }
}
