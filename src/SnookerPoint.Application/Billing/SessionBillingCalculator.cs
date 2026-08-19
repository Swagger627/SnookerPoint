using SnookerPoint.Domain.Billing;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Billing;

/// <summary>
/// Deterministic implementation of <see cref="ISessionBillingCalculator"/>.
///
/// Timing: for each segment, active seconds = wall-clock seconds − paused overlap.
/// All durations are computed from integer ticks (no binary floating-point).
///
/// Charging (order): grace → minimum → rounding produce the chargeable seconds
/// (see <see cref="BillingMath"/>). The chargeable duration is then distributed
/// across segments in proportion to each segment's active seconds and charged at
/// that segment's rate, so time spent at different rates is billed correctly while
/// the policy (grace/minimum/rounding) is applied once for the whole session. The
/// proportional step uses <see cref="decimal"/> and rounds to the nearest paisa.
/// </summary>
public sealed class SessionBillingCalculator : ISessionBillingCalculator
{
    private const decimal SecondsPerHour = 3600m;

    public SessionCharge Calculate(
        BillingPolicy policy,
        IReadOnlyList<SegmentTiming> segments,
        IReadOnlyList<PauseInterval> pauses,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(pauses);

        long elapsed = 0;
        long activeTotal = 0;
        var activePerSegment = new long[segments.Count];

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var segEnd = seg.End ?? asOf;
            var wall = WholeSeconds(seg.Start, segEnd);

            long pausedInSeg = 0;
            foreach (var pause in pauses)
            {
                var pauseEnd = pause.End ?? asOf;
                pausedInSeg += OverlapSeconds(pause.Start, pauseEnd, seg.Start, segEnd);
            }

            var active = Math.Max(0, wall - pausedInSeg);
            activePerSegment[i] = active;
            activeTotal += active;
            elapsed += wall;
        }

        long pausedTotal = 0;
        foreach (var pause in pauses)
        {
            pausedTotal += WholeSeconds(pause.Start, pause.End ?? asOf);
        }

        var chargeableSeconds = BillingMath.ChargeableSeconds(activeTotal, policy);
        var charge = AllocateCharge(segments, activePerSegment, activeTotal, chargeableSeconds);

        return new SessionCharge(elapsed, pausedTotal, activeTotal, chargeableSeconds, charge);
    }

    private static Money AllocateCharge(
        IReadOnlyList<SegmentTiming> segments,
        long[] activePerSegment,
        long activeTotal,
        long chargeableSeconds)
    {
        if (chargeableSeconds <= 0)
        {
            return Money.Zero;
        }

        if (activeTotal <= 0)
        {
            // No active time but a minimum was forced: charge it at the last known rate.
            return segments.Count > 0
                ? BillingMath.ChargeForDurationAtRate(chargeableSeconds, segments[^1].Rate)
                : Money.Zero;
        }

        decimal totalPaisa = 0m;
        for (var i = 0; i < segments.Count; i++)
        {
            if (activePerSegment[i] == 0)
            {
                continue;
            }

            var segmentSeconds = (decimal)chargeableSeconds * activePerSegment[i] / activeTotal;
            totalPaisa += segmentSeconds / SecondsPerHour * segments[i].Rate.Paisa;
        }

        return Money.FromPaisa((long)decimal.Round(totalPaisa, 0, MidpointRounding.AwayFromZero));
    }

    private static long WholeSeconds(DateTimeOffset start, DateTimeOffset end)
    {
        var ticks = (end - start).Ticks;
        return ticks <= 0 ? 0 : ticks / TimeSpan.TicksPerSecond;
    }

    private static long OverlapSeconds(
        DateTimeOffset aStart, DateTimeOffset aEnd,
        DateTimeOffset bStart, DateTimeOffset bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return WholeSeconds(start, end);
    }
}
