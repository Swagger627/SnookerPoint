using System.Security.Cryptography;
using SnookerPoint.Application.Security;

namespace SnookerPoint.Infrastructure.Security;

/// <summary>
/// Salted, versioned secret hasher built on the framework's PBKDF2 (no external
/// dependencies). Produces a self-describing string that records the algorithm
/// version, iteration count, salt and derived key, so parameters can be raised over
/// time and old hashes flagged for rehash. Verification is constant-time.
/// </summary>
/// <remarks>
/// Encoded form: <c>PBKDF2-SHA256${version}${iterations}${saltBase64}${hashBase64}</c>.
/// </remarks>
public sealed class Pbkdf2SecretHasher : ISecretHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;   // 128-bit salt
    private const int KeySize = 32;    // 256-bit derived key
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    private readonly int _iterations;

    public Pbkdf2SecretHasher(int iterations = 120_000)
    {
        if (iterations < 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iteration count is too low.");
        }

        _iterations = iterations;
    }

    public string Hash(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(plaintext, salt, _iterations, Algorithm, KeySize);

        return string.Join('$',
            Prefix,
            CurrentVersion.ToString(),
            _iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    public SecretVerification Verify(string plaintext, string encodedHash)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (!TryParse(encodedHash, out var version, out var iterations, out var salt, out var expectedKey))
        {
            return new SecretVerification(false, false);
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(plaintext, salt, iterations, Algorithm, expectedKey.Length);
        var isValid = CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);

        var needsRehash = isValid && (version < CurrentVersion || iterations < _iterations);
        return new SecretVerification(isValid, needsRehash);
    }

    private static bool TryParse(
        string encoded,
        out int version,
        out int iterations,
        out byte[] salt,
        out byte[] key)
    {
        version = 0;
        iterations = 0;
        salt = Array.Empty<byte>();
        key = Array.Empty<byte>();

        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != Prefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out version) || !int.TryParse(parts[2], out iterations))
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            key = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && key.Length > 0;
    }
}
