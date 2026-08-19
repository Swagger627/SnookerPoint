using System.Buffers.Binary;
using System.Text;

namespace SnookerPoint.Licensing;

/// <summary>
/// Deterministic, length-prefixed serialisation of a licence payload. The signature is
/// computed over these bytes, which are derived from the parsed field values — never from the
/// licence file's text. Any change to any field therefore changes the bytes and breaks the
/// signature, regardless of JSON formatting or whitespace.
/// </summary>
public static class LicenseCanonical
{
    private static readonly byte[] Magic = "SPLIC"u8.ToArray();

    public static byte[] ToBytes(LicensePayload p)
    {
        using var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);

        WriteField(ms, p.FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteField(ms, p.ProductId);
        WriteField(ms, p.LicenseId);
        WriteField(ms, p.CustomerName);
        WriteField(ms, p.MachineHash);
        // A stable, culture-invariant instant (UTC ticks) so the same moment always encodes identically.
        WriteField(ms, p.IssuedUtc.ToUniversalTime().UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteField(ms, ((int)p.Type).ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteField(ms, p.Notes ?? string.Empty);
        WriteField(ms, p.Edition ?? string.Empty);
        WriteField(ms, p.SignatureAlgorithm);

        return ms.ToArray();
    }

    private static void WriteField(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, bytes.Length);
        s.Write(len);
        s.Write(bytes, 0, bytes.Length);
    }
}
