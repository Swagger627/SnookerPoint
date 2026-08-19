using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Sales;

/// <summary>
/// One intended payment portion. <see cref="Amount"/> is the portion of the bill this
/// payment covers. For cash, <see cref="CashReceived"/> may exceed <see cref="Amount"/>;
/// the difference is change. Electronic payments never carry change.
/// </summary>
public readonly record struct PaymentEntry(
    PaymentMethodKind Kind,
    Money Amount,
    Money? CashReceived);

/// <summary>The outcome of validating a set of payment portions against an amount due.</summary>
public readonly record struct PaymentValidation(
    bool IsValid,
    string? Error,
    Money Applied,
    Money Remaining,
    Money CashApplied,
    Money ElectronicApplied,
    Money Change);

/// <summary>
/// Pure validation of split payments against an amount due, in integer paisa. The applied
/// amounts must sum to exactly the amount due (no under-payment, and the applied total can't
/// exceed it). Cash overpayment is expressed only as change (received − applied); electronic
/// payments carry no change. The cash applied (not the change) is what affects the drawer.
/// </summary>
public static class PaymentMath
{
    public static PaymentValidation Validate(Money amountDue, IReadOnlyList<PaymentEntry> entries)
    {
        var applied = Money.Zero;
        var cashApplied = Money.Zero;
        var electronicApplied = Money.Zero;
        var change = Money.Zero;

        if (entries.Count == 0)
        {
            return new PaymentValidation(false, "Add at least one payment.", Money.Zero, amountDue, Money.Zero, Money.Zero, Money.Zero);
        }

        foreach (var entry in entries)
        {
            if (entry.Amount.IsNegative)
            {
                return Fail("A payment amount cannot be negative.", amountDue);
            }

            if (entry.Kind == PaymentMethodKind.Cash)
            {
                var received = entry.CashReceived ?? entry.Amount;
                if (received < entry.Amount)
                {
                    return Fail("Cash received is less than the amount applied.", amountDue);
                }

                change += received - entry.Amount;
                cashApplied += entry.Amount;
            }
            else
            {
                if (entry.Amount.IsZero)
                {
                    return Fail("Enter an amount for the electronic payment.", amountDue);
                }

                electronicApplied += entry.Amount;
            }

            applied += entry.Amount;
        }

        if (applied > amountDue)
        {
            return Fail("The payment total is more than the amount due.", amountDue);
        }

        var remaining = amountDue - applied;
        var isFull = applied == amountDue;
        return new PaymentValidation(
            isFull,
            isFull ? null : "The payment total does not cover the amount due.",
            applied,
            remaining,
            cashApplied,
            electronicApplied,
            change);
    }

    private static PaymentValidation Fail(string error, Money amountDue) =>
        new(false, error, Money.Zero, amountDue, Money.Zero, Money.Zero, Money.Zero);
}
