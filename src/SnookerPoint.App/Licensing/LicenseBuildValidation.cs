using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// Validates the licensing configuration for a Pilot/Customer build. Used by tests and by the
/// build's <c>ValidateLicenseProfile</c> target so a release cannot ship without a real, valid,
/// non-overridable public verification key.
/// </summary>
public static class LicenseBuildValidation
{
    public static IReadOnlyList<string> Validate(string? embeddedPublicKeyBase64, bool allowDevOverride)
    {
        var errors = new List<string>();

        if (allowDevOverride)
        {
            errors.Add("Development public-key override is enabled — it must be disabled in a Pilot/Customer build.");
        }

        if (string.IsNullOrWhiteSpace(embeddedPublicKeyBase64))
        {
            errors.Add("The trusted public verification key is empty — set the approved public key before a Pilot/Customer build.");
            return errors;
        }

        try
        {
            using var _ = LicenseKeys.ImportPublicKey(embeddedPublicKeyBase64);
        }
        catch (Exception)
        {
            errors.Add("The trusted public verification key is malformed.");
        }

        // A private key must never be embedded as the "public" key.
        if (embeddedPublicKeyBase64.Contains("PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Private-key material was found where a public key was expected.");
        }

        return errors;
    }

    public static bool IsValid(string? embeddedPublicKeyBase64, bool allowDevOverride) =>
        Validate(embeddedPublicKeyBase64, allowDevOverride).Count == 0;
}
