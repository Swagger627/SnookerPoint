using SnookerPoint.App.Licensing;
using SnookerPoint.Licensing;

namespace SnookerPoint.Tests.Infrastructure;

public class LicenseBuildValidationTests
{
    [Fact]
    public void EmptyKey_FailsValidation()
    {
        Assert.False(LicenseBuildValidation.IsValid("", allowDevOverride: false));
        Assert.Contains(LicenseBuildValidation.Validate("", false), e => e.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DevOverrideEnabled_FailsValidation()
    {
        using var key = LicenseKeys.GenerateEphemeral();
        var pub = LicenseKeys.ExportPublicKeyBase64(key);
        Assert.False(LicenseBuildValidation.IsValid(pub, allowDevOverride: true));
        Assert.Contains(LicenseBuildValidation.Validate(pub, true), e => e.Contains("override", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MalformedKey_FailsValidation()
    {
        Assert.False(LicenseBuildValidation.IsValid("not-a-real-key", allowDevOverride: false));
    }

    [Fact]
    public void ValidKey_NoOverride_Passes()
    {
        using var key = LicenseKeys.GenerateEphemeral();
        var pub = LicenseKeys.ExportPublicKeyBase64(key);
        Assert.True(LicenseBuildValidation.IsValid(pub, allowDevOverride: false));
    }

    [Fact]
    public void Resolver_IgnoresDevOverride_WhenDisabled_UsingEmbeddedKeyInstead()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"spkey-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using var devKey = LicenseKeys.GenerateEphemeral();
            var devPub = LicenseKeys.ExportPublicKeyBase64(devKey);
            File.WriteAllText(Path.Combine(folder, TrustedPublicKeyResolver.DevOverrideFileName), devPub);

            // Development profile honours the override (returns the dev key).
            using var devResolved = TrustedPublicKeyResolver.Resolve(folder, allowDevOverride: true);
            Assert.NotNull(devResolved);
            Assert.Equal(devPub, LicenseKeys.ExportPublicKeyBase64(devResolved!));

            // Pilot/Customer profile IGNORES the dev override and uses the embedded pilot key instead.
            using var releaseResolved = TrustedPublicKeyResolver.Resolve(folder, allowDevOverride: false);
            Assert.NotNull(releaseResolved);
            Assert.Equal(TrustedPublicKeyResolver.EmbeddedPublicKey, LicenseKeys.ExportPublicKeyBase64(releaseResolved!));
            Assert.NotEqual(devPub, LicenseKeys.ExportPublicKeyBase64(releaseResolved!)); // NOT the dev override
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void EmbeddedPilotPublicKey_IsPresent_Valid_AndPublicOnly()
    {
        var embedded = TrustedPublicKeyResolver.EmbeddedPublicKey;
        Assert.False(string.IsNullOrWhiteSpace(embedded));                 // a persistent pilot key is set
        Assert.DoesNotContain("PRIVATE", embedded, StringComparison.OrdinalIgnoreCase); // never private material
        using var _ = LicenseKeys.ImportPublicKey(embedded);              // a valid public key
        Assert.True(LicenseBuildValidation.IsValid(embedded, allowDevOverride: false)); // passes Pilot/Customer validation
    }
}
