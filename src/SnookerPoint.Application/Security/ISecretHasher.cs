namespace SnookerPoint.Application.Security;

/// <summary>Outcome of verifying a plaintext secret against a stored hash.</summary>
/// <param name="IsValid">True when the secret matches.</param>
/// <param name="NeedsRehash">
/// True when the stored hash used weaker parameters than the current policy and
/// should be re-hashed on the next successful use.
/// </param>
public readonly record struct SecretVerification(bool IsValid, bool NeedsRehash);

/// <summary>
/// Hashes and verifies secrets (passwords, PINs) using a salted, versioned,
/// configurable key-derivation function. Implementations must use constant-time
/// comparison and must never return or log the plaintext.
/// </summary>
public interface ISecretHasher
{
    /// <summary>Produces an encoded, salted hash string for the given plaintext.</summary>
    string Hash(string plaintext);

    /// <summary>Verifies a plaintext against a previously produced encoded hash.</summary>
    SecretVerification Verify(string plaintext, string encodedHash);
}
