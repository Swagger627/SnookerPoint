using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Reporting;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Writes reports to CSV. Every cell is RFC 4180 escaped (quoted when it contains a comma,
/// quote or newline; inner quotes doubled) and guarded against spreadsheet formula injection.
/// Because cells are written verbatim as text, barcodes keep their leading zeroes. Files are
/// UTF-8 with a BOM so Excel opens them cleanly. The export is audited (no data is copied to
/// the audit record).
/// </summary>
public sealed class CsvExportService : ICsvExportService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly AppDataPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<CsvExportService> _logger;

    public CsvExportService(
        IDbContextFactory<SnookerPointDbContext> factory,
        AppDataPaths paths,
        IClock clock,
        ILogger<CsvExportService> logger)
    {
        _factory = factory;
        _paths = paths;
        _clock = clock;
        _logger = logger;
    }

    public string DefaultExportsFolder => _paths.Exports;

    public OperationResult<string> Export(CsvDocument document, string? destinationFolder, int actorUserId)
    {
        var folder = string.IsNullOrWhiteSpace(destinationFolder) ? _paths.Exports : destinationFolder!;

        string path;
        try
        {
            Directory.CreateDirectory(folder);
            var fileName = $"{Sanitize(document.Title)}-{_clock.UtcNow.ToLocalTime():yyyyMMdd-HHmmss}.csv";
            path = Path.Combine(folder, fileName);

            var sb = new StringBuilder();
            sb.Append(string.Join(",", document.Headers.Select(Escape))).Append("\r\n");
            foreach (var row in document.Rows)
            {
                sb.Append(string.Join(",", row.Select(Escape))).Append("\r\n");
            }

            // UTF-8 with BOM so spreadsheets detect the encoding.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "CSV export to {Folder} failed.", folder);
            return OperationResult<string>.Failure(FriendlyIoMessage(ex, folder));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV export to {Folder} failed unexpectedly.", folder);
            return OperationResult<string>.Failure("The export could not be saved. Please choose another folder and try again.");
        }

        WriteAudit(actorUserId, document.Title, document.Rows.Count, path);
        return OperationResult<string>.Success(path);
    }

    /// <summary>Escapes a single CSV field and neutralises spreadsheet formula injection.</summary>
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        // Neutralise classic CSV/formula-injection triggers without corrupting normal text
        // (money like "Rs 50" or "-Rs 5" is left intact; only formula/command leads are guarded).
        if (text.Length > 0 && (text[0] is '=' or '+' or '@' or '\t' or '\r'))
        {
            text = "'" + text;
        }

        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            text = "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        return text;
    }

    private static string Sanitize(string title)
    {
        var cleaned = new string(title.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim();
        cleaned = cleaned.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }

    private static string FriendlyIoMessage(Exception ex, string folder) => ex switch
    {
        UnauthorizedAccessException => $"Snooker Point does not have permission to write to {folder}. Please choose another folder.",
        IOException io when io.Message.Contains("being used", StringComparison.OrdinalIgnoreCase)
            => "That file is open in another program. Please close it and try again.",
        _ => $"The export could not be saved to {folder}. Please choose another folder and try again.",
    };

    private void WriteAudit(int actorUserId, string title, int rowCount, string path)
    {
        try
        {
            using var db = _factory.CreateDbContext();
            db.AuditEvents.Add(new AuditEvent
            {
                Utc = _clock.UtcNow,
                Action = AuditActions.ReportExported,
                ActorUserId = actorUserId,
                Entity = "Report",
                EntityId = title,
                Details = $"Exported '{title}' ({rowCount.ToString(CultureInfo.InvariantCulture)} row(s)) to {Path.GetFileName(path)}.",
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            // Auditing must never block a successful export.
            _logger.LogWarning(ex, "Could not write the export audit event.");
        }
    }
}
