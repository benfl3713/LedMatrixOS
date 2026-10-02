using System.Text.Json;

namespace LedMatrixOS.Apps.Tube;

public enum LineHealth { Unknown, Good, Minor, Severe }

public sealed record StopInfo(string Id, string Name);

/// <summary>A train, placed at the stop it is next due at (index into the ordered stop list).</summary>
public sealed record TrainInfo(string Id, int StopIndex, string Direction, string Destination, string NextStation, int Seconds);

public sealed record LineStatusInfo(LineHealth Health, string Description, string? Reason);

public sealed record LineSnapshot(string LineId, IReadOnlyList<StopInfo> Stops, IReadOnlyList<TrainInfo> Trains, LineStatusInfo Status, IReadOnlyList<string> RouteOptions);

/// <summary>Fetches and parses one TfL line: its ordered stations (cached), live train positions and service status.</summary>
public sealed class TubeLineClient(HttpClient http, string? appKey = null)
{
    private sealed record Route(Dictionary<string, (string Name, double Lon)> Stops, List<(string Label, string[] Ids, bool Regular)> Variants, DateTime Fetched);

    private readonly Dictionary<string, Route> _routes = new(StringComparer.OrdinalIgnoreCase);

    public string? AppKey { get; set; } = appKey;

    public async Task<LineSnapshot> FetchAsync(string lineId, bool pinned, string branchRoute, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var route = await GetRouteAsync(lineId, timeout.Token);
        var variants = route.Variants;
        var chosen = (pinned && !branchRoute.Equals("auto", StringComparison.OrdinalIgnoreCase)
                         ? variants.FirstOrDefault(v => v.Label.Equals(branchRoute, StringComparison.OrdinalIgnoreCase)) : default)
                     is { Ids: not null } pick ? pick
                     : variants.OrderByDescending(v => v.Regular).ThenByDescending(v => v.Ids.Length).FirstOrDefault();

        var stops = BuildStops(route, chosen.Ids);
        var arrivals = await GetAsync($"https://api.tfl.gov.uk/Line/{lineId}/Arrivals", timeout.Token);
        var trains = ParseArrivals(arrivals, stops);
        LineStatusInfo status;
        try { status = ParseStatus(await GetAsync($"https://api.tfl.gov.uk/Line/{lineId}/Status", timeout.Token)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { status = new LineStatusInfo(LineHealth.Unknown, "No status", null); }

        return new LineSnapshot(lineId, stops, trains, status, new[] { "auto" }.Concat(variants.Select(v => v.Label).Distinct(StringComparer.OrdinalIgnoreCase)).ToArray());
    }

    private async Task<string> GetAsync(string url, CancellationToken ct) =>
        await http.GetStringAsync(string.IsNullOrEmpty(AppKey) ? url : url + "?app_key=" + Uri.EscapeDataString(AppKey), ct);

    private async Task<Route> GetRouteAsync(string lineId, CancellationToken ct)
    {
        if (_routes.TryGetValue(lineId, out var cached) && DateTime.UtcNow - cached.Fetched < TimeSpan.FromHours(6)) return cached;
        foreach (var direction in new[] { "all", "inbound", "outbound" })
        {
            try
            {
                if (ParseRoute(await GetAsync($"https://api.tfl.gov.uk/Line/{lineId}/Route/Sequence/{direction}", ct)) is { } route)
                    return _routes[lineId] = route;
            }
            catch (HttpRequestException) { }
        }
        throw new HttpRequestException($"No route data for {lineId}");
    }

    private static Route? ParseRoute(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var stops = new Dictionary<string, (string, double)>(StringComparer.OrdinalIgnoreCase);
        var fallback = new List<string>();
        void Add(JsonElement s)
        {
            var id = Str(s, "stationId") is { Length: > 0 } sid ? sid : Str(s, "id");
            if (id.Length == 0) return;
            stops.TryAdd(id, (Str(s, "name"), s.TryGetProperty("lon", out var lon) && lon.ValueKind == JsonValueKind.Number ? lon.GetDouble() : 0));
            fallback.Add(id);
        }
        if (root.TryGetProperty("stations", out var st)) foreach (var s in st.EnumerateArray()) Add(s);
        var sequences = new List<string[]>();
        if (root.TryGetProperty("stopPointSequences", out var seqs))
            foreach (var seq in seqs.EnumerateArray())
            {
                var before = fallback.Count;
                if (seq.TryGetProperty("stopPoint", out var sp)) foreach (var s in sp.EnumerateArray()) Add(s);
                sequences.Add(fallback.Skip(before).ToArray());
            }

        var variants = new List<(string, string[], bool)>();
        if (root.TryGetProperty("orderedLineRoutes", out var ordered))
            foreach (var r in ordered.EnumerateArray())
            {
                var ids = r.TryGetProperty("naptanIds", out var n) ? n.EnumerateArray().Select(e => e.GetString() ?? "").Where(i => i.Length > 0).ToArray() : [];
                if (ids.Length < 2) continue;
                var name = Str(r, "name");
                variants.Add((name.Length == 0 ? $"{ids[0]} -> {ids[^1]}" : name, ids, Str(r, "serviceType").Equals("Regular", StringComparison.OrdinalIgnoreCase)));
            }

        // Same label twice: keep the longer path.
        var result = variants.GroupBy(v => v.Item1, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(v => v.Item2.Length).First()).ToList();
        if (result.Count == 0)
        {
            var best = sequences.OrderByDescending(s => s.Length).FirstOrDefault() ?? fallback.ToArray();
            if (best.Length > 0) result.Add(("Main", best, true));
        }
        return stops.Count == 0 && result.Count == 0 ? null : new Route(stops, result, DateTime.UtcNow);
    }

    private static List<StopInfo> BuildStops(Route route, string[]? ids)
    {
        var list = (ids ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => route.Stops.TryGetValue(id, out var s) ? (Stop: new StopInfo(id, s.Name), Lon: s.Lon) : (Stop: new StopInfo(id, id), Lon: 0.0)).ToList();
        // Run west to east: easier to read on a wide display.
        if (list.Count >= 2 && list[0].Lon > list[^1].Lon) list.Reverse();
        return list.Select(x => x.Stop).ToList();
    }

    private static List<TrainInfo> ParseArrivals(string json, List<StopInfo> stops)
    {
        var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < stops.Count; i++)
        {
            foreach (var key in IdCandidates(stops[i].Id)) byId.TryAdd(key, i);
            byName.TryAdd(NormalizeName(stops[i].Name), i);
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(a => (Vehicle: Str(a, "vehicleId"), Naptan: Str(a, "naptanId"), Name: Str(a, "stationName"), Secs: a.TryGetProperty("timeToStation", out var t) ? t.GetInt32() : 0, A: a))
            .Where(x => x.Vehicle.Length > 0 && x.Naptan.Length > 0)
            .GroupBy(x => x.Vehicle)
            .Select(g => g.OrderBy(x => x.Secs).First())
            .Select(x =>
            {
                int index = IdCandidates(x.Naptan).Select(k => byId.TryGetValue(k, out var i) ? i : -1).FirstOrDefault(i => i >= 0, -1);
                if (index < 0 && byName.TryGetValue(NormalizeName(x.Name), out var ni)) index = ni;
                return index < 0 ? null : new TrainInfo(x.Vehicle, index, Str(x.A, "direction"), Str(x.A, "destinationName"), ShortName(x.Name), x.Secs);
            })
            .Where(t => t is not null).Select(t => t!).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>TfL severity codes: 10/18/19 are fine, 6/2/1/16/20 stop or badly hurt service, the rest are smaller problems.</summary>
    public static LineHealth Classify(int severity) => severity switch
    {
        10 or 18 or 19 => LineHealth.Good,
        1 or 2 or 6 or 16 or 20 => LineHealth.Severe,
        _ => LineHealth.Minor,
    };

    public static LineStatusInfo ParseStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        LineStatusInfo? worst = null;
        foreach (var line in doc.RootElement.EnumerateArray())
        {
            if (!line.TryGetProperty("lineStatuses", out var statuses)) continue;
            foreach (var s in statuses.EnumerateArray())
            {
                var health = Classify(s.TryGetProperty("statusSeverity", out var sev) ? sev.GetInt32() : 10);
                if (worst is not null && health <= worst.Health) continue;
                var reason = Str(s, "reason");
                if (reason.Length == 0 && s.TryGetProperty("disruption", out var d)) reason = Str(d, "description");
                worst = new LineStatusInfo(health, Str(s, "statusSeverityDescription"), reason.Length == 0 ? null : reason);
            }
        }
        return worst ?? new LineStatusInfo(LineHealth.Unknown, "No status", null);
    }

    public static string NormalizeName(string? name) => (name ?? "").Trim()
        .Replace(" Underground Station", "", StringComparison.OrdinalIgnoreCase).Replace(" Rail Station", "", StringComparison.OrdinalIgnoreCase)
        .Replace(" Station", "", StringComparison.OrdinalIgnoreCase).Replace(" & ", " and ", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();

    /// <summary>Station name for display: station suffixes stripped, original casing kept.</summary>
    public static string ShortName(string? name) => (name ?? "").Trim()
        .Replace(" Underground Station", "", StringComparison.OrdinalIgnoreCase).Replace(" Rail Station", "", StringComparison.OrdinalIgnoreCase)
        .Replace(" Station", "", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> IdCandidates(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) yield break;
        var n = id.Trim().ToUpperInvariant();
        yield return n;
        int slash = n.LastIndexOf('/');
        if (slash >= 0 && slash < n.Length - 1) { n = n[(slash + 1)..]; yield return n; }
        if (n.Length == 3 && n.All(char.IsLetterOrDigit)) yield return "940GZZLU" + n;
        if (n.StartsWith("940GZZLU", StringComparison.Ordinal) && n.Length > 8) yield return n[8..];
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
