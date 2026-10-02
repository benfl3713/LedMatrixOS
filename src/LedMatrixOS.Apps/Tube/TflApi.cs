using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Tube;

/// <summary>One predicted arrival at a station, as TfL returns it.</summary>
internal sealed class TflArrival
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("vehicleId")] public string VehicleId { get; set; } = "";
    [JsonPropertyName("destinationName")] public string DestinationName { get; set; } = "";
    [JsonPropertyName("towards")] public string Towards { get; set; } = "";
    [JsonPropertyName("timeToStation")] public int TimeToStation { get; set; }
    [JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";
    [JsonPropertyName("lineName")] public string LineName { get; set; } = "";
    [JsonPropertyName("lineId")] public string LineId { get; set; } = "";
}

/// <summary>Worst current status of a line. Value equality, so a list view can tell when a status really changed.</summary>
internal sealed record LineStatus(string LineId, string Name, int Severity, string Description, string Reason)
{
    public Health Health => LineHealth.Classify(Severity, Description);
}

internal sealed record StationMatch(string Id, string Name);

/// <summary>
/// The TfL unified API calls both Tube apps need, kept apart from the views. It only talks HTTP and shapes the results;
/// polling, back-off and caching belong to the app (<c>Poll</c>).
/// </summary>
internal sealed class TflApi(HttpClient http)
{
    private const string Root = "https://api.tfl.gov.uk";

    /// <summary>Optional TfL application key (configuration <c>TFL:AppKey</c>), appended to every request.</summary>
    public string? AppKey { get; set; }

    public string WithKey(string url, string? extraQuery = null)
    {
        var query = extraQuery ?? "";
        if (!string.IsNullOrEmpty(AppKey)) query += (query.Length > 0 ? "&" : "") + $"app_key={Uri.EscapeDataString(AppKey)}";
        return query.Length > 0 ? $"{url}?{query}" : url;
    }

    public async Task<TflArrival[]> GetArrivalsAsync(string stationId, CancellationToken ct) =>
        await http.GetFromJsonAsync<TflArrival[]>(WithKey($"{Root}/StopPoint/{Uri.EscapeDataString(stationId)}/Arrivals"), ct) ?? [];

    public async Task<string> GetStationNameAsync(string stationId, CancellationToken ct)
    {
        var data = await http.GetFromJsonAsync<StopPointName>(WithKey($"{Root}/StopPoint/{Uri.EscapeDataString(stationId)}"), ct);
        return string.IsNullOrWhiteSpace(data?.CommonName) ? "" : StripStationSuffix(data.CommonName);
    }

    /// <summary>Status of specific lines (comma separated TfL ids), ordered by id.</summary>
    public async Task<LineStatus[]> GetLineStatusesAsync(IEnumerable<string> lineIds, CancellationToken ct)
    {
        var ids = string.Join(",", lineIds.Select(Uri.EscapeDataString));
        var lines = await http.GetFromJsonAsync<ApiLine[]>(WithKey($"{Root}/Line/{ids}/Status"), ct) ?? [];
        return Shape(lines);
    }

    /// <summary>Status of every line of the given modes (e.g. "tube,dlr"), ordered by line name.</summary>
    public async Task<LineStatus[]> GetModeStatusesAsync(string modes, CancellationToken ct)
    {
        var lines = await http.GetFromJsonAsync<ApiLine[]>(WithKey($"{Root}/Line/Mode/{modes}/Status"), ct) ?? [];
        return Shape(lines, byName: true);
    }

