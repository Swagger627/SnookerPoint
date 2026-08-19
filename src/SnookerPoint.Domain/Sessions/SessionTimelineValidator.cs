namespace SnookerPoint.Domain.Sessions;

/// <summary>
/// Pure validation of a session's timeline (start/finish, table/rate segments and
/// pause periods). Used by the correction workflow to reject impossible timelines
/// before anything is persisted. Friendly, non-technical messages.
/// </summary>
public static class SessionTimelineValidator
{
    /// <summary>A time window; a null end means "still open" (bounded by the timeline end).</summary>
    public readonly record struct Interval(DateTimeOffset Start, DateTimeOffset? End);

    public static IReadOnlyList<string> Validate(
        DateTimeOffset start,
        DateTimeOffset? finish,
        IReadOnlyList<Interval> segments,
        IReadOnlyList<Interval> pauses,
        DateTimeOffset now)
    {
        var errors = new List<string>();
        var boundary = finish ?? now;

        if (finish is { } f && start > f)
        {
            errors.Add("The start time cannot be after the finish time.");
        }

        if (start > boundary)
        {
            errors.Add("The start time cannot be after the session end.");
        }

        foreach (var seg in segments)
        {
            var end = seg.End ?? boundary;
            if (seg.Start < start)
            {
                errors.Add("A table period cannot begin before the session starts.");
            }

            if (seg.End is { } se && se <= seg.Start)
            {
                errors.Add("A table period must end after it begins.");
            }

            if (end > boundary || seg.Start > boundary)
            {
                errors.Add("A table period cannot extend past the session end.");
            }
        }

        foreach (var pause in pauses)
        {
            if (pause.Start < start)
            {
                errors.Add("A pause cannot begin before the session starts.");
            }

            if (pause.End is { } pe && pe <= pause.Start)
            {
                errors.Add("A pause must end after it begins.");
            }

            if ((pause.End ?? boundary) > boundary || pause.Start > boundary)
            {
                errors.Add("A pause cannot extend past the session end.");
            }
        }

        // No overlapping pauses.
        var ordered = pauses.OrderBy(p => p.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previousEnd = ordered[i - 1].End ?? boundary;
            if (previousEnd > ordered[i].Start)
            {
                errors.Add("Pause periods cannot overlap.");
                break;
            }
        }

        // No segment may end up with negative playing time.
        foreach (var seg in segments)
        {
            var segEnd = seg.End ?? boundary;
            var wall = segEnd - seg.Start;
            if (wall < TimeSpan.Zero)
            {
                continue;
            }

            var paused = TimeSpan.Zero;
            foreach (var pause in pauses)
            {
                var pauseEnd = pause.End ?? boundary;
                var overlapStart = seg.Start > pause.Start ? seg.Start : pause.Start;
                var overlapEnd = segEnd < pauseEnd ? segEnd : pauseEnd;
                if (overlapEnd > overlapStart)
                {
                    paused += overlapEnd - overlapStart;
                }
            }

            if (paused > wall)
            {
                errors.Add("The correction would produce a negative playing time.");
                break;
            }
        }

        return errors.Distinct().ToList();
    }
}
