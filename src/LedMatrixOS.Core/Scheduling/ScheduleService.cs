using System.Text.Json;

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
            var json = File.ReadAllText(jsonPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("playlists", out var playlistsElem))
            {
                foreach (var p in playlistsElem.EnumerateArray())
                {
                    var name = p.GetProperty("name").GetString() ?? "unnamed";
                    var entries = new List<PlaylistEntry>();
                    foreach (var e in p.GetProperty("entries").EnumerateArray())
                    {
                        var appId = e.GetProperty("appId").GetString() ?? "";
                        var durationMs = e.GetProperty("durationMs").GetInt32();
                        var duration = TimeSpan.FromMilliseconds(durationMs);
                        var entry = new PlaylistEntry(appId, duration);
                        entries.Add(entry);
                    }
                    RegisterPlaylist(new PlaylistConfig { Name = name, Entries = entries });
                }
            }

            if (root.TryGetProperty("rules", out var rulesElem))
            {
                foreach (var r in rulesElem.EnumerateArray())
                {
                    var rule = new ScheduleRule
                    {
                        PlaylistId = r.GetProperty("playlistId").GetString() ?? "",
                        Priority = r.GetProperty("priority").GetInt32(),
                    };
                    if (r.TryGetProperty("startTime", out var st))
                        rule.StartTime = TimeSpan.Parse(st.GetString() ?? "00:00");
                    if (r.TryGetProperty("endTime", out var et))
                        rule.EndTime = TimeSpan.Parse(et.GetString() ?? "23:59");
                    if (r.TryGetProperty("daysMask", out var dm))
                        rule.ActiveDaysMask = dm.GetInt32();
                    if (r.TryGetProperty("brightnessOverride", out var bo))
                        rule.BrightnessOverride = (byte)bo.GetInt32();
                    AddRule(rule);
                }
            }

            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Get the currently active app ID. This evaluates the schedule rules and playlist rotation.
    /// Returns null if no playlist is active or no app is available.
    /// </summary>
    public string? GetActiveAppId()
    {
        var now = _timeProvider.GetUtcNow();

        // Find the active playlist based on current time and rules
        var applicableRule = _rules.FirstOrDefault(r => r.Matches(now.DateTime));
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
        var now = _timeProvider.GetUtcNow();
        return _rules.FirstOrDefault(r => r.Matches(now.DateTime))?.BrightnessOverride;
    }
}
