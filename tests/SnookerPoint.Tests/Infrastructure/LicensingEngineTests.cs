using System.Security.Cryptography;
using SnookerPoint.Licensing;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class LicensingEngineTests
{
    private const string MachineA = "MACHINE-A-HASH";
    private const string MachineB = "MACHINE-B-HASH";

    private static (LicensingEngine Engine, ECDsa Private, InMemoryLicenseStateStore Store, FakeLicenseClock Clock, FixedFingerprintProvider Fp)
        NewEngine(string machineHash = MachineA)
    {
        var clock = new FakeLicenseClock();
        var fp = new FixedFingerprintProvider { Fingerprint = new(1, machineHash, "AAAA-BBBB-CCCC-DDDD") };
        var store = new InMemoryLicenseStateStore();
        var priv = LicenseKeys.GenerateEphemeral();
        var pub = LicenseKeys.ImportPublicKey(LicenseKeys.ExportPublicKeyBase64(priv));
        return (new LicensingEngine(clock, fp, store, pub), priv, store, clock, fp);
    }

    private static string SignLicence(ECDsa priv, string machineHash, int format = 1,
        string product = "SNOOKERPOINT", LicenseType type = LicenseType.Lifetime)
    {
        var payload = new LicensePayload(format, product, "LIC-1", "The Club", machineHash,
            DateTimeOffset.UtcNow, type, "note", "Standard", ProductInfo.SignatureAlgorithm);
        return LicenseText.Encode(new LicenseSigner(priv).Sign(payload));
    }

    // ---------- Trial lifecycle ----------

    [Fact]
    public void Trial_NotStarted_BeforeSetupStartsIt()
    {
        var (engine, _, _, _, _) = NewEngine();
        var e = engine.Evaluate();
        Assert.Equal(LicenseStatus.NotStarted, e.Status);
        Assert.False(e.OperationsAllowed);
    }

    [Fact]
    public void Trial_StartsOnce_AndDoesNotRestart()
    {
        var (engine, _, _, clock, _) = NewEngine();
        Assert.True(engine.StartTrialIfNeeded());
        var first = engine.Evaluate();
        Assert.Equal(LicenseStatus.Active, first.Status);

        clock.Advance(TimeSpan.FromHours(10));
        Assert.False(engine.StartTrialIfNeeded()); // never restarts
        var second = engine.Evaluate();
        Assert.Equal(first.TrialStartUtc, second.TrialStartUtc); // same start
    }

    [Fact]
    public void Trial_IsExactly72Hours()
    {
        var (engine, _, _, clock, _) = NewEngine();
        engine.StartTrialIfNeeded();
        var start = engine.Evaluate().TrialStartUtc!.Value;

        clock.UtcNow = start + TimeSpan.FromHours(71);
        Assert.True(engine.Evaluate().OperationsAllowed);           // still within trial

        clock.UtcNow = start + TimeSpan.FromHours(72);
        var expired = engine.Evaluate();
        Assert.Equal(LicenseStatus.Expired, expired.Status);        // exactly 72h
        Assert.Equal(start + TimeSpan.FromHours(72), expired.TrialExpiryUtc);
    }

    [Fact]
    public void Restart_DoesNotResetTrial()
    {
        var (engine, _, store, clock, fp) = NewEngine();
        engine.StartTrialIfNeeded();
        var start = engine.Evaluate().TrialStartUtc;

        // A "restart" is a brand-new engine over the same persisted store.
        clock.Advance(TimeSpan.FromHours(5));
        var engine2 = new LicensingEngine(clock, fp, store, null);
        Assert.False(engine2.StartTrialIfNeeded());
        // The store still holds the original start time.
        Assert.Equal(start, store.State!.TrialStartUtc);
    }

    [Fact]
    public void ExpiringSoon_WarnsWithin24Hours()
    {
        var (engine, _, _, clock, _) = NewEngine();
        engine.StartTrialIfNeeded();
        var start = engine.Evaluate().TrialStartUtc!.Value;

        clock.UtcNow = start + TimeSpan.FromHours(49); // 23h remaining
        Assert.Equal(LicenseStatus.ExpiringSoon, engine.Evaluate().Status);
    }

    [Fact]
    public void ActiveTrial_AllowsOperations_ExpiredBlocks()
    {
        var (engine, _, _, clock, _) = NewEngine();
        engine.StartTrialIfNeeded();
        var start = engine.Evaluate().TrialStartUtc!.Value;

        Assert.True(engine.Evaluate().OperationsAllowed);
        clock.UtcNow = start + TimeSpan.FromHours(80);
        Assert.False(engine.Evaluate().OperationsAllowed);
    }

    // ---------- Clock rollback ----------

    [Fact]
    public void ClockRollback_IsDetected_AndDoesNotExtendTrial()
    {
        var (engine, _, _, clock, _) = NewEngine();
        engine.StartTrialIfNeeded();
        var start = engine.Evaluate().TrialStartUtc!.Value;

        clock.UtcNow = start + TimeSpan.FromHours(10);
        engine.Evaluate(); // trusted last-seen advances to +10h

        clock.UtcNow = start + TimeSpan.FromHours(10) - TimeSpan.FromMinutes(30); // beyond tolerance
        var rolled = engine.Evaluate();
        Assert.Equal(LicenseStatus.LicenseStateError, rolled.Status);
        Assert.True(rolled.RollbackDetected);
    }

    [Fact]
    public void MinorClockAdjustment_IsTolerated()
    {
        var (engine, _, _, clock, _) = NewEngine();
        engine.StartTrialIfNeeded();
        var start = engine.Evaluate().TrialStartUtc!.Value;

        clock.UtcNow = start + TimeSpan.FromHours(10);
        engine.Evaluate();

        clock.UtcNow = start + TimeSpan.FromHours(10) - TimeSpan.FromMinutes(5); // within tolerance
        Assert.Equal(LicenseStatus.Active, engine.Evaluate().Status);
    }

    // ---------- Licence verification ----------

    [Fact]
    public void ValidLicence_ForThisMachine_Verifies()
    {
        var (engine, priv, _, _, _) = NewEngine();
        var text = SignLicence(priv, MachineA);
        Assert.True(engine.VerifyLicense(text, engine.GetFingerprint()).IsValid);
    }

    [Fact]
    public void ModifiedPayload_Fails()
    {
        var (engine, priv, _, _, _) = NewEngine();
        var doc = new LicenseSigner(priv).Sign(new LicensePayload(1, "SNOOKERPOINT", "LIC-1", "The Club", MachineA,
            DateTimeOffset.UtcNow, LicenseType.Lifetime, null, null, ProductInfo.SignatureAlgorithm));
        var tampered = doc with { Payload = doc.Payload with { CustomerName = "Someone Else" } };
        Assert.Equal("BAD_SIGNATURE", engine.VerifyLicense(LicenseText.Encode(tampered), engine.GetFingerprint()).Code);
    }

    [Fact]
    public void ModifiedSignature_Fails()
    {
        var (engine, priv, _, _, _) = NewEngine();
        var doc = new LicenseSigner(priv).Sign(new LicensePayload(1, "SNOOKERPOINT", "LIC-1", "The Club", MachineA,
            DateTimeOffset.UtcNow, LicenseType.Lifetime, null, null, ProductInfo.SignatureAlgorithm));
        var sig = (byte[])doc.Signature.Clone();
        sig[0] ^= 0xFF;
        Assert.Equal("BAD_SIGNATURE", engine.VerifyLicense(LicenseText.Encode(doc with { Signature = sig }), engine.GetFingerprint()).Code);
    }

    [Fact]
    public void WrongProduct_Fails()
    {
        var (engine, priv, _, _, _) = NewEngine();
        var text = SignLicence(priv, MachineA, product: "OTHERPRODUCT");
        Assert.Equal("WRONG_PRODUCT", engine.VerifyLicense(text, engine.GetFingerprint()).Code);
    }

    [Fact]
    public void UnsupportedFormat_Fails()
    {
        var (engine, priv, _, _, _) = NewEngine();
        var text = SignLicence(priv, MachineA, format: 99);
        Assert.Equal("UNSUPPORTED_FORMAT", engine.VerifyLicense(text, engine.GetFingerprint()).Code);
    }

    [Fact]
    public void WrongMachine_Fails_SameMachineSucceeds()
    {
        var (engine, priv, _, _, _) = NewEngine();
        Assert.Equal(LicenseStatus.MachineMismatch, engine.VerifyLicense(SignLicence(priv, MachineB), engine.GetFingerprint()).Status);
        Assert.True(engine.VerifyLicense(SignLicence(priv, MachineA), engine.GetFingerprint()).IsValid);
    }

    // ---------- Activation ----------

    [Fact]
    public void Activation_Succeeds_AndSurvivesRestart()
    {
        var (engine, priv, store, clock, fp) = NewEngine();
        engine.StartTrialIfNeeded();

        var outcome = engine.Activate(SignLicence(priv, MachineA));
        Assert.True(outcome.Success);
        Assert.Equal(LicenseStatus.Licensed, engine.Evaluate().Status);

        // "Restart": a fresh engine over the same store still sees the licence.
        var pub = LicenseKeys.ImportPublicKey(LicenseKeys.ExportPublicKeyBase64(priv));
        var engine2 = new LicensingEngine(clock, fp, store, pub);
        Assert.Equal(LicenseStatus.Licensed, engine2.Evaluate().Status);
    }

    [Fact]
    public void Activation_OfInvalidLicence_LeavesNoStoredLicence()
    {
        var (engine, _, store, _, _) = NewEngine();
        var outcome = engine.Activate("not a real licence");
        Assert.False(outcome.Success);
        Assert.Null(store.License); // nothing stored; business data untouched
    }

    [Fact]
    public void MachineMismatch_ActivationMessage_IsFriendly()
    {
        var (engine, priv, _, _, _) = NewEngine();
        var outcome = engine.Activate(SignLicence(priv, MachineB));
        Assert.False(outcome.Success);
        Assert.Equal("This licence was created for another computer.", outcome.Message);
    }

    [Fact]
    public void LifetimeLicence_RemainsValid_AfterTrialExpiry()
    {
        var (engine, priv, _, clock, _) = NewEngine();
        engine.StartTrialIfNeeded();
        engine.Activate(SignLicence(priv, MachineA));

        clock.Advance(TimeSpan.FromDays(30)); // well past 72h
        Assert.Equal(LicenseStatus.Licensed, engine.Evaluate().Status);
    }

    [Fact]
    public void ValidLicence_OverridesDamagedTrialState()
    {
        var (engine, priv, store, _, _) = NewEngine();
        engine.Activate(SignLicence(priv, MachineA));
        store.Corrupt = true; // the trial-state file is damaged

        var e = engine.Evaluate();
        Assert.Equal(LicenseStatus.Licensed, e.Status); // a valid licence is the highest authority
    }

    [Fact]
    public void CorruptState_WithNoLicence_RequiresActivation_ButDoesNotStartTrial()
    {
        var (engine, _, store, _, _) = NewEngine();
        store.Corrupt = true;
        Assert.Equal(LicenseStatus.LicenseStateError, engine.Evaluate().Status);
        Assert.False(engine.StartTrialIfNeeded()); // never start a trial over corrupt state
    }

    [Fact]
    public void RestoringLicenceStateOnAnotherMachine_DoesNotActivate()
    {
        // Activate on machine A, then evaluate the very same stored state on machine B.
        var (engineA, priv, store, clock, _) = NewEngine(MachineA);
        engineA.Activate(SignLicence(priv, MachineA));

        var pub = LicenseKeys.ImportPublicKey(LicenseKeys.ExportPublicKeyBase64(priv));
        var fpB = new FixedFingerprintProvider { Fingerprint = new(1, MachineB, "BBBB") };
        var engineB = new LicensingEngine(clock, fpB, store, pub);

        Assert.Equal(LicenseStatus.MachineMismatch, engineB.Evaluate().Status);
        Assert.False(engineB.Evaluate().OperationsAllowed);
    }
}
