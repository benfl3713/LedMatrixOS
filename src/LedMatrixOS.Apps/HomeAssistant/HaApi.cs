using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LedMatrixOS.Apps.HomeAssistant;

internal sealed record HaState(string EntityId, string State, string? Unit, string? FriendlyName);

/// <summary>An entity the user asked for, with an optional label ("sensor.lounge_temp|Lounge").</summary>
internal sealed record EntityRef(string EntityId, string? Label);

internal enum TileKind { Number, On, Off, Text, Unavailable }

/// <summary>What one tile shows, already formatted (built only when the data or entity list changes).</summary>
internal sealed record TileData(string Label, string Value, string Unit, TileKind Kind);

internal static class HaFormat
{
    public static List<EntityRef> ParseEntities(string? text)
    {
        var list = new List<EntityRef>();
        foreach (var part in (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bar = part.IndexOf('|');
            var id = (bar < 0 ? part : part[..bar]).Trim();
            var label = bar < 0 ? null : part[(bar + 1)..].Trim();
            if (id.Contains('.') && id.Length > 2) list.Add(new EntityRef(id, string.IsNullOrEmpty(label) ? null : label));
        }
        return list;
    }

    public static HaState ParseState(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? unit = null, name = null;
        if (root.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            if (attrs.TryGetProperty("unit_of_measurement", out var u) && u.ValueKind == JsonValueKind.String) unit = u.GetString();
            if (attrs.TryGetProperty("friendly_name", out var f) && f.ValueKind == JsonValueKind.String) name = f.GetString();
        }
        return new HaState(root.GetProperty("entity_id").GetString() ?? "", root.GetProperty("state").GetString() ?? "", unit, name);
    }

    public static TileData ToTile(EntityRef entity, HaState? state)
    {
        var label = entity.Label ?? state?.FriendlyName ?? entity.EntityId[(entity.EntityId.IndexOf('.') + 1)..].Replace('_', ' ');
        label = label.ToUpperInvariant();
        if (state is null || state.State is "unavailable" or "unknown") return new TileData(label, "N/A", "", TileKind.Unavailable);

        if (double.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return new TileData(label, Math.Round(number, 1).ToString("0.#", CultureInfo.InvariantCulture), state.Unit ?? "", TileKind.Number);

        return state.State.ToLowerInvariant() switch
        {
            "on" or "home" or "open" or "playing" or "unlocked" => new TileData(label, state.State.ToUpperInvariant(), "", TileKind.On),
            "off" or "not_home" or "closed" or "paused" or "idle" or "locked" => new TileData(label, state.State.Replace('_', ' ').ToUpperInvariant(), "", TileKind.Off),
            _ => new TileData(label, state.State.Replace('_', ' ').ToUpperInvariant(), "", TileKind.Text),
        };
    }
}

internal sealed class HaApi(HttpClient http)
{
    public string BaseUrl { get; set; } = "";
    public string Token { get; set; } = "";

    public bool IsConfigured => BaseUrl.Length > 0 && Token.Length > 0;

    /// <summary>Fetches every entity; one that fails comes back as null so its tile shows N/A instead of hiding the others.</summary>
    public async Task<HaState?[]> GetStatesAsync(IReadOnlyList<EntityRef> entities, CancellationToken ct) =>
        await Task.WhenAll(entities.Select(e => GetOneAsync(e.EntityId, ct)));

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
