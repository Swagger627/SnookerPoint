namespace SnookerPoint.App.Services;

/// <summary>Small display helpers for durations and times.</summary>
public static class DisplayFormat
{
    /// <summary>Formats a duration in seconds as e.g. "1:05:09" or "0:03:20".</summary>
    public static string Duration(long seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}";
    }

    /// <summary>Formats a duration in seconds as e.g. "1h 05m" (no seconds).</summary>
    public static string DurationShort(long seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:D2}m" : $"{span.Minutes}m {span.Seconds:D2}s";
    }

    /// <summary>Local wall-clock time, e.g. "3:45 PM".</summary>
    public static string LocalTime(DateTimeOffset utc) => utc.ToLocalTime().ToString("h:mm tt");

    /// <summary>Local date + time, e.g. "29 Jul 2026, 3:45 PM".</summary>
    public static string LocalDateTime(DateTimeOffset utc) => utc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt");
}
