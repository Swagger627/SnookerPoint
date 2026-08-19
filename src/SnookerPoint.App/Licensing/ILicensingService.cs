using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// App-facing licensing service: evaluates trial/licence status for startup routing and the UI,
/// starts the trial after setup, and performs offline activation. Writes safe audit summaries for
/// licensing events (never keys, raw machine identifiers or pasted licence text).
/// </summary>
public interface ILicensingService
{
    /// <summary>Evaluates the current status and advances the trusted last-seen time.</summary>
    LicenseEvaluation Evaluate();

    /// <summary>Starts the 72-hour trial once, after setup completes. No-op if already trialing/licensed.</summary>
    bool StartTrialIfNeeded();

    /// <summary>Verifies and (on success) stores an imported/pasted licence for this machine.</summary>
    ActivationOutcome Activate(string? licenseText);

    /// <summary>This machine's fingerprint (hash + copyable installation code).</summary>
    MachineFingerprint GetFingerprint();
}
