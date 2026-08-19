namespace SnookerPoint.Application.Catalog;

/// <summary>How to handle a CSV row whose SKU or barcode already exists.</summary>
public enum CsvDuplicateStrategy
{
    Skip = 0,
    UpdateBySku = 1,
    UpdateByBarcode = 2,
    Cancel = 3,
}

/// <summary>The validation outcome for one CSV row, shown in the import preview.</summary>
public sealed record CsvRowPreview(
    int LineNumber,
    string Sku,
    string? Barcode,
    string Name,
    bool IsValid,
    IReadOnlyList<string> Errors,
    bool DuplicateSku,
    bool DuplicateBarcode);

/// <summary>A validated preview of a CSV import, before anything is written.</summary>
public sealed record CsvImportPreview(
    IReadOnlyList<string> Headers,
    IReadOnlyList<CsvRowPreview> Rows,
    IReadOnlyList<string> FileErrors)
{
    public int ValidCount => Rows.Count(r => r.IsValid);
    public int InvalidCount => Rows.Count(r => !r.IsValid);
    public int DuplicateCount => Rows.Count(r => r.DuplicateSku || r.DuplicateBarcode);
    public bool HasFileErrors => FileErrors.Count > 0;

    /// <summary>Importable when the file parsed and at least one valid row exists.</summary>
    public bool CanImport => !HasFileErrors && ValidCount > 0;
}

/// <summary>The result of committing a CSV import.</summary>
public sealed record CsvImportResult(
    int Added,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Messages);
