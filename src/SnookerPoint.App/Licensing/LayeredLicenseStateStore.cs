using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// Composes a primary (per-user, DPAPI CurrentUser) and a secondary (machine-level, ProgramData
/// DPAPI LocalMachine) licence-state checkpoint into best-effort machine-level protection:
///
/// <list type="bullet">
/// <item>The trial start is the EARLIEST recorded across both copies, so switching Windows users
/// (the machine copy is shared) or deleting one copy never restarts the trial.</item>
/// <item>Writes fan out to both copies, healing a missing one.</item>
/// <item>If neither copy has a trial start but a copy exists yet is unreadable/tampered, the state
/// is reported CORRUPT (a licence-state warning) rather than treated as a fresh install.</item>
/// <item>The activated licence is read from either copy, so a valid signed licence stays the
/// highest authority.</item>
/// </list>
///
/// Honest limitation: a determined administrator who removes every protected copy on the machine
/// can still reset an offline trial; the online activation service (a later phase) closes this.
/// </summary>
public sealed class LayeredLicenseStateStore : ILicenseStateStore
{
    private readonly ILicenseStateStore _primary;
    private readonly ILicenseStateStore _secondary;

    public LayeredLicenseStateStore(ILicenseStateStore primary, ILicenseStateStore secondary)
    {
        _primary = primary;
        _secondary = secondary;
    }

    public LicenseState? Load(out bool corrupt)
    {
        var p = SafeLoad(_primary, out var pCorrupt);
        var s = SafeLoad(_secondary, out var sCorrupt);

        // Prefer any copy that carries a trial start; never let a single copy's absence reset it.
        DateTimeOffset? earliestStart = Earliest(p?.TrialStartUtc, s?.TrialStartUtc);
        if (earliestStart is not null)
        {
            corrupt = false;
            var lastSeen = Latest(p?.LastSeenUtc, s?.LastSeenUtc) ?? earliestStart.Value;
            var lastRun = Latest(p?.LastRunUtc, s?.LastRunUtc) ?? earliestStart.Value;
            return new LicenseState
            {
                Version = ProductInfo.StateVersion,
                TrialStartUtc = earliestStart,
                LastSeenUtc = lastSeen,
                LastRunUtc = lastRun,
            };
        }

        // No usable trial start anywhere. If a copy existed but was unreadable/tampered, warn
        // rather than silently treating this as a brand-new install.
        corrupt = pCorrupt || sCorrupt;
        return null;
    }

    public bool Save(LicenseState state)
    {
        var primaryOk = _primary.Save(state);
        // Machine copy is best-effort (may be read-only for a non-admin user); don't fail on it.
        try { _secondary.Save(state); } catch { /* best-effort heal */ }
        return primaryOk;
    }

    public string? LoadLicense()
    {
        return SafeLoadLicense(_primary) ?? SafeLoadLicense(_secondary);
    }

    public bool SaveLicense(string licenseText)
    {
        var primaryOk = _primary.SaveLicense(licenseText);
        try { _secondary.SaveLicense(licenseText); } catch { /* best-effort mirror */ }
        return primaryOk;
    }

    private static LicenseState? SafeLoad(ILicenseStateStore store, out bool corrupt)
    {
        try
        {
            return store.Load(out corrupt);
        }
        catch
        {
            corrupt = true;
            return null;
        }
    }

    private static string? SafeLoadLicense(ILicenseStateStore store)
    {
        try { return store.LoadLicense(); }
        catch { return null; }
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a <= b ? a : b);

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a >= b ? a : b);
}
