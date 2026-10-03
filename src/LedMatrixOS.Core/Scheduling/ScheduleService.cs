namespace LedMatrixOS.Core.Scheduling;

/// <summary>
/// Manages playlists and schedule rules. Evaluates the current time and context to determine
/// which playlist should be active and which app within it should be displayed.
/// </summary>
public sealed class ScheduleService
{
    private readonly Dictionary<string, PlaylistConfig> _playlists = new();
    private readonly List<ScheduleRule> _rules = new();
    private string? _activePlaylistId;
    private int _activeEntryIndex = 0;
    private DateTimeOffset _entryStartTime = DateTimeOffset.UtcNow;
    private readonly TimeProvider _timeProvider;

    public ScheduleService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Callers that mutate the schedule (reload) while the runner reads it lock on this.</summary>
    public object Gate { get; } = new();

    public void Clear()
    {
        _playlists.Clear();
        _rules.Clear();
        _activePlaylistId = null;
        _activeEntryIndex = 0;
    }

    /// <summary>Add a playlist to the scheduler. Later calls override earlier ones.</summary>
    public void RegisterPlaylist(PlaylistConfig config)
    {
        _playlists[config.Name] = config;
    }

    /// <summary>Add a schedule rule. Rules are evaluated in priority order (highest first).</summary>
    public void AddRule(ScheduleRule rule)
    {
        _rules.Add(rule);
        _rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>Load playlists and rules from a JSON file (e.g. next to app-settings.json).</summary>
    public bool TryLoadFromJson(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath)) return false;
            var doc = ScheduleDocument.TryParse(File.ReadAllText(jsonPath), out _);
            if (doc == null) return false;
            doc.ApplyTo(this);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Snapshot of the current playlists and rules in file form.</summary>
    public ScheduleDocument Export() => ScheduleDocument.From(_playlists.Values, _rules);

    /// <summary>
    /// Replaces the whole schedule with the document. Caller should hold <see cref="Gate"/>.
    /// </summary>
    public void Replace(ScheduleDocument doc)
    {
        Clear();
        doc.ApplyTo(this);
    }

    /// <summary>
    /// Where the schedule is right now: matching rule, playlist position and the next moment the answer changes.
    /// Caller should hold <see cref="Gate"/>. Conditional rules are assumed to keep their current state when looking ahead.
    /// </summary>
    public ScheduleStatus GetStatus()
    {
        var appId = GetActiveAppId();
        var now = _timeProvider.GetUtcNow();
        var local = _timeProvider.GetLocalNow().DateTime;
        var rule = _rules.FirstOrDefault(r => r.Matches(local));

        PlaylistConfig? playlist = null;
        if (_activePlaylistId != null) _playlists.TryGetValue(_activePlaylistId, out playlist);

        DateTimeOffset? next = null;
        string? reason = null;

        if (playlist != null && playlist.Entries.Count > 1)
        {
            var end = _entryStartTime;
            for (int i = 0; i <= _activeEntryIndex && i < playlist.Entries.Count; i++) end += playlist.Entries[i].Duration;
            next = end;
            reason = "playlist";
        }

        // Scan forward a minute at a time for the next time the matching rule differs.
        var boundary = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0).AddMinutes(1);
        for (int i = 0; i < 8 * 24 * 60; i++)
        {
            var t = boundary.AddMinutes(i);
            if (!ReferenceEquals(_rules.FirstOrDefault(r => r.Matches(t)), rule))
            {
                var at = now + (t - local);
                if (next == null || at < next) { next = at; reason = "rule"; }
                break;
            }
        }

        return new ScheduleStatus(
            rule == null ? null : new ActiveRuleInfo(_rules.IndexOf(rule), rule.PlaylistId, rule.Priority, rule.Condition),
            playlist?.Name,
            playlist == null ? null : _activeEntryIndex,
            playlist?.Entries.Count,
            appId,
            next,
            reason);
    }

    /// <summary>
    /// Get the currently active app ID. This evaluates the schedule rules and playlist rotation.
    /// Returns null if no playlist is active or no app is available.
    /// </summary>
    public string? GetActiveAppId()
    {
        var now = _timeProvider.GetUtcNow();
        var local = _timeProvider.GetLocalNow().DateTime;

        // Find the active playlist based on current time and rules
        var applicableRule = _rules.FirstOrDefault(r => r.Matches(local));
        var targetPlaylistId = applicableRule?.PlaylistId;

        // If the active playlist changed, restart from index 0
        if (targetPlaylistId != _activePlaylistId)
        {
            _activePlaylistId = targetPlaylistId;
            _activeEntryIndex = 0;
            _entryStartTime = now;
        }

        // If no playlist is active, return null
        if (_activePlaylistId == null || !_playlists.TryGetValue(_activePlaylistId, out var playlist))
            return null;

        // Rotate through entries based on elapsed time
        var elapsed = now - _entryStartTime;
        var cumulativeDuration = TimeSpan.Zero;
        for (int i = 0; i < playlist.Entries.Count; i++)
        {
            cumulativeDuration += playlist.Entries[i].Duration;
            if (elapsed < cumulativeDuration)
            {
                _activeEntryIndex = i;
                return playlist.Entries[i].AppId;
            }
        }

        // Loop back to start
        _activeEntryIndex = 0;
        _entryStartTime = now;
        return playlist.Entries.Count > 0 ? playlist.Entries[0].AppId : null;
    }

    /// <summary>Get the current entry in the active playlist (for transition config, settings overrides, etc).</summary>
    public PlaylistEntry? GetActiveEntry()
    {
        if (_activePlaylistId == null || !_playlists.TryGetValue(_activePlaylistId, out var playlist))
            return null;
        if (_activeEntryIndex >= playlist.Entries.Count) return null;
        return playlist.Entries[_activeEntryIndex];
    }

    /// <summary>Get the active brightness override, if any.</summary>
    public byte? GetActiveBrightnessOverride()
    {
        var local = _timeProvider.GetLocalNow().DateTime;
        return _rules.FirstOrDefault(r => r.Matches(local))?.BrightnessOverride;
    }
}

public sealed record ActiveRuleInfo(int Index, string PlaylistId, int Priority, string? Condition);

public sealed record ScheduleStatus(
    ActiveRuleInfo? ActiveRule,
    string? Playlist,
    int? EntryIndex,
    int? EntryCount,
    string? AppId,
    DateTimeOffset? NextChange,
    string? NextChangeReason);
