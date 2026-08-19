using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Catalog;

/// <summary>
/// Imports and exports the product catalogue and inventory as CSV. Import is a two-step
/// preview-then-commit flow; the commit is transactional and rolls back on any critical
/// failure. Opening quantities from CSV create Opening Stock movements. Local prices are
/// never silently replaced — updates only happen under an explicit duplicate strategy.
/// </summary>
public interface IProductCsvService
{
    /// <summary>The header row (and a hint) an operator can save as a starting template.</summary>
    string Template();

    string ExportProducts();

    string ExportStockSummary();

    string ExportStockHistory();

    /// <summary>Validates CSV content and reports per-row results without writing anything.</summary>
    CsvImportPreview Preview(string csvContent);

    /// <summary>Commits a validated import under the chosen duplicate strategy, transactionally.</summary>
    OperationResult<CsvImportResult> Import(string csvContent, CsvDuplicateStrategy strategy, int actorUserId, int? shiftId);
}
