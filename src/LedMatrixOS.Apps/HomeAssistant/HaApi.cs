using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LedMatrixOS.Apps.HomeAssistant;

internal sealed record HaState(string EntityId, string State, string? Unit, string? FriendlyName, string? DeviceClass = null);

/// <summary>Per-entity options from the third part of the entity syntax ("sensor.temp|Living|spark").</summary>
[Flags]
internal enum TileFlags { None = 0, Icon = 1, Spark = 2 }

/// <summary>An entity the user asked for, with an optional label and flags ("sensor.lounge_temp|Lounge|spark").</summary>
internal sealed record EntityRef(string EntityId, string? Label, TileFlags Flags = TileFlags.None);

internal enum TileKind { Number, On, Off, Text, Unavailable, Warm }

internal enum Glyph { None, Bulb, Plug, DoorClosed, DoorOpen, Motion, LockClosed, LockOpen }

/// <summary>What one tile shows, already formatted (built only when the data or entity list changes). <see cref="Series"/> is the optional 24h history.</summary>
internal sealed record TileData(string Label, string Value, string Unit, TileKind Kind, Glyph Glyph = Glyph.None, float[]? Series = null)
{
    public bool Equals(TileData? other) =>
        other is not null && Label == other.Label && Value == other.Value && Unit == other.Unit && Kind == other.Kind && Glyph == other.Glyph
        && (ReferenceEquals(Series, other.Series) || (Series is not null && other.Series is not null && Series.AsSpan().SequenceEqual(other.Series)));

    public override int GetHashCode() => HashCode.Combine(Label, Value, Unit, Kind, Glyph, Series?.Length);
}

