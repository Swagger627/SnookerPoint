using System.Security.Cryptography;

namespace SnookerPoint.Licensing;

/// <summary>
/// Signs a licence payload with an ECDSA P-256 private key over the canonical payload bytes.
/// Owner-side / test use only — the private key is supplied by the caller and never persisted
/// here.
/// </summary>
public sealed class LicenseSigner
{
    private readonly ECDsa _privateKey;

    public LicenseSigner(ECDsa privateKey)
    {
        _privateKey = privateKey;
    }

    public LicenseDocument Sign(LicensePayload payload)
    {
        var canonical = LicenseCanonical.ToBytes(payload);
        var signature = _privateKey.SignData(canonical, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new LicenseDocument(payload, signature);
    }
}
