using System.Security.Cryptography;
using System.Text;

namespace SnookerPoint.Infrastructure.Security;

/// <summary>
/// Generates a readable temporary password for administrator-issued resets, e.g.
/// "Temp-7K4M9Q". The account is flagged to require a change at next login, so this
/// value is only ever used once and is never stored in plaintext.
/// </summary>
public static class TemporaryPasswordGenerator
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ"; // no ambiguous chars
    private const int BodyLength = 8;

    public static string Generate()
    {
        var sb = new StringBuilder("Temp-", 5 + BodyLength);
        for (var i = 0; i < BodyLength; i++)
        {
            sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return sb.ToString();
    }
}
