using System.Security.Cryptography;

namespace SnookerPoint.Licensing;

/// <summary>
/// ECDSA P-256 key helpers. Used by the owner-side issuer and by tests (with ephemeral keys).
/// The customer application only ever imports a <em>public</em> key. No private key is embedded
/// anywhere in the customer build.
/// </summary>
public static class LicenseKeys
{
    /// <summary>Creates a fresh ephemeral P-256 key pair (used by tests and the owner issuer).</summary>
    public static ECDsa GenerateEphemeral() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Exports the public key as base64 SubjectPublicKeyInfo (safe to embed/share).</summary>
    public static string ExportPublicKeyBase64(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>Exports the private key as base64 PKCS#8. Owner-side only — never ship or commit this.</summary>
    public static string ExportPrivateKeyBase64(ECDsa key) => Convert.ToBase64String(key.ExportPkcs8PrivateKey());

    /// <summary>Imports a base64 SubjectPublicKeyInfo public key for verification.</summary>
    public static ECDsa ImportPublicKey(string base64)
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64.Trim()), out _);
        return key;
    }

    /// <summary>Imports a base64 PKCS#8 private key for signing (owner-side / tests only).</summary>
    public static ECDsa ImportPrivateKey(string base64)
    {
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(base64.Trim()), out _);
        return key;
    }
}
