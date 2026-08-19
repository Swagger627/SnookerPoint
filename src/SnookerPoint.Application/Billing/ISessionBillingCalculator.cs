using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Billing;

/// <summary>A table/rate span of a session: its rate and its time window.</summary>
/// <param name="Rate">Hourly rate for this segment.</param>
/// <param name="Start">Segment start (UTC).</param>
/// <param name="End">Segment end (UTC); null while it is the current segment.</param>
public readonly record struct SegmentTiming(Money Rate, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>A pause window within a session.</summary>
/// <param name="Start">Pause start (UTC).</param>
/// <param name="End">Resume time (UTC); null while currently paused.</param>
public readonly record struct PauseInterval(DateTimeOffset Start, DateTimeOffset? End);

/// <summary>The computed timing and charge for a session at a point in time.</summary>
public readonly record struct SessionCharge(
    long ElapsedSeconds,
    long PausedSeconds,
    long ActiveSeconds,
    long BillableSeconds,
    Money Charge);

/// <summary>
/// The single, UI-independent billing engine used everywhere a charge is needed:
/// the live dashboard, the finish preview, the frozen final charge, correction
/// recalculation, and (later) checkout. Deterministic and fully unit-tested.
/// </summary>
public interface ISessionBillingCalculator
{
    /// <summary>
    /// Computes elapsed/paused/active/billable seconds and the charge as of the
    /// given instant, correctly accounting for multiple rate segments and pauses.
    /// </summary>
    SessionCharge Calculate(
        BillingPolicy policy,
        IReadOnlyList<SegmentTiming> segments,
        IReadOnlyList<PauseInterval> pauses,
        DateTimeOffset asOf);
}
