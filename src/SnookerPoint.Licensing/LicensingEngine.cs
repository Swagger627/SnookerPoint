using System.Security.Cryptography;

namespace SnookerPoint.Licensing;

/// <summary>
/// The offline licensing core: derives the current status (trial or licensed), starts the trial
/// exactly once, verifies and binds an activated licence, and performs best-effort clock-rollback
/// and tamper detection. A valid signed licence is always the highest authority — a damaged trial
/// file never locks out a genuinely licensed machine, and business data is never touched.
///
/// This is deterrence, not perfect protection: no purely-offline scheme can fully prevent an
/// expert from bypassing it.
/// </summary>
public sealed class LicensingEngine
{
    private readonly ILicenseClock _clock;
    private readonly IMachineFingerprintProvider _fingerprint;
    private readonly ILicenseStateStore _store;
    private readonly LicenseVerifier? _verifier;

    public LicensingEngine(ILicenseClock clock, IMachineFingerprintProvider fingerprint, ILicenseStateStore store, ECDsa? trustedPublicKey)
    {
        _clock = clock;
        _fingerprint = fingerprint;
        _store = store;
        _verifier = trustedPublicKey is null ? null : new LicenseVerifier(trustedPublicKey);
    }

    public MachineFingerprint GetFingerprint() => _fingerprint.GetFingerprint();

    /// <summary>Evaluates the current licensing state and advances the trusted last-seen time.</summary>
    public LicenseEvaluation Evaluate()
    {
        var machine = _fingerprint.GetFingerprint();
        var now = _clock.UtcNow;

        // 1) A stored licence that verifies fully is the highest authority — even if the trial
        //    state file is damaged, a licensed machine is never locked out.
        var licenseText = _store.LoadLicense();
        if (licenseText is not null)
        {
            var verification = VerifyLicense(licenseText, machine);
            if (verification.IsValid)
            {
                var okState = _store.Load(out var stateCorrupt);
                if (okState is not null && !stateCorrupt)
                {
                    TouchState(okState, now, corrupt: false);
                }

                return Licensed(verification.Payload!, machine);
            }

            // A stored licence that no longer verifies (edited, or for another machine).
            return new LicenseEvaluation(verification.Status, machine,
                verification.Status == LicenseStatus.MachineMismatch ? "This licence is for another computer." : "This licence could not be verified.",
                null, null, null, null, false, verification.Code);
        }

        var state = _store.Load(out var corrupt);

        // 2) Corrupt state with no valid licence: require re-activation, but never delete data.
        if (corrupt)
        {
            return new LicenseEvaluation(LicenseStatus.LicenseStateError, machine,
                "Your licence information needs attention.", null, null, null, null, false, "STATE_CORRUPT");
        }

        // 3) No trial yet.
        if (state?.TrialStartUtc is not { } trialStart)
        {
            return new LicenseEvaluation(LicenseStatus.NotStarted, machine, string.Empty, null, null, null, null, false, "TRIAL_NOT_STARTED");
        }

        // 4) Clock-rollback detection: the clock is materially earlier than the trusted last-seen time.
        if (now < state.LastSeenUtc - ProductInfo.ClockRollbackTolerance)
        {
            // Keep the higher last-seen; record the run but do not extend the trial.
            _store.Save(state with { LastRunUtc = now });
            return new LicenseEvaluation(LicenseStatus.LicenseStateError, machine,
                "Your device clock looks incorrect. Please set the correct date and time, or activate a licence.",
                null, trialStart, TrialExpiry(state), null, true, "CLOCK_ROLLBACK");
        }

        // 5) Ordinary trial evaluation.
        TouchState(state, now, corrupt: false);
        var expiry = trialStart + ProductInfo.TrialDuration;
        var remaining = expiry - now;

        if (remaining <= TimeSpan.Zero)
        {
            return new LicenseEvaluation(LicenseStatus.Expired, machine, "Your trial has ended.", TimeSpan.Zero, trialStart, expiry, null, false, "TRIAL_EXPIRED");
        }

        var status = remaining <= ProductInfo.ExpiringSoonWindow ? LicenseStatus.ExpiringSoon : LicenseStatus.Active;
        return new LicenseEvaluation(status, machine, FriendlyRemaining(remaining, expiry, now), remaining, trialStart, expiry, null, false,
            status == LicenseStatus.ExpiringSoon ? "TRIAL_EXPIRING" : "TRIAL_ACTIVE");
    }

    /// <summary>Starts the trial once, if there is no licence and no existing trial and state is readable.</summary>
    public bool StartTrialIfNeeded()
    {
        if (_store.LoadLicense() is not null)
        {
            return false; // already licensed
        }

        var state = _store.Load(out var corrupt);
        if (corrupt)
        {
            return false;
        }

        if (state?.TrialStartUtc is not null)
        {
            return false; // trial already started — never restart
        }

        var now = _clock.UtcNow;
        return _store.Save(new LicenseState
        {
            Version = ProductInfo.StateVersion,
            TrialStartUtc = now,
            LastSeenUtc = now,
            LastRunUtc = now,
        });
    }

