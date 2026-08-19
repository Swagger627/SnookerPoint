using SnookerPoint.Licensing;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>A controllable clock for the licensing engine.</summary>
public sealed class FakeLicenseClock : ILicenseClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A fixed machine fingerprint for tests.</summary>
public sealed class FixedFingerprintProvider : IMachineFingerprintProvider
{
    public MachineFingerprint Fingerprint { get; set; } = new(1, "MACHINE-A-HASH", "AAAA-BBBB-CCCC-DDDD");

    public MachineFingerprint GetFingerprint() => Fingerprint;
}

/// <summary>An in-memory licence state store; can simulate a corrupt trial-state file.</summary>
public sealed class InMemoryLicenseStateStore : ILicenseStateStore
{
    public LicenseState? State { get; set; }
    public string? License { get; set; }
    public bool Corrupt { get; set; }

    public LicenseState? Load(out bool corrupt)
    {
        corrupt = Corrupt;
        return Corrupt ? null : State;
    }

    public bool Save(LicenseState state)
    {
        State = state;
        return true;
    }

    public string? LoadLicense() => License;

    public bool SaveLicense(string licenseText)
    {
        License = licenseText;
        return true;
    }
}
