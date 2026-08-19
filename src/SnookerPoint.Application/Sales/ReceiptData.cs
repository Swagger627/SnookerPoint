using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Sales;

/// <summary>One printed receipt line item.</summary>
public sealed record ReceiptLine(string Name, decimal Quantity, Money UnitPrice, Money LineTotal);

/// <summary>One printed payment portion.</summary>
public sealed record ReceiptPayment(string MethodName, Money Amount, Money? Received, Money? Change, string? Reference);

/// <summary>
/// Everything needed to render a receipt, resolved once at completion so the receipt is an
/// immutable snapshot independent of later catalogue/settings changes.
/// </summary>
public sealed record ReceiptData(
    string ClubName,
    string? Address,
    string? Phone,
    int SaleNumber,
    DateTimeOffset CompletedUtc,
    string CashierName,
    string SaleTypeText,
    string? TableInfo,
    IReadOnlyList<ReceiptLine> Lines,
    Money TableCharge,
    Money Subtotal,
    Money Discount,
    Money Tax,
    Money Service,
    Money Total,
    IReadOnlyList<ReceiptPayment> Payments,
    Money? CashReceived,
    Money? Change);
