using System.Text;
using System.Text.Json;

namespace SnookerPoint.Licensing;

/// <summary>
/// Encodes a licence document to a portable, copy-pasteable text block and decodes it back.
/// The wire form is base64 of a small JSON document, wrapped in BEGIN/END markers. Decoding is
/// tolerant (accepts the wrapped block, raw base64, or raw JSON) and never throws.
/// </summary>
public static class LicenseText
{
    private const string BeginMarker = "-----BEGIN SNOOKER POINT LICENCE-----";
    private const string EndMarker = "-----END SNOOKER POINT LICENCE-----";

    public static string Encode(LicenseDocument doc)
    {
        var dto = new FileDto
        {
            Payload = new PayloadDto
            {
                FormatVersion = doc.Payload.FormatVersion,
                ProductId = doc.Payload.ProductId,
                LicenseId = doc.Payload.LicenseId,
                CustomerName = doc.Payload.CustomerName,
                MachineHash = doc.Payload.MachineHash,
                IssuedUtcTicks = doc.Payload.IssuedUtc.ToUniversalTime().UtcTicks,
                Type = (int)doc.Payload.Type,
                Notes = doc.Payload.Notes,
                Edition = doc.Payload.Edition,
                SignatureAlgorithm = doc.Payload.SignatureAlgorithm,
            },
            Signature = Convert.ToBase64String(doc.Signature),
        };

        var json = JsonSerializer.Serialize(dto);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var sb = new StringBuilder();
        sb.AppendLine(BeginMarker);
        for (var i = 0; i < base64.Length; i += 64)
        {
            sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }

        sb.AppendLine(EndMarker);
        return sb.ToString();
    }

    /// <summary>Tries to decode licence text. Returns false with a safe code on any problem.</summary>
    public static bool TryDecode(string? text, out LicenseDocument? document, out string code)
    {
        document = null;
        code = "EMPTY";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var json = ExtractJson(text.Trim());
            if (json is null)
            {
                code = "MALFORMED";
                return false;
            }

            var dto = JsonSerializer.Deserialize<FileDto>(json);
            if (dto?.Payload is null || string.IsNullOrEmpty(dto.Signature))
            {
                code = "MALFORMED";
                return false;
            }

            var payload = new LicensePayload(
                dto.Payload.FormatVersion,
                dto.Payload.ProductId ?? string.Empty,
                dto.Payload.LicenseId ?? string.Empty,
                dto.Payload.CustomerName ?? string.Empty,
                dto.Payload.MachineHash ?? string.Empty,
                new DateTimeOffset(new DateTime(dto.Payload.IssuedUtcTicks, DateTimeKind.Utc)),
                (LicenseType)dto.Payload.Type,
                dto.Payload.Notes,
                dto.Payload.Edition,
                dto.Payload.SignatureAlgorithm ?? string.Empty);

            document = new LicenseDocument(payload, Convert.FromBase64String(dto.Signature));
            code = "OK";
            return true;
        }
        catch (Exception)
        {
            code = "MALFORMED";
            return false;
        }
    }

    private static string? ExtractJson(string text)
    {
        if (text.StartsWith("{", StringComparison.Ordinal))
        {
            return text;
        }

        var body = text;
        var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (begin >= 0)
        {
            var start = begin + BeginMarker.Length;
            var end = text.IndexOf(EndMarker, start, StringComparison.Ordinal);
            body = end > start ? text.Substring(start, end - start) : text.Substring(start);
        }

        var base64 = new string(body.Where(c => !char.IsWhiteSpace(c)).ToArray());
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class FileDto
    {
        public PayloadDto? Payload { get; set; }
        public string Signature { get; set; } = string.Empty;
    }

    private sealed class PayloadDto
    {
        public int FormatVersion { get; set; }
        public string? ProductId { get; set; }
        public string? LicenseId { get; set; }
        public string? CustomerName { get; set; }
        public string? MachineHash { get; set; }
        public long IssuedUtcTicks { get; set; }
        public int Type { get; set; }
        public string? Notes { get; set; }
        public string? Edition { get; set; }
        public string? SignatureAlgorithm { get; set; }
    }
}
