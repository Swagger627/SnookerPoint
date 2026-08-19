using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Reporting;

/// <summary>A ready-to-write CSV document: a title (used for the file name), headers and rows.</summary>
public sealed record CsvDocument(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Writes report data to a CSV file. Fields are RFC 4180 escaped and neutralised against
/// spreadsheet formula injection; barcodes keep their leading zeroes because every field is
/// written as text. Exports default to <c>%APPDATA%\SnookerPoint\Exports</c>; another
/// destination folder may be supplied. Returns the saved file path.
/// </summary>
public interface ICsvExportService
{
    OperationResult<string> Export(CsvDocument document, string? destinationFolder, int actorUserId);

    /// <summary>The default exports folder path (created on demand).</summary>
    string DefaultExportsFolder { get; }
}
