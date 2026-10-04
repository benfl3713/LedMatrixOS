namespace LedMatrixOS.Core.Scheduling;

/// <summary>
/// A rule that determines if a playlist should be active based on time and context.
/// Example: "Mon–Fri 07:15–08:45 → 'Commute' playlist" or "23:00–07:00 → dim + clock".
/// </summary>
public sealed class ScheduleRule
{
    public required string PlaylistId { get; set; }

    /// <summary>Which days the rule applies (bitmask: Sun=1, Mon=2, Tue=4, ..., Sat=64).</summary>
    public int ActiveDaysMask { get; set; } = 127; // All days by default

    /// <summary>Start time (HH:MM). Null = midnight (00:00).</summary>
    public TimeSpan? StartTime { get; set; }

    /// <summary>End time (HH:MM). Null = midnight (23:59, wraps to next day if needed).</summary>
    public TimeSpan? EndTime { get; set; }

    /// <summary>
    /// Optional context trigger, evaluated by <see cref="AttentionEvaluator"/> on top of the time window:
    /// "spotify_playing", "line_disrupted:&lt;lineId&gt;", "bus_due:&lt;stopId&gt;" or "ha_state:&lt;entity&gt;=&lt;value&gt;".
    /// A rule whose condition has no registered source never matches.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>Brightness to set when this rule is active (0-255). Null = no change.</summary>
    public byte? BrightnessOverride { get; set; }

    /// <summary>Priority for conflict resolution (higher = more urgent). Default 50.</summary>
    public int Priority { get; set; } = 50;

    public bool Matches(DateTime now)
    {
        var dayOfWeek = (int)now.DayOfWeek;
        var mask = 1 << dayOfWeek;
        if ((ActiveDaysMask & mask) == 0) return false;

        var time = now.TimeOfDay;
        if (!StartTime.HasValue && !EndTime.HasValue) return true;

        if (EndTime.HasValue && EndTime < StartTime)
        {
            // Rule wraps midnight: active from StartTime to 23:59, and 00:00 to EndTime
            return time >= StartTime || time < EndTime;
        }

        // Non-wrapping range
        if (StartTime.HasValue && time < StartTime) return false;
        if (EndTime.HasValue && time >= EndTime) return false;
        return true;
    }
}
