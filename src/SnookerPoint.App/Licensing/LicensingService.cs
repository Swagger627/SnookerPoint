using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Storage;
using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// Wires the portable <see cref="LicensingEngine"/> to Windows implementations (fingerprint,
/// DPAPI state store, trusted public key) and records safe audit summaries for licensing events.
/// Licensing state lives outside the business database and is never included in business backups.
/// </summary>
public sealed class LicensingService : ILicensingService
{
    private readonly LicensingEngine _engine;
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly ILogger<LicensingService> _logger;
    private readonly HashSet<string> _loggedThisRun = new();

    public LicensingService(
        IClock clock,
        AppDataPaths paths,
        IDbContextFactory<SnookerPointDbContext> factory,
        ILogger<LicensingService> logger)
    {
        _factory = factory;
        _logger = logger;

        var fingerprint = new WindowsMachineFingerprintProvider();

        // Per-user checkpoint (DPAPI CurrentUser) plus a best-effort machine-level checkpoint
        // (ProgramData, DPAPI LocalMachine) so switching users or deleting one copy never restarts
        // the trial. If the machine location is not writable (non-admin, no installer ACL), we fall
        // back to a per-user-only checkpoint.
        var perUser = new DpapiLicenseStateStore(paths.License, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        ILicenseStateStore machine;
        try
        {
            machine = new DpapiLicenseStateStore(paths.MachineLicense, System.Security.Cryptography.DataProtectionScope.LocalMachine);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Machine-level licence checkpoint is unavailable; using per-user state only.");
            machine = new NoOpLicenseStateStore();
        }

        var store = new LayeredLicenseStateStore(perUser, machine);
        var publicKey = TrustedPublicKeyResolver.Resolve(paths.License);
        _engine = new LicensingEngine(new ClockAdapter(clock), fingerprint, store, publicKey);
    }

    public LicenseEvaluation Evaluate()
    {
        var evaluation = _engine.Evaluate();

        // Audit discrete/rare states at most once per process run to avoid log spam.
        switch (evaluation.Status)
        {
            case LicenseStatus.ExpiringSoon:
                AuditOnce(AuditActions.TrialExpiringSoon, "Trial is nearing expiry.");
                break;
            case LicenseStatus.Expired:
                AuditOnce(AuditActions.TrialExpired, "Trial has expired; operations are locked until activation.");
                break;
            case LicenseStatus.LicenseStateError when evaluation.RollbackDetected:
                AuditOnce(AuditActions.ClockRollbackDetected, "Device clock appears to have moved backwards (code CLOCK_ROLLBACK).");
                break;
            case LicenseStatus.LicenseStateError:
                AuditOnce(AuditActions.LicenseStateCorruption, $"Licence state needs attention (code {evaluation.Code}).");
                break;
        }

        return evaluation;
    }

    public bool StartTrialIfNeeded()
    {
        var started = _engine.StartTrialIfNeeded();
        if (started)
        {
            WriteAudit(AuditActions.TrialStarted, "72-hour trial started after setup.");
        }

        return started;
    }

    public ActivationOutcome Activate(string? licenseText)
    {
        WriteAudit(AuditActions.LicenseActivationAttempted, "Offline activation attempted.");
        var outcome = _engine.Activate(licenseText);

        if (outcome.Success)
        {
            WriteAudit(AuditActions.LicenseActivated, "Licence activated for this machine.");
        }
        else
        {
            var action = outcome.Status switch
            {
                LicenseStatus.MachineMismatch => AuditActions.LicenseMachineMismatch,
                _ when outcome.Code == "BAD_SIGNATURE" => AuditActions.LicenseInvalidSignature,
                _ => AuditActions.LicenseActivationFailed,
            };
            // Log only the safe diagnostic code — never the pasted licence text.
            WriteAudit(action, $"Activation failed (code {outcome.Code}).");
        }

        return outcome;
    }

    public MachineFingerprint GetFingerprint() => _engine.GetFingerprint();

    // ---------- helpers ----------

    private void AuditOnce(string action, string details)
    {
        if (_loggedThisRun.Add(action))
        {
            WriteAudit(action, details);
        }
    }

    private void WriteAudit(string action, string details)
    {
        try
        {
            using var db = _factory.CreateDbContext();
            db.AuditEvents.Add(new AuditEvent
            {
                Utc = DateTimeOffset.UtcNow,
                Action = action,
                ActorUserId = null, // system actor (no user is logged in during licensing events)
                Entity = "License",
                Details = details,
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write licensing audit event {Action}.", action);
        }
    }

    private sealed class ClockAdapter : ILicenseClock
    {
        private readonly IClock _clock;
        public ClockAdapter(IClock clock) => _clock = clock;
        public DateTimeOffset UtcNow => _clock.UtcNow;
    }
}
