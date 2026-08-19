using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Sales;

/// <summary>Filter for the sales-history query (all optional).</summary>
public sealed record SalesHistoryFilter(
    int? SaleNumber = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int? CashierUserId = null,
    int? TableSessionId = null,
    int? MethodId = null,
    SaleType? Type = null);

/// <summary>A row in sales history.</summary>
public sealed record SaleHistoryItem(
    int SaleId,
    int SaleNumber,
    DateTimeOffset CompletedUtc,
    SaleType Type,
    int? TableSessionId,
    int? TableSessionNumber,
    string CashierName,
    Money Total,
    string PaymentSummary,
    SaleStatus Status);

/// <summary>A payment portion shown in sale detail.</summary>
public sealed record SalePaymentLine(
    string MethodName,
    PaymentMethodKind Kind,
    Money Amount,
    Money? Received,
    Money? Change,
    string? Reference);

/// <summary>A stock movement linked to a sale line, shown in sale detail.</summary>
public sealed record SaleStockLine(string ProductName, decimal QuantityDelta, decimal PreviousQuantity, decimal NewQuantity);

/// <summary>Full detail of one completed sale.</summary>
public sealed record SaleDetail(
    int SaleId,
    int SaleNumber,
    DateTimeOffset CompletedUtc,
    SaleType Type,
    int? TableSessionId,
    int? TableSessionNumber,
    string CashierName,
    Money Subtotal,
    Money TableCharge,
    Money Discount,
    string? DiscountReason,
    Money Tax,
    Money Service,
    Money Total,
    Money? CashReceived,
    Money? ChangeGiven,
    IReadOnlyList<CartLine> Lines,
    IReadOnlyList<SalePaymentLine> Payments,
    IReadOnlyList<SaleStockLine> StockMovements,
    int PrintCount);
