using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A sale: a walk-in store sale or the checkout of a completed table session. Persisted
/// from creation (as a Draft) so an in-progress cart survives a crash. Once Completed it
/// is an immutable financial record — never silently edited or deleted. Money is stored
/// as integer minor units; the table charge is imported frozen from the session and kept
/// separate from product lines so it can't be silently removed.
/// </summary>
public sealed class Sale
{
    public int Id { get; set; }

    /// <summary>Unique, sequential, human-facing number — assigned only when Completed.</summary>
    public int? SaleNumber { get; set; }

    public SaleType Type { get; set; } = SaleType.Walkin;

    public SaleStatus Status { get; set; } = SaleStatus.Draft;

    /// <summary>Optional label for a held draft, or a walk-in note.</summary>
    public string? Label { get; set; }

    // --- Table link (Table sales only) ---
    public int? TableSessionId { get; set; }

    /// <summary>The frozen table charge imported from the session (never recalculated here).</summary>
    public Money TableCharge { get; set; } = Money.Zero;

    /// <summary>Snapshot of the session's billing type, for history.</summary>
    public BillingType? TableBillingType { get; set; }

    // --- Discount (sale-level, optional) ---
    public DiscountKind DiscountKind { get; set; } = DiscountKind.None;

    /// <summary>The configured discount input: a rupee amount (Fixed) or a percent value 0–100 (Percentage).</summary>
    public long DiscountValue { get; set; }

    /// <summary>The resolved discount applied to the total.</summary>
    public Money DiscountAmount { get; set; } = Money.Zero;

    public string? DiscountReason { get; set; }

    // --- Tax / service (disabled by default; 0 unless configured) ---
    public Money TaxAmount { get; set; } = Money.Zero;
    public Money ServiceAmount { get; set; } = Money.Zero;

    // --- Frozen totals (set at completion) ---
    public Money Subtotal { get; set; } = Money.Zero;
    public Money Total { get; set; } = Money.Zero;

    // --- Cash summary (across cash payment portions) ---
    public Money? CashReceived { get; set; }
    public Money? ChangeGiven { get; set; }

    // --- Actors / shift ---
    public int CreatedByUserId { get; set; }
    public int? CompletedByUserId { get; set; }
    public int? ShiftId { get; set; }

    // --- Receipt ---
    /// <summary>Immutable receipt text snapshot rendered at completion.</summary>
    public string? ReceiptSnapshot { get; set; }
    public int PrintCount { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }

    public List<SaleLine> Lines { get; set; } = new();
    public List<SalePayment> Payments { get; set; } = new();
}
