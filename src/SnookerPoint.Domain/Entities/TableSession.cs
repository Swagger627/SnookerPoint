using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A live (or completed) play session at a table. The session keeps its identity
/// across transfers; each table/rate span is a <see cref="SessionSegment"/> and each
/// pause is a <see cref="SessionPause"/>. Billing policy is snapshotted at start.
/// </summary>
public sealed class TableSession
{
    public int Id { get; set; }

    /// <summary>Unique, sequential, human-facing session number.</summary>
    public int SessionNumber { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Active;

    public CheckoutStatus CheckoutStatus { get; set; } = CheckoutStatus.NotCompleted;

    /// <summary>The table the session is currently at (its latest segment's table).</summary>
    public int CurrentTableId { get; set; }

    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset? FinishUtc { get; set; }

    public string? CustomerLabel { get; set; }
    public string? Note { get; set; }
    public string? ClosingNote { get; set; }

    // --- Billing type snapshot (frozen at start) ---
    /// <summary>Hourly (time × rate) or Fixed (a single agreed charge).</summary>
    public BillingType BillingType { get; set; } = BillingType.Hourly;

    /// <summary>The agreed fixed charge when <see cref="BillingType"/> is Fixed; otherwise null.</summary>
    public Money? FixedAmount { get; set; }

    // --- Billing policy snapshot (frozen at start; used by Hourly billing) ---
    public BillingMethod BillingMethod { get; set; }
    public int RoundingIncrementMinutes { get; set; }
    public int MinimumBillableMinutes { get; set; }
    public int GracePeriodMinutes { get; set; }

    // --- Actors ---
    public int OpenedByUserId { get; set; }
    public int OpenedShiftId { get; set; }
    public int? FinishedByUserId { get; set; }
    public int? FinishedShiftId { get; set; }

    // --- Frozen result (set on finish) ---
    public Money? FinalCharge { get; set; }
    public long? FinalBillableSeconds { get; set; }

    // --- Void ---
    public string? VoidReason { get; set; }
    public int? VoidedByUserId { get; set; }
    public DateTimeOffset? VoidedUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public List<SessionSegment> Segments { get; set; } = new();
    public List<SessionPause> Pauses { get; set; } = new();
    public List<SessionAdjustment> Adjustments { get; set; } = new();

    public BillingPolicy Policy =>
        new(BillingMethod, RoundingIncrementMinutes, MinimumBillableMinutes, GracePeriodMinutes);

    public bool IsLive => Status is SessionStatus.Active or SessionStatus.Paused;
}
