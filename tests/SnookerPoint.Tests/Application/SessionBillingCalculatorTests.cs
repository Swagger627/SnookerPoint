using SnookerPoint.Application.Billing;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Tests.Application;

public class SessionBillingCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly SessionBillingCalculator _calc = new();

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private static SegmentTiming Seg(long ratePaisa, double startMin, double? endMin) =>
        new(Money.FromPaisa(ratePaisa), At(startMin), endMin is { } e ? At(e) : null);

    private static PauseInterval Pause(double startMin, double? endMin) =>
        new(At(startMin), endMin is { } e ? At(e) : null);

    private static BillingPolicy Exact(int minMinutes = 0, int graceMinutes = 0) =>
        new(BillingMethod.Exact, 5, minMinutes, graceMinutes);

    private static BillingPolicy RoundUp(int increment, int minMinutes = 0, int graceMinutes = 0) =>
        new(BillingMethod.RoundUp, increment, minMinutes, graceMinutes);

    private SessionCharge One(BillingPolicy policy, long ratePaisa, double activeMinutes) =>
        _calc.Calculate(policy, new[] { Seg(ratePaisa, 0, activeMinutes) }, Array.Empty<PauseInterval>(), At(activeMinutes));

    [Fact]
    public void ExactTime_OneHour_ChargesTheHourlyRate()
    {
        var result = One(Exact(), 12_000, 60);
        Assert.Equal(12_000, result.Charge.Paisa);
        Assert.Equal(3600, result.ActiveSeconds);
    }

    [Fact]
    public void ExactTime_HalfHour_ChargesHalf()
    {
        Assert.Equal(6_000, One(Exact(), 12_000, 30).Charge.Paisa);
    }

    [Theory]
    [InlineData(5, 13_000)]   // 61 → 65 min
    [InlineData(10, 14_000)]  // 61 → 70 min
    [InlineData(15, 15_000)]  // 61 → 75 min
    [InlineData(30, 18_000)]  // 61 → 90 min
    [InlineData(20, 16_000)]  // custom: 61 → 80 min
    public void RoundUp_RoundsChargeableDurationUp(int increment, long expectedPaisa)
    {
        var result = One(RoundUp(increment), 12_000, 61);
        Assert.Equal(expectedPaisa, result.Charge.Paisa);
    }

    [Fact]
    public void GracePeriod_IsAppliedFirst()
    {
        // 65 active minutes, 5 minute grace → 60 billable minutes.
        var result = One(Exact(graceMinutes: 5), 12_000, 65);
        Assert.Equal(12_000, result.Charge.Paisa);
        Assert.Equal(3600, result.BillableSeconds);
    }

    [Fact]
    public void Minimum_RaisesShortSessions()
    {
        // 10 active minutes, 30 minute minimum → charge for 30 minutes.
        var result = One(Exact(minMinutes: 30), 12_000, 10);
        Assert.Equal(6_000, result.Charge.Paisa);
        Assert.Equal(1800, result.BillableSeconds);
    }

    [Fact]
    public void Order_IsGraceThenMinimumThenRounding()
    {
        // 3 active minutes; grace 5 → 0; minimum 10 → 10 min; roundup 5 → 10 min.
        var result = _calc.Calculate(
            RoundUp(5, minMinutes: 10, graceMinutes: 5),
            new[] { Seg(12_000, 0, 3) },
            Array.Empty<PauseInterval>(),
            At(3));
        Assert.Equal(600, result.BillableSeconds);
        Assert.Equal(2_000, result.Charge.Paisa); // 10/60 * 12000
    }

    [Fact]
    public void PaisaRounding_IsNearestAwayFromZero()
    {
        // Rs 100/hr for 1 minute = 166.67 paisa → 167.
        var result = One(Exact(), 10_000, 1);
        Assert.Equal(167, result.Charge.Paisa);
    }

    [Fact]
    public void ZeroDuration_ChargesNothing()
    {
        var result = _calc.Calculate(Exact(), new[] { Seg(12_000, 0, 0) }, Array.Empty<PauseInterval>(), At(0));
        Assert.Equal(0, result.Charge.Paisa);
        Assert.Equal(0, result.ActiveSeconds);
    }

    [Fact]
    public void ZeroActive_WithMinimum_ChargesMinimumAtRate()
    {
        var result = _calc.Calculate(Exact(minMinutes: 15), new[] { Seg(12_000, 0, 0) }, Array.Empty<PauseInterval>(), At(0));
        Assert.Equal(3_000, result.Charge.Paisa); // 15/60 * 12000
    }

    [Fact]
    public void Pause_ExcludesPausedTime()
    {
        // 60 min segment with a 10 min pause → 50 billable minutes.
        var result = _calc.Calculate(Exact(), new[] { Seg(12_000, 0, 60) }, new[] { Pause(20, 30) }, At(60));
        Assert.Equal(10_000, result.Charge.Paisa);
        Assert.Equal(3000, result.ActiveSeconds);
        Assert.Equal(600, result.PausedSeconds);
    }

    [Fact]
    public void MultiplePauses_SumCorrectly()
    {
        var result = _calc.Calculate(
            Exact(),
            new[] { Seg(12_000, 0, 60) },
            new[] { Pause(10, 15), Pause(30, 40) }, // 5 + 10 = 15 min paused
            At(60));
        Assert.Equal(2700, result.ActiveSeconds); // 45 min
        Assert.Equal(9_000, result.Charge.Paisa);
    }

    [Fact]
    public void MultipleRates_AfterTransfer_ChargeEachSegmentAtItsRate()
    {
        // 30 min at Rs120 + 30 min at Rs240 = 6000 + 12000 = 18000.
        var result = _calc.Calculate(
            Exact(),
            new[] { Seg(12_000, 0, 30), Seg(24_000, 30, 60) },
            Array.Empty<PauseInterval>(),
            At(60));
        Assert.Equal(18_000, result.Charge.Paisa);
        Assert.Equal(3600, result.ActiveSeconds);
    }

    [Fact]
    public void LiveSession_UsesAsOfForOpenSegment()
    {
        // Open segment (no end); asOf 45 min in.
        var result = _calc.Calculate(Exact(), new[] { Seg(12_000, 0, null) }, Array.Empty<PauseInterval>(), At(45));
        Assert.Equal(2700, result.ActiveSeconds);
        Assert.Equal(9_000, result.Charge.Paisa);
    }
}
