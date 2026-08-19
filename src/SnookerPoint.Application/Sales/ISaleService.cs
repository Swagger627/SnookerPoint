using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Sales;

/// <summary>
/// The draft-sale lifecycle and transactional checkout. Drafts persist in SQLite so a cart
/// survives a crash; they deduct no stock and are not revenue. Completion is one atomic
/// transaction: it snapshots totals, records payments, deducts tracked inventory exactly
/// once, links and marks any table session Checked Out, and assigns a unique sale number.
/// A completed sale is immutable. An open shift is required to complete a payment.
/// </summary>
public interface ISaleService
{
    OperationResult<int> CreateWalkinDraft(int actorUserId);

    /// <summary>Creates a draft attached to an Awaiting-Checkout session, importing its frozen charge.</summary>
    OperationResult<int> CreateTableCheckoutDraft(int sessionId, int actorUserId);

    DraftSaleView? GetDraft(int saleId);

    IReadOnlyList<HeldSaleListItem> GetHeldSales();

    IReadOnlyList<AwaitingCheckoutItem> GetAwaitingCheckout();

    /// <summary>Adds a product to the cart, merging into an existing line (increasing quantity).</summary>
    OperationResult AddProduct(int saleId, int productId, decimal quantity, int actorUserId);

    OperationResult SetLineQuantity(int saleId, int lineId, decimal quantity, int actorUserId);

    OperationResult RemoveLine(int saleId, int lineId, int actorUserId);

    /// <summary>Overrides a line's unit price (Manager/Admin/Owner), recording the original and a reason.</summary>
    OperationResult OverrideLinePrice(int saleId, int lineId, Money newUnitPrice, string reason, int actorUserId);

    OperationResult ApplyDiscount(int saleId, DiscountKind kind, long value, string reason, int actorUserId);

    OperationResult RemoveDiscount(int saleId, int actorUserId);

    OperationResult Hold(int saleId, string? label, int actorUserId);

    OperationResult Reopen(int saleId, int actorUserId);

    OperationResult Cancel(int saleId, int actorUserId);

    /// <summary>Completes (pays) a sale in one transaction. Requires an open shift and full payment.</summary>
    OperationResult<SaleCompletionResult> Complete(CompleteSaleRequest request);
}