    /// <summary>
    /// Rail stops matching <paramref name="query"/> as "id | name" strings (the format of the stationSelect options),
    /// or a single message such as "No matches".
    /// </summary>
    public async Task<(string[] Options, bool Succeeded)> SearchStationsAsync(string query, CancellationToken ct)
    {
        var url = WithKey($"{Root}/StopPoint/Search/{Uri.EscapeDataString(query)}", "modes=tube,dlr,overground,elizabeth-line,tram");
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return (["No matches"], false);

        var data = await response.Content.ReadFromJsonAsync<SearchResponse>(ct);
        var matches = (data?.Matches ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name))
            .Where(IsLikelyRailStop)
            .DistinctBy(m => m.Id)
            .Take(12)
            .Select(m => $"{m.Id} | {StripStationSuffix(m.Name)}")
            .ToArray();
        return (matches.Length > 0 ? matches : ["No matches"], true);
    }

    public static string StripStationSuffix(string name) => name
        .Replace(" Underground Station", "", StringComparison.OrdinalIgnoreCase)
        .Replace(" Rail Station", "", StringComparison.OrdinalIgnoreCase)
        .Replace(" Station", "", StringComparison.OrdinalIgnoreCase)
        .Trim();

    private static LineStatus[] Shape(ApiLine[] lines, bool byName = false)
    {
        var shaped = lines.Select(l =>
        {
            // A line can carry several statuses at once (delays plus a part closure); show the worst one.
            var worst = (l.LineStatuses ?? []).OrderByDescending(s => (int)LineHealth.Classify(s.StatusSeverity, s.StatusSeverityDescription)).FirstOrDefault();
            return new LineStatus(l.Id, l.Name, worst?.StatusSeverity ?? 10, worst?.StatusSeverityDescription ?? "Unknown", worst?.Reason ?? "");
        });
        return (byName ? shaped.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase) : shaped.OrderBy(s => s.LineId, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static bool IsLikelyRailStop(SearchMatch match)
    {
        if (match.Id.StartsWith("940GZZ", StringComparison.OrdinalIgnoreCase)) return true;
        var rail = new[] { "tube", "dlr", "overground", "elizabeth-line", "tram" };
        return (match.Modes ?? []).Any(m => rail.Contains(m, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class StopPointName { [JsonPropertyName("commonName")] public string CommonName { get; set; } = ""; }

    private sealed class SearchResponse { [JsonPropertyName("matches")] public SearchMatch[]? Matches { get; set; } }

    private sealed class SearchMatch
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("modes")] public string[]? Modes { get; set; }
    }

    private sealed class ApiLine
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("lineStatuses")] public ApiStatus[]? LineStatuses { get; set; }
    }

    private sealed class ApiStatus
    {
        [JsonPropertyName("statusSeverity")] public int StatusSeverity { get; set; }
        [JsonPropertyName("statusSeverityDescription")] public string StatusSeverityDescription { get; set; } = "";
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }
}

/// <summary>How bad a line's current status is, ordered from calm to worst so the maximum is the one to show.</summary>
public enum Health { Good, Planned, Minor, Severe, Closed }

internal static class LineHealth
{
    public static readonly Pixel GoodColor = new(0, 200, 90);
    public static readonly Pixel PlannedColor = new(70, 170, 235);
    public static readonly Pixel MinorColor = new(255, 176, 0);
    public static readonly Pixel SevereColor = new(255, 50, 40);

    /// <summary>
    /// Groups the TfL severity codes: 10 is Good Service and 9 is Minor Delays (the old departures app treated 9 as good).
    /// </summary>
    public static Health Classify(int severity, string description) => severity switch
    {
        10 or 13 or 14 or 18 or 19 => Health.Good,
        4 or 5 or 11 => Health.Planned,
        6 or 8 => Health.Severe,
        1 or 2 or 16 or 20 => Health.Closed,
        0 or 3 or 7 or 9 or 12 or 15 or 17 => Health.Minor,
        _ => description.Contains("good", StringComparison.OrdinalIgnoreCase) ? Health.Good : Health.Minor,
    };

    /// <summary>True for the states that deserve attention now (planned closures are known well in advance).</summary>
    public static bool NeedsAttention(this Health h) => h >= Health.Minor;

    public static Pixel Color(this Health h) => h switch
    {
        Health.Good => GoodColor,
        Health.Planned => PlannedColor,
        Health.Minor => MinorColor,
        _ => SevereColor,
    };
}
