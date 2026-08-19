using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Sales;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Sales;

/// <summary>A configurable payment method option for the UI.</summary>
public sealed record PaymentMethodOption(int Id, string Name, PaymentMethodKind Kind, bool IsActive, bool IsSystem);

/// <summary>One product line in a draft cart.</summary>
public sealed record CartLine(
    int LineId,
    int? ProductId,
    string Name,
    string Sku,
    string? Barcode,
    Money UnitPrice,
    decimal Quantity,
    Money LineTotal,
    Money? OriginalUnitPrice,
    bool TrackInventory);

/// <summary>A full view of a draft/held sale for the POS screen.</summary>
public sealed record DraftSaleView(
    int SaleId,
    SaleType Type,
    SaleStatus Status,
    string? Label,
    int? TableSessionId,
    string? TableLabel,
    Money TableCharge,
    BillingType? TableBillingType,
    DiscountKind DiscountKind,
    long DiscountValue,
    string? DiscountReason,
    IReadOnlyList<CartLine> Lines,
    SaleTotals Totals);

/// <summary>A row in the held-sales list.</summary>
public sealed record HeldSaleListItem(
    int SaleId,
    string? Label,
    SaleType Type,
    int LineCount,
    Money Total,
    DateTimeOffset UpdatedUtc,
    int? TableSessionId);

/// <summary>A completed table session awaiting checkout.</summary>
public sealed record AwaitingCheckoutItem(
    int SessionId,
    int SessionNumber,
    string TableNames,
    string? CustomerLabel,
    DateTimeOffset? FinishUtc,
    long BillableSeconds,
    Money TableCharge,
    BillingType BillingType,
    string FinishedByName,
    bool AlreadyInDraft);

/// <summary>One intended payment portion from the payment dialog.</summary>
public sealed record PaymentInput(
    int MethodId,
    Money Amount,
    Money? CashReceived,
    string? Reference,
    string? Note);

/// <summary>Request to complete (pay) a sale. Shift must be open.</summary>
public sealed record CompleteSaleRequest(
    int SaleId,
    IReadOnlyList<PaymentInput> Payments,
    int ActorUserId,
    int ShiftId);

/// <summary>The result of completing a sale.</summary>
public sealed record SaleCompletionResult(int SaleId, int SaleNumber, Money Total, Money Change, string ReceiptText);
