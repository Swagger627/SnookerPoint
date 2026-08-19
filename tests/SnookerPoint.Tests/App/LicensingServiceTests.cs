using Microsoft.Extensions.Logging.Abstractions;
using SnookerPoint.App.Licensing;
using SnookerPoint.Application.Audit;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Exercises the App-level LicensingService over the real DPAPI store + Windows fingerprint,
/// focusing on safe auditing (no secrets, no pasted licence text, no raw machine id).
/// </summary>
public class LicensingServiceTests
{
    private static LicensingService Create(Phase1Environment env) =>
        new(env.Clock, env.Paths, env.Factory, NullLogger<LicensingService>.Instance);

    [Fact]
    public void StartTrial_WritesTrialStartedAudit()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);
        var svc = Create(env);

        Assert.True(svc.StartTrialIfNeeded());
        var events = env.Audit.Query(new AuditFilter(), 0, 1000);
        Assert.Contains(events, e => e.Action == AuditActions.TrialStarted);
    }

    [Fact]
    public void FailedActivation_AuditsCodeOnly_NeverThePastedLicenceOrMachineId()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);
        var svc = Create(env);
        var machineHash = svc.GetFingerprint().Hash;
        const string secretPaste = "SECRET-PASTED-LICENCE-TEXT-DO-NOT-LOG";

        var outcome = svc.Activate(secretPaste);
        Assert.False(outcome.Success);

        var events = env.Audit.Query(new AuditFilter(), 0, 1000);
        Assert.Contains(events, e => e.Action == AuditActions.LicenseActivationAttempted);
        Assert.Contains(events, e => e.Action == AuditActions.LicenseActivationFailed);

        foreach (var e in events)
        {
            Assert.DoesNotContain(secretPaste, e.Details ?? string.Empty);
            Assert.DoesNotContain(machineHash, e.Details ?? string.Empty); // no raw machine identifier
        }
    }

    [Fact]
    public void LicensingAudit_UsesSystemActor_WhenNoUserLoggedIn()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);
        var svc = Create(env);
        svc.StartTrialIfNeeded();

        using var db = env.NewContext();
        var trialEvent = db.AuditEvents.First(a => a.Action == AuditActions.TrialStarted);
        Assert.Null(trialEvent.ActorUserId); // system actor
    }
}
