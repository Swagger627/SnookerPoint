using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Sales;

/// <summary>
/// Read access to completed sales plus receipt reprint accounting. Completed sales are
/// immutable here — this service never edits them.
/// </summary>
public interface ISalesQueryService
{
    IReadOnlyList<SaleHistoryItem> GetHistory(SalesHistoryFilter filter);

    SaleDetail? GetDetail(int saleId);

    /// <summary>Resolves the immutable receipt data for a completed sale (for preview/reprint).</summary>
    ReceiptData? GetReceiptData(int saleId);

    /// <summary>The stored receipt text snapshot for a completed sale.</summary>
    string? GetReceiptSnapshot(int saleId);

    /// <summary>Records that a receipt was printed or reprinted (audited; increments the count).</summary>
    OperationResult MarkReceiptPrinted(int saleId, int actorUserId, bool isReprint);
}
