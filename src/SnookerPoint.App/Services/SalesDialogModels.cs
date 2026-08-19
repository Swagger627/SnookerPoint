using SnookerPoint.Application.Sales;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.Services;

/// <summary>What the payment dialog needs: the amount due and the active methods.</summary>
public sealed record PaymentDialogContext(Money AmountDue, IReadOnlyList<PaymentMethodOption> Methods);

/// <summary>The payment dialog's result — the validated payment portions.</summary>
public sealed record PaymentDialogResult(IReadOnlyList<PaymentInput> Payments, Money Change);

/// <summary>The discount dialog's result.</summary>
public sealed record DiscountResult(DiscountKind Kind, long Value, string Reason);

/// <summary>The price-override dialog's result.</summary>
public sealed record PriceOverrideResult(Money NewUnitPrice, string Reason);
