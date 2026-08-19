using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// A do-nothing state store used as the machine-level checkpoint when ProgramData is not writable
/// (e.g. a non-admin user with no installer-provisioned folder). It never reports corruption, so
/// the app degrades cleanly to per-user protection only.
/// </summary>
public sealed class NoOpLicenseStateStore : ILicenseStateStore
{
    public LicenseState? Load(out bool corrupt)
    {
        corrupt = false;
        return null;
    }

    public bool Save(LicenseState state) => false;

    public string? LoadLicense() => null;

    public bool SaveLicense(string licenseText) => false;
}
