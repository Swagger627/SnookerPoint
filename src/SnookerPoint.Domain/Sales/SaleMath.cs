using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Sales;

/// <summary>The computed money breakdown of a sale.</summary>
public readonly record struct SaleTotals(
    Money Subtotal,
    Money TableCharge,
    Money Discount,
    Money Tax,
    Money Service,
    Money Total);

/// <summary>
/// Pure sale arithmetic in integer paisa: line totals, a sale-level discount (fixed or
/// percentage) that can never push the total below zero, optional tax/service (zero unless
/// configured), and the final total. No dependencies, so it is trivially testable and
/// shared by the service, checkout preview and receipt.
/// </summary>
public static class SaleMath
{
    /// <summary>A line total = unit price × quantity, rounded to the nearest paisa.</summary>
    public static Money LineTotal(Money unitPrice, decimal quantity)
    {
        var paisa = decimal.Round(unitPrice.Paisa * quantity, 0, MidpointRounding.AwayFromZero);
        return Money.FromPaisa((long)paisa);
    }

    /// <summary>Resolves the discount amount for a given base (subtotal + table charge).</summary>
    public static Money ResolveDiscount(DiscountKind kind, long value, Money discountBase)
    {
        if (kind == DiscountKind.None || value <= 0 || discountBase.Paisa <= 0)
        {
            return Money.Zero;
        }

        var amount = kind switch
        {
            DiscountKind.FixedAmount => Money.FromPaisa(value),
            DiscountKind.Percentage => Money.FromPaisa(
                (long)decimal.Round(discountBase.Paisa * Math.Min(value, 100) / 100m, 0, MidpointRounding.AwayFromZero)),
            _ => Money.Zero,
        };

        // A discount can never exceed the base (total stays ≥ 0).
        return amount > discountBase ? discountBase : amount;
    }

    /// <summary>
    /// Computes the full breakdown. Tax and service are supplied already-resolved (0 by
    /// default). The total is subtotal + table charge − discount + tax + service, floored at 0.
    /// </summary>
    public static SaleTotals Compute(
        Money subtotal,
        Money tableCharge,
        DiscountKind discountKind,
        long discountValue,
        Money tax,
        Money service)
    {
        var baseAmount = subtotal + tableCharge;
        var discount = ResolveDiscount(discountKind, discountValue, baseAmount);
        var total = baseAmount - discount + tax + service;
        if (total.IsNegative)
        {
            total = Money.Zero;
        }

        return new SaleTotals(subtotal, tableCharge, discount, tax, service, total);
    }

    /// <summary>
    /// Computes the full breakdown, resolving tax and service from percentages applied to
    /// the post-discount base (subtotal + table charge − discount). Percentages are 0 when
    /// the corresponding charge is disabled, so the default is an unchanged total.
    /// </summary>
    public static SaleTotals ComputeWithRates(
        Money subtotal,
        Money tableCharge,
        DiscountKind discountKind,
        long discountValue,
        decimal taxPercent,
        decimal servicePercent)
    {
        var baseAmount = subtotal + tableCharge;
        var discount = ResolveDiscount(discountKind, discountValue, baseAmount);
        var taxable = baseAmount - discount;
        if (taxable.IsNegative)
        {
            taxable = Money.Zero;
        }

        var tax = Percentage(taxable, taxPercent);
        var service = Percentage(taxable, servicePercent);
        return new SaleTotals(subtotal, tableCharge, discount, tax, service, taxable + tax + service);
    }

    /// <summary>A percentage of a money amount, rounded to the nearest paisa. 0 for a non-positive percent.</summary>
    public static Money Percentage(Money amount, decimal percent)
    {
        if (percent <= 0 || amount.Paisa <= 0)
        {
            return Money.Zero;
        }

        var capped = Math.Min(percent, 100m);
        return Money.FromPaisa((long)decimal.Round(amount.Paisa * capped / 100m, 0, MidpointRounding.AwayFromZero));
    }
}
