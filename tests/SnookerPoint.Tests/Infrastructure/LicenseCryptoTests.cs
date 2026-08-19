using System.Security.Cryptography;
using SnookerPoint.App.Licensing;
using SnookerPoint.Licensing;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class LicenseCryptoTests
{
    private static LicensePayload SamplePayload(string machineHash) => new(
        ProductInfo.CurrentFormatVersion, ProductInfo.ProductId, "LIC-TEST", "The Club", machineHash,
        DateTimeOffset.UtcNow, LicenseType.Lifetime, "note", "Standard", ProductInfo.SignatureAlgorithm);

    [Fact]
    public void SignThenVerify_RoundTrips()
    {
        using var key = LicenseKeys.GenerateEphemeral();
        var doc = new LicenseSigner(key).Sign(SamplePayload("MACHINE"));
        using var pub = LicenseKeys.ImportPublicKey(LicenseKeys.ExportPublicKeyBase64(key));
        Assert.True(new LicenseVerifier(pub).Verify(doc));
    }

    [Fact]
    public void AnotherKey_DoesNotVerify()
    {
        using var key = LicenseKeys.GenerateEphemeral();
        using var other = LicenseKeys.GenerateEphemeral();
        var doc = new LicenseSigner(key).Sign(SamplePayload("MACHINE"));
        using var wrongPub = LicenseKeys.ImportPublicKey(LicenseKeys.ExportPublicKeyBase64(other));
        Assert.False(new LicenseVerifier(wrongPub).Verify(doc));
    }

    [Fact]
    public void LicenseText_EncodesAndDecodes()
    {
        using var key = LicenseKeys.GenerateEphemeral();
        var doc = new LicenseSigner(key).Sign(SamplePayload("MACHINE"));
        var text = LicenseText.Encode(doc);

        Assert.True(LicenseText.TryDecode(text, out var decoded, out _));
        Assert.Equal(doc.Payload, decoded!.Payload);
        Assert.Equal(doc.Signature, decoded.Signature);
    }

    [Fact]
    public void LicenseText_RejectsGarbage()
    {
        Assert.False(LicenseText.TryDecode("this is not a licence", out _, out var code));
        Assert.Equal("MALFORMED", code);
    }

    [Fact]
    public void InstallationCodec_RoundTrips()
    {
        var hash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var code = InstallationCodec.Encode(hash);
        Assert.Contains("-", code);
        Assert.Equal(hash, InstallationCodec.Decode(code));
    }

    [Fact]
    public void Issuer_SignsFromInstallationCode_AndVerifierAccepts()
    {
        // Owner side: an ephemeral test key (never a production key).
        using var privateKey = LicenseKeys.GenerateEphemeral();

        // Customer side: an installation code encodes the machine hash.
        var machineHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var installationCode = InstallationCodec.Encode(machineHash);

        // Owner decodes the code back to the hash and signs a lifetime licence bound to it.
        Assert.True(InstallationCodec.TryDecode(installationCode, out var decodedHash));
        Assert.Equal(machineHash, decodedHash);
        var doc = new LicenseSigner(privateKey).Sign(SamplePayload(decodedHash));
        var text = LicenseText.Encode(doc);

        // Customer side: the public-key verifier accepts the issuer's output for this machine.
        var clock = new FakeLicenseClock();
        var fp = new FixedFingerprintProvider { Fingerprint = new(1, machineHash, installationCode) };
        using var pub = LicenseKeys.ImportPublicKey(LicenseKeys.ExportPublicKeyBase64(privateKey));
        var engine = new LicensingEngine(clock, fp, new InMemoryLicenseStateStore(), pub);

        Assert.True(engine.VerifyLicense(text, fp.GetFingerprint()).IsValid);
        Assert.True(engine.Activate(text).Success);
        Assert.Equal(LicenseStatus.Licensed, engine.Evaluate().Status);
    }

    // ---------- Windows machine fingerprint ----------

    [Fact]
    public void Fingerprint_IsStable_AndCodeDecodesToHash()
    {
        var provider = new WindowsMachineFingerprintProvider();
        var a = provider.GetFingerprint();
        var b = provider.GetFingerprint();

        Assert.Equal(a.Hash, b.Hash);                 // stable across "restarts"
        Assert.Equal(a.InstallationCode, b.InstallationCode);
        Assert.Equal(64, a.Hash.Length);              // SHA-256 hex
        Assert.Equal(a.Hash, InstallationCodec.Decode(a.InstallationCode));
    }

    // ---------- DPAPI-protected store ----------

    [Fact]
    public void DpapiStore_RoundTrips_AndIsNotPlaintext()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"splic-{Guid.NewGuid():N}");
        try
        {
            var store = new DpapiLicenseStateStore(folder);
            var now = DateTimeOffset.UtcNow;
            Assert.True(store.Save(new LicenseState { TrialStartUtc = now, LastSeenUtc = now, LastRunUtc = now }));
            Assert.True(store.SaveLicense("SECRET-LICENCE-BODY"));

            var loaded = store.Load(out var corrupt);
            Assert.False(corrupt);
            Assert.NotNull(loaded!.TrialStartUtc);
            Assert.Equal("SECRET-LICENCE-BODY", store.LoadLicense());

            // The on-disk bytes must be protected, not plaintext.
            var raw = File.ReadAllText(Path.Combine(folder, "license.dat"));
            Assert.DoesNotContain("SECRET-LICENCE-BODY", raw);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void DpapiStore_DetectsCorruptState()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"splic-{Guid.NewGuid():N}");
        try
        {
            var store = new DpapiLicenseStateStore(folder);
            var now = DateTimeOffset.UtcNow;
            store.Save(new LicenseState { TrialStartUtc = now, LastSeenUtc = now, LastRunUtc = now });

            // Tamper with the protected file.
            var statePath = Path.Combine(folder, "state.dat");
            var bytes = File.ReadAllBytes(statePath);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(statePath, bytes);

            store.Load(out var corrupt);
            Assert.True(corrupt);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best-effort */ }
        }
    }
}
