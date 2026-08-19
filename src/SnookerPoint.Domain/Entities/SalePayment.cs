using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// One payment portion of a sale. A sale may have several (a split payment). The applied
/// <see cref="Amount"/> is the portion of the bill this payment covers; for cash, the
/// amount handed over (<see cref="ReceivedAmount"/>) and the <see cref="ChangeAmount"/> are
/// also recorded. Change is never revenue, and electronic payments never affect the drawer.
/// </summary>
public sealed class SalePayment
{
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int MethodId { get; set; }

    /// <summary>Method name snapshot, for immutable history.</summary>
    public string MethodNameSnapshot { get; set; } = string.Empty;

    public PaymentMethodKind Kind { get; set; }

    /// <summary>The portion of the bill this payment covers.</summary>
    public Money Amount { get; set; } = Money.Zero;

    /// <summary>Cash handed over (cash only).</summary>
    public Money? ReceivedAmount { get; set; }

    /// <summary>Change returned (cash only).</summary>
    public Money? ChangeAmount { get; set; }

    /// <summary>Optional transaction/reference number (stored as text).</summary>
    public string? Reference { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset Utc { get; set; }
}
