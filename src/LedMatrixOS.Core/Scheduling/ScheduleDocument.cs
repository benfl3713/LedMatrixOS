using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedMatrixOS.Core.Scheduling;

/// <summary>Wire/file shape of schedule.json (also the body of PUT /api/schedule).</summary>
public sealed class ScheduleDocument
{
    public List<PlaylistDocument> Playlists { get; set; } = new();
    public List<RuleDocument> Rules { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Parses a document; returns null and an error message when the JSON is malformed.</summary>
    public static ScheduleDocument? TryParse(string json, out string? error)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<ScheduleDocument>(json, Options);
            if (doc == null) { error = "Body must be a JSON object"; return null; }
            error = null;
            return doc;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Validates the document. appExists / transitionExists are optional so Core can be used without the app registry.
    /// Returns a list of human readable problems; empty means valid.
    /// </summary>
    public List<string> Validate(Func<string, bool>? appExists = null, Func<string, bool>? transitionExists = null)
    {
        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < Playlists.Count; i++)
        {
            var p = Playlists[i];
            var where = $"playlists[{i}]";
            if (p == null) { errors.Add($"{where} is null"); continue; }
            if (string.IsNullOrWhiteSpace(p.Name)) errors.Add($"{where}.name is required");
            else if (!names.Add(p.Name)) errors.Add($"{where}.name '{p.Name}' is duplicated");

            if (p.Entries == null || p.Entries.Count == 0) { errors.Add($"{where}.entries must contain at least one entry"); continue; }
            for (int j = 0; j < p.Entries.Count; j++)
            {
                var e = p.Entries[j];
                var ew = $"{where}.entries[{j}]";
                if (e == null) { errors.Add($"{ew} is null"); continue; }
                if (string.IsNullOrWhiteSpace(e.AppId)) errors.Add($"{ew}.appId is required");
                else if (appExists != null && !appExists(e.AppId)) errors.Add($"{ew}.appId '{e.AppId}' is not a registered app");
                if (e.DurationMs <= 0) errors.Add($"{ew}.durationMs must be greater than 0");
                if (!string.IsNullOrEmpty(e.Transition) && transitionExists != null && !transitionExists(e.Transition))
                    errors.Add($"{ew}.transition '{e.Transition}' is not a known transition");
            }
        }

        for (int i = 0; i < Rules.Count; i++)
        {
            var r = Rules[i];
            var where = $"rules[{i}]";
            if (r == null) { errors.Add($"{where} is null"); continue; }
            if (string.IsNullOrWhiteSpace(r.PlaylistId)) errors.Add($"{where}.playlistId is required");
            else if (!names.Contains(r.PlaylistId)) errors.Add($"{where}.playlistId '{r.PlaylistId}' does not match any playlist");
            if (r.StartTime != null && !TryParseTime(r.StartTime, out _)) errors.Add($"{where}.startTime must be HH:mm");
            if (r.EndTime != null && !TryParseTime(r.EndTime, out _)) errors.Add($"{where}.endTime must be HH:mm");
            if (r.DaysMask is < 1 or > 127) errors.Add($"{where}.daysMask must be between 1 and 127");
            if (r.BrightnessOverride is < 0 or > 255) errors.Add($"{where}.brightnessOverride must be between 0 and 255");
            if (r.Condition != null && !AttentionCondition.TryParse(r.Condition, out _, out var condError))
                errors.Add($"{where}.condition: {condError}");
        }

        return errors;
    }

    /// <summary>Adds this document's playlists and rules to the service (caller clears first when replacing).</summary>
    public void ApplyTo(ScheduleService service)
    {
        foreach (var p in Playlists)
        {
            service.RegisterPlaylist(new PlaylistConfig
            {
                Name = p.Name,
                SkipUnavailable = p.SkipUnavailable,
                Entries = p.Entries.Select(e => new PlaylistEntry(
                    e.AppId,
                    TimeSpan.FromMilliseconds(e.DurationMs),
                    e.Settings?.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.GetRawText()),
                    e.Transition)).ToList(),
            });
        }

        foreach (var r in Rules)
        {
            var rule = new ScheduleRule
            {
                PlaylistId = r.PlaylistId,
                Priority = r.Priority,
                ActiveDaysMask = r.DaysMask,
                Condition = string.IsNullOrWhiteSpace(r.Condition) ? null : r.Condition,
                BrightnessOverride = r.BrightnessOverride is { } b ? (byte)b : null,
            };
            if (r.StartTime != null && TryParseTime(r.StartTime, out var st)) rule.StartTime = st;
            if (r.EndTime != null && TryParseTime(r.EndTime, out var et)) rule.EndTime = et;
            service.AddRule(rule);
        }
    }

    /// <summary>Builds a document from the service's current state.</summary>
    public static ScheduleDocument From(IEnumerable<PlaylistConfig> playlists, IEnumerable<ScheduleRule> rules) => new()
    {
        Playlists = playlists.Select(p => new PlaylistDocument
        {
            Name = p.Name,
            SkipUnavailable = p.SkipUnavailable,
            Entries = p.Entries.Select(e => new EntryDocument
            {
                AppId = e.AppId,
                DurationMs = (int)e.Duration.TotalMilliseconds,
                Transition = e.TransitionId,
                Settings = e.SettingsOverride?.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value)),
            }).ToList(),
        }).ToList(),
        Rules = rules.Select(r => new RuleDocument
        {
            PlaylistId = r.PlaylistId,
            Priority = r.Priority,
            DaysMask = r.ActiveDaysMask,
            StartTime = r.StartTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            EndTime = r.EndTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            BrightnessOverride = r.BrightnessOverride,
            Condition = r.Condition,
        }).ToList(),
    };

    internal static bool TryParseTime(string text, out TimeSpan value) =>
        TimeSpan.TryParseExact(text, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out value)
        && value >= TimeSpan.Zero && value < TimeSpan.FromHours(24);

    /// <summary>Writes the file atomically: temp file in the same directory, then replace.</summary>
    public static void WriteAtomic(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

public sealed class PlaylistDocument
{
    public string Name { get; set; } = "";
    public bool SkipUnavailable { get; set; }
    public List<EntryDocument> Entries { get; set; } = new();
}

public sealed class EntryDocument
{
    public string AppId { get; set; } = "";
    public int DurationMs { get; set; }
    public Dictionary<string, JsonElement>? Settings { get; set; }
    public string? Transition { get; set; }
}

public sealed class RuleDocument
{
    public string PlaylistId { get; set; } = "";
    public int Priority { get; set; } = 50;
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public int DaysMask { get; set; } = 127;
    public int? BrightnessOverride { get; set; }
    public string? Condition { get; set; }
}
