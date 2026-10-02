namespace LedMatrixOS.Core.Scheduling;

/// <summary>
/// A playlist is an ordered sequence of apps to display, each for a duration with optional settings overrides and transitions.
/// </summary>
public sealed record PlaylistEntry(
    string AppId,
    TimeSpan Duration,
    Dictionary<string, string>? SettingsOverride = null,
    string? TransitionId = null
);

/// <summary>
/// A playlist is rotated automatically: play app 1 for duration 1, then app 2 for duration 2, etc.
/// Example: Clock 30s → Weather 15s → Spotify while playing → (repeat).
/// </summary>
public sealed class PlaylistConfig
{
    public required string Name { get; set; }
    public required List<PlaylistEntry> Entries { get; set; }

    /// <summary>If true, skip an app if it's not available (e.g. Spotify playing when it's not actually playing).</summary>
    public bool SkipUnavailable { get; set; } = false;
}
