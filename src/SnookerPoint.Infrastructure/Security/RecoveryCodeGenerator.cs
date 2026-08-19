using System.Security.Cryptography;
using System.Text;

namespace SnookerPoint.Infrastructure.Security;

/// <summary>
/// Generates a cryptographically secure, human-transcribable recovery code, e.g.
/// "4KJ9-QW7T-MN2R-8XPD-VH3F". Uses a Crockford-style alphabet with ambiguous
/// characters (0/O, 1/I/L) removed. The plaintext is only ever shown once; it is
/// stored only as a salted hash by the caller.
/// </summary>
public static class RecoveryCodeGenerator
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ"; // 31 chars, no 0/O/1/I/L
    private const int Groups = 5;
    private const int GroupSize = 4;

    /// <summary>Produces a new random recovery code with grouped, hyphen-separated blocks.</summary>
    public static string Generate()
    {
        var sb = new StringBuilder(Groups * GroupSize + Groups);
        for (var g = 0; g < Groups; g++)
        {
            if (g > 0)
            {
                sb.Append('-');
            }

            for (var i = 0; i < GroupSize; i++)
            {
                sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Normalises a user-entered code for comparison: upper-cased, whitespace and hyphens
    /// removed. (The hash is computed over the normalised form so spacing never matters.)
    /// </summary>
    public static string Normalize(string? code) =>
        new((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