internal static class HaFormat
{
    /// <summary>
    /// Parses "id|Label|flag|flag, id2, ..." (separators: comma, semicolon, newline). Label and flags are optional; flags are
    /// <c>icon</c> and <c>spark</c> (joined with '|', '+' or a space) and unknown flags are ignored.
    /// </summary>
    public static List<EntityRef> ParseEntities(string? text)
    {
        var list = new List<EntityRef>();
        foreach (var part in (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bar = part.IndexOf('|');
            var id = (bar < 0 ? part : part[..bar]).Trim();
            string? label = null;
            var flags = TileFlags.None;
            if (bar >= 0)
            {
                var rest = part[(bar + 1)..].Split('|');
                label = rest[0].Trim();
                for (int i = 1; i < rest.Length; i++)
                    foreach (var flag in rest[i].Split(['+', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        flags |= flag.ToLowerInvariant() switch { "icon" or "glyph" => TileFlags.Icon, "spark" or "sparkline" or "history" => TileFlags.Spark, _ => TileFlags.None };
            }
            if (id.Contains('.') && id.Length > 2) list.Add(new EntityRef(id, string.IsNullOrEmpty(label) ? null : label, flags));
        }
        return list;
    }

    public static HaState ParseState(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? unit = null, name = null, deviceClass = null;
        if (root.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            if (attrs.TryGetProperty("unit_of_measurement", out var u) && u.ValueKind == JsonValueKind.String) unit = u.GetString();
            if (attrs.TryGetProperty("friendly_name", out var f) && f.ValueKind == JsonValueKind.String) name = f.GetString();
            if (attrs.TryGetProperty("device_class", out var d) && d.ValueKind == JsonValueKind.String) deviceClass = d.GetString();
        }
        return new HaState(root.GetProperty("entity_id").GetString() ?? "", root.GetProperty("state").GetString() ?? "", unit, name, deviceClass);
    }

    /// <summary>
    /// Parses one entity's history (the "minimal_response" list of state changes) into <paramref name="buckets"/> evenly spaced samples
    /// across the window ending at <paramref name="end"/>. Each bucket holds the last numeric value seen in it, carrying the previous
    /// value forward over quiet spells; non-numeric states are skipped. Returns null when there is no numeric data.
    /// </summary>
    public static float[]? ParseHistory(string json, DateTimeOffset end, TimeSpan window, int buckets = 48)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
        var series = root[0];
        if (series.ValueKind != JsonValueKind.Array) return null;

        var start = end - window;
        var step = window / buckets;
        var values = new float?[buckets];
        float? initial = null;
        foreach (var item in series.EnumerateArray())
        {
            if (!item.TryGetProperty("state", out var st) || st.ValueKind != JsonValueKind.String) continue;
            if (!float.TryParse(st.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !float.IsFinite(v)) continue;

            var when = start;
            if (item.TryGetProperty("last_changed", out var lc) && lc.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(lc.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                when = parsed;

            if (when <= start) { initial = v; continue; }
            int index = (int)((when - start) / step);
            if (index >= 0 && index < buckets) values[index] = v;
        }

        // Carry forward over quiet spells; leading gaps take the first value that appears.
        float? last = initial;
        for (int i = 0; i < buckets; i++)
        {
            if (values[i] is { } v) last = v;
            else values[i] = last;
        }
        float? first = values.FirstOrDefault(x => x.HasValue);
        if (first is null) return null;
        var result = new float[buckets];
        for (int i = 0; i < buckets; i++) result[i] = values[i] ?? first.Value;
        return result;
    }

    public static Glyph GlyphFor(EntityRef entity, HaState state, bool on)
    {
        var domain = entity.EntityId[..entity.EntityId.IndexOf('.')];
        var id = entity.EntityId.ToLowerInvariant();
        var dc = state.DeviceClass?.ToLowerInvariant() ?? "";
        switch (domain)
        {
            case "light": return Glyph.Bulb;
            case "switch" or "input_boolean" or "fan": return Glyph.Plug;
            case "lock": return on ? Glyph.LockOpen : Glyph.LockClosed;   // "on" means unlocked
            case "binary_sensor":
                if (dc is "door" or "garage_door" or "opening" or "window" || id.Contains("door") || id.Contains("window"))
                    return on ? Glyph.DoorOpen : Glyph.DoorClosed;
                if (dc is "motion" or "occupancy" or "presence" or "moving" || id.Contains("motion") || id.Contains("occupancy"))
                    return Glyph.Motion;
                if (dc is "lock") return on ? Glyph.LockOpen : Glyph.LockClosed;
                return Glyph.None;
            default: return Glyph.None;
        }
    }

    public static TileData ToTile(EntityRef entity, HaState? state, float[]? series = null)
    {
        var label = entity.Label ?? state?.FriendlyName ?? entity.EntityId[(entity.EntityId.IndexOf('.') + 1)..].Replace('_', ' ');
        label = label.ToUpperInvariant();
        if (state is null || state.State is "unavailable" or "unknown") return new TileData(label, "N/A", "", TileKind.Unavailable);

        if (double.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return new TileData(label, Math.Round(number, 1).ToString("0.#", CultureInfo.InvariantCulture), state.Unit ?? "", TileKind.Number,
                Series: (entity.Flags & TileFlags.Spark) != 0 && series is { Length: > 1 } ? series : null);

        var lower = state.State.ToLowerInvariant();
        bool on = lower is "on" or "home" or "open" or "playing" or "unlocked";
        bool off = lower is "off" or "not_home" or "closed" or "paused" or "idle" or "locked";
        if ((on || off) && (entity.Flags & TileFlags.Icon) != 0)
        {
            var glyph = GlyphFor(entity, state, on);
            if (glyph != Glyph.None)
            {
                // Tint: lit things and heads-ups (open door, unlocked, motion) are warm, a locked lock is reassuring green.
                var kind = glyph switch
                {
                    Glyph.LockClosed => TileKind.On,
                    Glyph.Plug => on ? TileKind.On : TileKind.Off,
                    Glyph.DoorClosed => TileKind.Off,
                    _ => on ? TileKind.Warm : TileKind.Off,
                };
                return new TileData(label, "", "", kind, glyph);
            }
        }
        if (on) return new TileData(label, state.State.ToUpperInvariant(), "", TileKind.On);
        if (off) return new TileData(label, state.State.Replace('_', ' ').ToUpperInvariant(), "", TileKind.Off);
        return new TileData(label, state.State.Replace('_', ' ').ToUpperInvariant(), "", TileKind.Text);
    }
}

internal sealed class HaApi(HttpClient http)
{
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);

    public string BaseUrl { get; set; } = "";
    public string Token { get; set; } = "";

    /// <summary>Clock for the history window (not render code; overridable for tests).</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public bool IsConfigured => BaseUrl.Length > 0 && Token.Length > 0;

    /// <summary>Fetches every entity; one that fails comes back as null so its tile shows N/A instead of hiding the others.</summary>
    public async Task<HaState?[]> GetStatesAsync(IReadOnlyList<EntityRef> entities, CancellationToken ct) =>
        await Task.WhenAll(entities.Select(e => GetOneAsync(e.EntityId, ct)));

    /// <summary>
    /// Fetches the last 24 hours for each entity that asked for a sparkline. The result lines up with <paramref name="entities"/>;
    /// an entry is null where none was requested or the request failed.
    /// </summary>
    public async Task<float[]?[]> GetHistoryAsync(IReadOnlyList<EntityRef> entities, CancellationToken ct)
    {
        var end = Now();
        return await Task.WhenAll(entities.Select(e => (e.Flags & TileFlags.Spark) == 0 ? Task.FromResult<float[]?>(null) : HistoryOneAsync(e.EntityId, end, ct)));
    }

    private async Task<float[]?> HistoryOneAsync(string entityId, DateTimeOffset end, CancellationToken ct)
    {
        try
        {
            var start = (end - HistoryWindow).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var url = $"{BaseUrl.TrimEnd('/')}/api/history/period/{Uri.EscapeDataString(start)}?filter_entity_id={Uri.EscapeDataString(entityId)}&minimal_response&no_attributes";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? HaFormat.ParseHistory(await response.Content.ReadAsStringAsync(ct), end, HistoryWindow) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<HaState?> GetOneAsync(string entityId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl.TrimEnd('/')}/api/states/{Uri.EscapeDataString(entityId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? HaFormat.ParseState(await response.Content.ReadAsStringAsync(ct)) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
