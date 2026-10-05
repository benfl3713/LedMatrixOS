using LedMatrixOS.Core.Settings;

namespace LedMatrixOS.Apps.Tube;

/// <summary>
/// A "route" is what a rider cares about at a station: a line plus a direction ("Jubilee Eastbound") or, where TfL gives no direction,
/// a line plus where it is going ("Metropolitan towards Aldgate"). It replaces the platform as the way to filter and group a board,
/// because platforms mean little on the Tube (except at interchanges such as Baker Street).
/// <para>
/// A route is stored as a key: <c>lineId|Direction</c> (e.g. <c>jubilee|Eastbound</c>) or <c>lineId|towards:Destination</c>
/// (e.g. <c>metropolitan|towards:Aldgate</c>). Keys are compared case-insensitively and never contain a comma (the list separator).
/// </para>
/// </summary>
internal static class TubeRoutes
{
    private const string TowardsPrefix = "towards:";

    /// <summary>The key of an arrival: its direction when the platform name carries one, otherwise its (terminus) destination.</summary>
    public static string KeyOf(string lineId, string direction, string destination) =>
        Clean(lineId).ToLowerInvariant() + "|" + (direction.Length > 0 ? Clean(direction) : TowardsPrefix + Clean(destination));

    public static string KeyOf(TflArrival a) => KeyOf(a.LineId, Departure.DirectionOf(a.PlatformName), DestinationOf(a));

    /// <summary>The terminus of an arrival without the "Underground Station" suffix; the vague "towards" text is only a fallback.</summary>
    public static string DestinationOf(TflArrival a) =>
        TflApi.StripStationSuffix(!string.IsNullOrWhiteSpace(a.DestinationName) ? a.DestinationName : a.Towards);

    /// <summary>"Jubilee Eastbound" or "Metropolitan towards Aldgate".</summary>
    public static string LabelOf(string lineName, string direction, string destination) =>
        direction.Length > 0 ? $"{lineName} {direction}".Trim() : $"{lineName} towards {destination}".Trim();

    /// <summary>The label of a stored key without any lookup (line titles are derived from the id).</summary>
    public static string LabelOfKey(string key)
    {
        int bar = key.IndexOf('|');
        if (bar < 0) return key;
        var line = LineTitle(key[..bar]);
        var rest = key[(bar + 1)..];
        return rest.StartsWith(TowardsPrefix, StringComparison.OrdinalIgnoreCase)
            ? $"{line} towards {rest[TowardsPrefix.Length..]}"
            : $"{line} {rest}".Trim();
    }

    public static string LineTitle(string lineId) => lineId.ToLowerInvariant() switch
    {
        "dlr" => "DLR",
        "hammersmith-city" => "Hammersmith & City",
        "waterloo-city" => "Waterloo & City",
        var id => string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..])),
    };

    private static string Clean(string text) => text.Replace(',', ' ').Trim();
}

/// <summary>
/// Lists the routes serving a station, from its live arrivals: one option per line and direction (or destination). The station comes from
/// the options context (<c>stationId</c>), so the list works before the app is active. Arrivals are cached briefly per station.
/// </summary>
internal sealed class TflRouteOptions(TflApi api, Func<DateTime>? now = null) : ISettingOptionsProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(20);
    private const int MaxCached = 50;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, IReadOnlyList<SettingOption> Options)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool Browse => true;

    public async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        if (!context.TryGetValue("stationId", out var stationId) || string.IsNullOrWhiteSpace(stationId)) return [];
        stationId = stationId.Trim();

        if (!_cache.TryGetValue(stationId, out var hit) || _now() - hit.At >= Ttl)
        {
            var options = Build(await api.GetArrivalsAsync(stationId, ct).ConfigureAwait(false));
            if (_cache.Count >= MaxCached) _cache.Clear();
            _cache[stationId] = hit = (_now(), options);
        }

        query = query.Trim();
        return query.Length == 0
            ? hit.Options
            : hit.Options.Where(o => o.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || (o.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    public Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct) => Task.FromResult<string?>(TubeRoutes.LabelOfKey(value));

    internal static IReadOnlyList<SettingOption> Build(TflArrival[] arrivals) => arrivals
        .Where(a => !string.IsNullOrWhiteSpace(a.LineId))
        .GroupBy(TubeRoutes.KeyOf, StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
            var first = g.First();
            var lineName = string.IsNullOrWhiteSpace(first.LineName) ? TubeRoutes.LineTitle(first.LineId) : first.LineName;
            var platforms = g.Select(a => Departure.PlatformNumberOf(a.PlatformName)).Where(p => p.Length > 0).Distinct().Order().ToArray();
            var subtitle = platforms.Length switch
            {
                0 => $"{lineName} line",
                1 => $"{lineName} line, platform {platforms[0]}",
                _ => $"{lineName} line, platforms {string.Join(", ", platforms)}",
            };
            return (Line: lineName, Option: new SettingOption(g.Key, TubeRoutes.LabelOf(lineName, Departure.DirectionOf(first.PlatformName), TubeRoutes.DestinationOf(first)), subtitle));
        })
        .OrderBy(x => x.Line, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Option.Label, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.Option)
        .ToList();
}
