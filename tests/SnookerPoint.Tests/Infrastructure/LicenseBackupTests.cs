using System.IO.Compression;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class LicenseBackupTests
{
    [Fact]
    public void BusinessBackup_ExcludesMachineActivationState_AndSaysSoInManifest()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        // Simulate machine-bound activation state on disk (in the managed License folder).
        Directory.CreateDirectory(env.Paths.License);
        File.WriteAllText(Path.Combine(env.Paths.License, "state.dat"), "protected-trial-state");
        File.WriteAllText(Path.Combine(env.Paths.License, "license.dat"), "protected-licence");

        var backup = env.Backups.CreateBackup(null, null, ownerId).Value!;

        using var zip = ZipFile.OpenRead(backup.FilePath);
        // No licence/state files are ever included in a business backup.
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("License", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("state.dat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("license.dat", StringComparison.OrdinalIgnoreCase));

        // The manifest states that machine activation is excluded.
        using var manifest = zip.GetEntry("manifest.json")!.Open();
        using var reader = new StreamReader(manifest);
        var json = reader.ReadToEnd();
        Assert.Contains("MachineActivationExcluded", json);
    }
}