    /// <summary>Verifies a pasted/imported licence and, on success, binds and stores it for this machine.</summary>
    public ActivationOutcome Activate(string? licenseText)
    {
        var machine = _fingerprint.GetFingerprint();
        var verification = VerifyLicense(licenseText, machine);
        if (!verification.IsValid)
        {
            return new ActivationOutcome(false, verification.Status, verification.Code, FriendlyFailure(verification));
        }

        var saved = _store.SaveLicense(licenseText!);
        if (!saved)
        {
            return new ActivationOutcome(false, LicenseStatus.LicenseStateError, "STORE_FAILED",
                "Activation could not be completed. Your data was not affected.");
        }

        // Keep the trial state consistent (create one if absent) without ever resetting a trial.
        var state = _store.Load(out var corrupt);
        if (!corrupt)
        {
            var baseState = state ?? new LicenseState { LastSeenUtc = _clock.UtcNow, LastRunUtc = _clock.UtcNow };
            _store.Save(baseState with { LastSeenUtc = Max(baseState.LastSeenUtc, _clock.UtcNow), LastRunUtc = _clock.UtcNow });
        }

        return new ActivationOutcome(true, LicenseStatus.Licensed, "ACTIVATED", "Snooker Point was activated successfully.");
    }

    /// <summary>Verifies a licence's signature and required fields against this machine (no side effects).</summary>
    public LicenseVerification VerifyLicense(string? licenseText, MachineFingerprint machine)
    {
        if (_verifier is null)
        {
            return new LicenseVerification(LicenseStatus.InvalidLicense, null, "NO_TRUSTED_KEY");
        }

        if (!LicenseText.TryDecode(licenseText, out var doc, out var decodeCode) || doc is null)
        {
            return new LicenseVerification(LicenseStatus.InvalidLicense, null, decodeCode);
        }

        // Signature first — nothing in the payload is trusted until this passes.
        if (!_verifier.Verify(doc))
        {
            return new LicenseVerification(LicenseStatus.InvalidLicense, null, "BAD_SIGNATURE");
        }

        var p = doc.Payload;
        if (!ProductInfo.SupportedFormatVersions.Contains(p.FormatVersion))
        {
            return new LicenseVerification(LicenseStatus.InvalidLicense, p, "UNSUPPORTED_FORMAT");
        }

        if (!string.Equals(p.ProductId, ProductInfo.ProductId, StringComparison.Ordinal))
        {
            return new LicenseVerification(LicenseStatus.InvalidLicense, p, "WRONG_PRODUCT");
        }

        if (p.Type != LicenseType.Lifetime)
        {
            return new LicenseVerification(LicenseStatus.InvalidLicense, p, "WRONG_TYPE");
        }

        if (!string.Equals(p.MachineHash, machine.Hash, StringComparison.Ordinal))
        {
            return new LicenseVerification(LicenseStatus.MachineMismatch, p, "MACHINE_MISMATCH");
        }

        return new LicenseVerification(LicenseStatus.Licensed, p, "OK");
    }

    // ---------- helpers ----------

    private void TouchState(LicenseState state, DateTimeOffset now, bool corrupt)
    {
        if (corrupt)
        {
            return;
        }

        _store.Save(state with { LastSeenUtc = Max(state.LastSeenUtc, now), LastRunUtc = now });
    }

    private static LicenseEvaluation Licensed(LicensePayload payload, MachineFingerprint machine) =>
        new(LicenseStatus.Licensed, machine, "Licensed", null, null, null, payload, false, "LICENSED");

    private static DateTimeOffset? TrialExpiry(LicenseState? state) =>
        state?.TrialStartUtc is { } t ? t + ProductInfo.TrialDuration : null;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    private static string FriendlyRemaining(TimeSpan remaining, DateTimeOffset expiryUtc, DateTimeOffset nowUtc)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "Your trial has ended.";
        }

        if (expiryUtc.ToLocalTime().Date == nowUtc.ToLocalTime().Date)
        {
            return "Trial expires today.";
        }

        var days = remaining.Days;
        var hours = remaining.Hours;
        if (days >= 1)
        {
            return $"{days} day{(days == 1 ? "" : "s")} {hours} hour{(hours == 1 ? "" : "s")} remaining.";
        }

        return hours >= 1
            ? $"{hours} hour{(hours == 1 ? "" : "s")} remaining."
            : $"{Math.Max(1, remaining.Minutes)} minute{(remaining.Minutes == 1 ? "" : "s")} remaining.";
    }

    private static string FriendlyFailure(LicenseVerification v) => v.Code switch
    {
        "MACHINE_MISMATCH" => "This licence was created for another computer.",
        "UNSUPPORTED_FORMAT" => "This licence format is not supported.",
        "BAD_SIGNATURE" => "The licence information appears to have been changed.",
        "WRONG_PRODUCT" => "This licence is not for Snooker Point.",
        "WRONG_TYPE" => "This licence type is not supported.",
        "NO_TRUSTED_KEY" => "Activation is not available in this build.",
        _ => "This licence file is not valid.",
    };
}
