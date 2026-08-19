using System.Text;

namespace SnookerPoint.Licensing;

/// <summary>
/// Reversible, human-copyable encoding of a machine-fingerprint hash (the "Installation Code").
/// The customer copies this code to the owner, who decodes it back to the fingerprint hash and
/// signs a licence bound to it. Uses Crockford base32 (no ambiguous I/L/O/U), grouped in fours.
/// </summary>
public static class InstallationCodec
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Encodes a fingerprint hash (hex string) into a grouped installation code.</summary>
    public static string Encode(string hashHex)
    {
        var bytes = Convert.FromHexString(hashHex);
        var raw = Base32Encode(bytes);
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i += 4)
        {
            if (i > 0)
            {
                sb.Append('-');
            }

            sb.Append(raw.AsSpan(i, Math.Min(4, raw.Length - i)));
        }

        return sb.ToString();
    }

    /// <summary>Decodes an installation code back to the fingerprint hash (hex). Throws on malformed input.</summary>
    public static string Decode(string code)
    {
        var cleaned = new string((code ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(NormaliseChar)
            .ToArray());
        var bytes = Base32Decode(cleaned);
        return Convert.ToHexString(bytes);
    }

    public static bool TryDecode(string code, out string hashHex)
    {
        try
        {
            hashHex = Decode(code);
            return true;
        }
        catch (Exception)
        {
            hashHex = string.Empty;
            return false;
        }
    }

    private static char NormaliseChar(char c)
    {
        c = char.ToUpperInvariant(c);
        return c switch
        {
            'I' or 'L' => '1',
            'O' => '0',
            'U' => 'V',
            _ => c,
        };
    }

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return sb.ToString();
    }

    private static byte[] Base32Decode(string text)
    {
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            var index = Alphabet.IndexOf(c);
            if (index < 0)
            {
                throw new FormatException("Invalid installation code character.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return output.ToArray();
    }
}
