using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// Resolves the trusted public verification key for licences. The customer build carries only a
/// public key (embedded as the <c>PublicKey.txt</c> resource — public material, safe to ship).
/// A development override file (<c>trusted-public-key.dev.txt</c> in the managed License folder)
/// is honoured ONLY in Development builds (<see cref="LicenseProfile.AllowDevOverride"/>); Pilot
/// and Customer Release builds ignore it. When no key is available, verification simply fails and
/// the app runs in trial-only mode.
/// </summary>
public static class TrustedPublicKeyResolver
{
    public const string DevOverrideFileName = "trusted-public-key.dev.txt";
    private const string EmbeddedResourceName = "SnookerPoint.App.Licensing.PublicKey.txt";

    /// <summary>The embedded production public key (base64 SubjectPublicKeyInfo), or empty when unset.</summary>
    public static string EmbeddedPublicKey => ReadEmbeddedKey();

    public static ECDsa? Resolve(string licenseFolder) => Resolve(licenseFolder, LicenseProfile.AllowDevOverride);

    /// <summary>Resolves the trusted key; <paramref name="allowDevOverride"/> is exposed for testing both profiles.</summary>
    public static ECDsa? Resolve(string licenseFolder, bool allowDevOverride)
    {
        // 1) Development override (Development builds only).
        if (allowDevOverride)
        {
            try
            {
                var devFile = Path.Combine(licenseFolder, DevOverrideFileName);
                if (File.Exists(devFile))
                {
                    var b64 = File.ReadAllText(devFile).Trim();
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        return LicenseKeys.ImportPublicKey(b64);
                    }
                }
            }
            catch (Exception)
            {
                // fall through to the embedded key
            }
        }

        // 2) Embedded production public key.
        var embedded = ReadEmbeddedKey();
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            try
            {
                return LicenseKeys.ImportPublicKey(embedded);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    private static string ReadEmbeddedKey()
    {
        try
        {
            using var stream = typeof(TrustedPublicKeyResolver).Assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
