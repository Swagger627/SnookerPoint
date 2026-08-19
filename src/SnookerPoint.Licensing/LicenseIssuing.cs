using System.Security.Cryptography;
using System.Text;

namespace SnookerPoint.Licensing;

/// <summary>
/// Shared, owner-side licence-issuing logic used by both the command-line LicenceIssuer and the
/// graphical Licence Manager, so there is exactly one signing/format implementation. This is
/// owner-side code (it needs a private key supplied by the caller); it never persists, copies or
/// exposes the private key.
/// </summary>
public static class LicenseIssuing
{
    /// <summary>A new unique, human-facing licence id.</summary>
    public static string NewLicenseId() => "LIC-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>
    /// Builds and signs a lifetime, machine-bound licence for the given installation code, using
    /// the existing payload format and signature algorithm. Throws <see cref="FormatException"/>
    /// if the installation code is not valid.
    /// </summary>
    public static LicenseDocument Issue(
        ECDsa privateKey,
        string installationCode,
        string customerName,
        string? licenseId = null,
        string? notes = null,
        string? edition = null)
    {
        if (!InstallationCodec.TryDecode(installationCode, out var machineHash))
        {
            throw new FormatException("The installation code is not valid.");
        }

        var payload = new LicensePayload(
            ProductInfo.CurrentFormatVersion,
            ProductInfo.ProductId,
            licenseId ?? NewLicenseId(),
            customerName.Trim(),
            machineHash,
            DateTimeOffset.UtcNow,
            LicenseType.Lifetime,
            string.IsNullOrWhiteSpace(notes) ? null : notes,
            string.IsNullOrWhiteSpace(edition) ? null : edition,
            ProductInfo.SignatureAlgorithm);

        return new LicenseSigner(privateKey).Sign(payload);
    }

    /// <summary>
    /// The public-key fingerprint (lowercase SHA-256 hex of the base64 SubjectPublicKeyInfo) derived
    /// from a key. Works for a private-key handle too (ECDSA exports the public part). Never exposes
    /// private material.
    /// </summary>
    public static string PublicKeyFingerprint(ECDsa key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LicenseKeys.ExportPublicKeyBase64(key)))).ToLowerInvariant();
}
