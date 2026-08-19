using SnookerPoint.Domain.Sessions;
using Interval = SnookerPoint.Domain.Sessions.SessionTimelineValidator.Interval;

namespace SnookerPoint.Tests.Domain;

public class SessionTimelineValidatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private static IReadOnlyList<string> Validate(
        DateTimeOffset start, DateTimeOffset? finish, Interval[] segments, Interval[] pauses) =>
        SessionTimelineValidator.Validate(start, finish, segments, pauses, At(120));

    [Fact]
    public void ValidTimeline_HasNoErrors()
    {
        var errors = Validate(
            At(0), At(60),
            new[] { new Interval(At(0), At(60)) },
            new[] { new Interval(At(10), At(20)) });
        Assert.Empty(errors);
    }

    [Fact]
    public void StartAfterFinish_IsRejected()
    {
        var errors = Validate(At(70), At(60), new[] { new Interval(At(70), At(60)) }, Array.Empty<Interval>());
        Assert.Contains(errors, e => e.Contains("after the finish"));
    }

    [Fact]
    public void PauseBeforeStart_IsRejected()
    {
        var errors = Validate(At(0), At(60), new[] { new Interval(At(0), At(60)) },
            new[] { new Interval(At(-5), At(10)) });
        Assert.Contains(errors, e => e.Contains("before the session starts"));
    }

    [Fact]
    public void PauseEndBeforeStart_IsRejected()
    {
        var errors = Validate(At(0), At(60), new[] { new Interval(At(0), At(60)) },
            new[] { new Interval(At(20), At(15)) });
        Assert.Contains(errors, e => e.Contains("must end after it begins"));
    }

    [Fact]
    public void OverlappingPauses_AreRejected()
    {
        var errors = Validate(At(0), At(60), new[] { new Interval(At(0), At(60)) },
            new[] { new Interval(At(10), At(30)), new Interval(At(20), At(40)) });
        Assert.Contains(errors, e => e.Contains("overlap"));
    }

    [Fact]
    public void PauseAfterFinish_IsRejected()
    {
        var errors = Validate(At(0), At(60), new[] { new Interval(At(0), At(60)) },
            new[] { new Interval(At(70), At(80)) });
        Assert.Contains(errors, e => e.Contains("past the session end"));
    }

    [Fact]
    public void SegmentBeyondFinish_IsRejected()
    {
        var errors = Validate(At(0), At(60), new[] { new Interval(At(0), At(70)) }, Array.Empty<Interval>());
        Assert.Contains(errors, e => e.Contains("past the session end"));
    }

    [Fact]
    public void PauseCoveringWholeSegment_IsAllowed()
    {
        // Active becomes zero but not negative — this is valid.
        var errors = Validate(At(0), At(60), new[] { new Interval(At(0), At(60)) },
            new[] { new Interval(At(0), At(60)) });
        Assert.Empty(errors);
    }
}
