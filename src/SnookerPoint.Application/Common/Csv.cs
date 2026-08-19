using System.Text;

namespace SnookerPoint.Application.Common;

/// <summary>
/// A tiny, dependency-free CSV reader/writer that handles quoted fields, embedded
/// commas, escaped quotes ("") and quoted newlines. Deliberately minimal — enough for
/// the Phase 3 product import/export without pulling in a CSV library.
/// </summary>
public static class Csv
{
    /// <summary>Parses CSV text into rows of fields. Blank trailing lines are ignored.</summary>
    public static List<List<string>> Parse(string content)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(content))
        {
            return rows;
        }

        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var i = 0;

        while (i < content.Length)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    i++;
                    break;
                case '\r':
                    i++;
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    i++;
                    break;
                default:
                    field.Append(c);
                    i++;
                    break;
            }
        }

        // Flush the final field/row if the file didn't end with a newline.
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        // Drop rows that are entirely empty (e.g. a trailing blank line).
        rows.RemoveAll(r => r.Count == 1 && string.IsNullOrWhiteSpace(r[0]));
        return rows;
    }

    /// <summary>Escapes a single field for CSV output, quoting when necessary.</summary>
    public static string Escape(string? value)
    {
        var s = value ?? string.Empty;
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        return s;
    }

    /// <summary>Writes a single CSV line (fields escaped) with a trailing newline.</summary>
    public static string Line(params string?[] fields) =>
        string.Join(",", fields.Select(Escape)) + "\r\n";
}
