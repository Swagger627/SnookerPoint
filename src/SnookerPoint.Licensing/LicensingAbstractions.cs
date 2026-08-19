namespace SnookerPoint.Licensing;

/// <summary>A clock the licensing engine reads UTC from (so tests can control time).</summary>
public interface ILicenseClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Provides this machine's fingerprint (hash + copyable installation code).</summary>
public interface IMachineFingerprintProvider
{
    MachineFingerprint GetFingerprint();
}

/// <summary>
/// Protected local licensing state, held outside the business database. It is never included in
/// ordinary business backups, so a backup can't clone activation to another machine.
/// </summary>
public sealed record LicenseState
{
    public int Version { get; init; } = ProductInfo.StateVersion;

    /// <summary>The authoritative trial start (UTC). Set once, after setup completes.</summary>
    public DateTimeOffset? TrialStartUtc { get; init; }

    /// <summary>The latest trusted observed UTC (monotonic — never goes backwards).</summary>
    public DateTimeOffset LastSeenUtc { get; init; }

    /// <summary>The UTC of the most recent application run.</summary>
    public DateTimeOffset LastRunUtc { get; init; }
}

/// <summary>
/// Reads and writes the protected local state. The activated licence is stored separately from
/// the trial/rollback state, so a damaged trial-state file never loses a valid licence.
/// Implementations should protect the files against casual editing (e.g. Windows DPAPI), but
/// trust must still come from the licence signature.
/// </summary>
public interface ILicenseStateStore
{
    /// <summary>Loads the trial state, or null when none exists. Sets <paramref name="corrupt"/> when a file exists but is unreadable/tampered.</summary>
    LicenseState? Load(out bool corrupt);

    /// <summary>Persists the trial state. Returns false on failure (never throws).</summary>
    bool Save(LicenseState state);

    /// <summary>Loads the activated licence text, or null when none is stored/readable.</summary>
    string? LoadLicense();

    /// <summary>Persists the activated licence text. Returns false on failure (never throws).</summary>
    bool SaveLicense(string licenseText);
}

/// <summary>The full result of evaluating licensing at a point in time.</summary>
public sealed record LicenseEvaluation(
    LicenseStatus Status,
    MachineFingerprint Machine,
    string FriendlyRemaining,
    TimeSpan? Remaining,
    DateTimeOffset? TrialStartUtc,
    DateTimeOffset? TrialExpiryUtc,
    LicensePayload? License,
    bool RollbackDetected,
    string Code)
{
    /// <summary>Whether ordinary operational actions are allowed (blocked once the trial has expired).</summary>
    public bool OperationsAllowed => Status is LicenseStatus.Active or LicenseStatus.ExpiringSoon or LicenseStatus.Licensed;

    public bool IsLicensed => Status == LicenseStatus.Licensed;
    public bool IsTrial => Status is LicenseStatus.Active or LicenseStatus.ExpiringSoon;
}

/// <summary>The outcome of an activation attempt (never contains crypto exceptions or secrets).</summary>
public sealed record ActivationOutcome(bool Success, LicenseStatus Status, string Code, string Message);
