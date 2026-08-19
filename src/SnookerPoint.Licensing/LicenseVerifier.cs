using System.Security.Cryptography;

namespace SnookerPoint.Licensing;

/// <summary>
/// Verifies a licence document's signature against a trusted public key. Verification uses the
/// canonical serialisation of the parsed payload, so any tampering with the licence text fails.
/// This class only checks the cryptographic signature; product/format/machine checks are the
/// engine's responsibility and are only applied after the signature is confirmed valid.
/// </summary>
public sealed class LicenseVerifier
{
    private readonly ECDsa _publicKey;

    public LicenseVerifier(ECDsa publicKey)
    {
        _publicKey = publicKey;
    }

    /// <summary>True only if the signature is valid for the payload under the trusted key.</summary>
    public bool Verify(LicenseDocument document)
    {
        if (document.Signature.Length == 0)
        {
            return false;
        }

        try
        {
            var canonical = LicenseCanonical.ToBytes(document.Payload);
            return _publicKey.VerifyData(canonical, document.Signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
