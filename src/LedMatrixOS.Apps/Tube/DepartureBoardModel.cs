using System.Text.RegularExpressions;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Tube;

/// <summary>
/// One train on the board. The instance for a train lives across polls (same <see cref="Key"/>), so a list view keeps its row and only the
/// minutes change; the minutes count down with the app's own clock between polls.
/// </summary>
internal sealed partial class Departure
{
    public string Key = "";
    public string LineId = "";
    public string LineName = "";
    public string Destination = "";
    public string PlatformNumber = "";
    public string PlatformFilterText = "";

    /// <summary>The direction of travel from the platform name ("Northbound" from "Northbound - Platform 1"); empty when there is none.</summary>
    public string Direction = "";

    /// <summary>The route (line plus direction or destination) this train runs on; see <see cref="TubeRoutes"/>.</summary>
    public string RouteKey = "";

    /// <summary>"Jubilee Eastbound" / "Metropolitan towards Aldgate".</summary>
    public string RouteLabel = "";

    /// <summary>True when more than one platform of the station serves this train's route, so the platform number tells riders something.</summary>
    public bool ShowPlatform;
    public Pixel Color;

    /// <summary>Seconds to arrival as TfL reported them at <see cref="SeenAt"/>.</summary>
    public int TimeToStation;
    public TimeSpan SeenAt;

    public double RemainingSeconds(TimeSpan now) => TimeToStation - (now - SeenAt).TotalSeconds;

    /// <summary>Whole minutes away; 0 means "due" (under a minute), as on a station board.</summary>
    public int Minutes(TimeSpan now)
    {
        var remaining = RemainingSeconds(now);
        return remaining < 60 ? 0 : (int)(remaining / 60);
    }

    [GeneratedRegex(@"platform\s+(\w{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex PlatformRegex();

    /// <summary>The text before " - Platform" (or before the dash), unless that part is itself just the platform.</summary>
    public static string DirectionOf(string platformName)
    {
        var text = (platformName ?? "").Trim();
        int dash = text.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0) text = text[..dash].Trim();
        if (text.StartsWith("platform", StringComparison.OrdinalIgnoreCase)) return "";
        if (text.Length == 0 || text.Equals("n/a", StringComparison.OrdinalIgnoreCase)) return "";
        return text;
    }

    public static string PlatformNumberOf(string platformName)
    {
        var m = PlatformRegex().Match(platformName ?? "");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
    }
}

internal readonly record struct PageToken(int Index);

/// <summary>
/// Turns polled arrivals into what the board shows: the platform filter and maximum applied, trains that have already left dropped,
/// the next train as the hero and the rest split into pages. All of it is derived from the app clock, so it is deterministic in tests.
/// <para>
/// Published lists only change when the set or order of trains changes, so a list view diffing them stays quiet in the steady state
/// and the per-frame <see cref="Refresh"/> allocates nothing.
/// </para>
/// </summary>
internal sealed class DepartureBoardModel
{
    /// <summary>How long a train that reached zero stays on the board before it counts as gone.</summary>
    public const double LingerSeconds = 12;

    public const int RowsPerPage = 2;

    /// <summary>Trains shown per direction column when grouping by direction.</summary>
    public const int ColumnRows = 3;

    private readonly Dictionary<string, Departure> _pool = new();
    private readonly List<Departure> _sorted = new();
    private readonly List<Departure> _scratch = new();
    private TflArrival[]? _source;
    private string _filter = "";
    private string[] _routes = [];
    private string _routeSetting = "";
    private string[] _routeKeys = [];
    private int _max;
    private Departure[] _visible = [];
    private Departure[] _hero = [];
    private PageToken[] _tokens = [];
    private Departure[][] _pages = [];
    private bool _perDirection;
    private Departure[][] _columns = [];
    private string[] _labels = [];
    private readonly List<string> _dirNames = new();
    private readonly List<int> _dirCounts = new();

    public TimeSpan Now { get; private set; }

    /// <summary>
    /// When set, the filter text is a comma separated list of route names (buses) that must match a departure's line name exactly,
    /// instead of a substring of its platform.
    /// </summary>
    public bool FilterByRoute { get; set; }

    /// <summary>True once any arrivals have been seen for the current station.</summary>
    public bool HasSource => _source is not null;

    public IReadOnlyList<Departure> Visible => _visible;

    /// <summary>The next train (empty or one item).</summary>
    public IReadOnlyList<Departure> Hero => _hero;

    /// <summary>One token per page of the remaining trains.</summary>
    public IReadOnlyList<PageToken> Pages => _tokens;

    /// <summary>Number of direction columns (0 to 2) when the board was refreshed per direction.</summary>
    public int ColumnCount => _columns.Length;

    /// <summary>The next trains of one direction column, in time order.</summary>
    public IReadOnlyList<Departure> Column(int index) => index >= 0 && index < _columns.Length ? _columns[index] : [];

    /// <summary>The direction of a column ("" for trains with no direction).</summary>
    public string ColumnLabel(int index) => index >= 0 && index < _labels.Length ? _labels[index] : "";

    /// <summary>True when the columns are the selected routes (line colour plus destination) rather than raw directions.</summary>
    public bool ColumnsByRoute => _routeKeys.Length > 0;

    /// <summary>The line colour of a column when <see cref="ColumnsByRoute"/>.</summary>
    public Pixel ColumnColor(int index) => index >= 0 && index < _columns.Length && _columns[index].Length > 0 ? _columns[index][0].Color : default;

    public IReadOnlyList<Departure> Page(int index) => index >= 0 && index < _pages.Length ? _pages[index] : [];

    public void Reset()
    {
        _source = null;
        _pool.Clear();
        _sorted.Clear();
        _visible = [];
        _hero = [];
        _tokens = [];
        _pages = [];
        _columns = [];
        _labels = [];
    }

    /// <summary>Re-derives the board. Returns true when the visible trains changed.</summary>
    /// <param name="perDirection">
    /// When true <paramref name="max"/> (capped at <see cref="ColumnRows"/>) applies to each direction rather than to the whole board, and
    /// <see cref="Column"/> holds the trains of the (up to two) busiest directions.
    /// </param>
    /// <param name="routes">
    /// Comma separated route keys (see <see cref="TubeRoutes"/>); when set only trains on one of them are kept (and the columns are the
    /// routes). Empty keeps everything. The platform filter still applies on top.
    /// </param>
    public bool Refresh(TimeSpan now, TflArrival[]? arrivals, string platformFilter, int max, bool perDirection = false, string routes = "")
    {
        Now = now;
        bool dirty = false;

        if (!ReferenceEquals(arrivals, _source))
        {
            if (arrivals is null) Reset();
            else Ingest(arrivals, now);
            dirty = true;
        }

        platformFilter = platformFilter?.Trim() ?? "";
        if (perDirection != _perDirection)
        {
            _perDirection = perDirection;
            dirty = true;
        }

        routes ??= "";
        if (routes != _routeSetting)
        {
            _routeSetting = routes;
            _routeKeys = routes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            dirty = true;
        }

        if (platformFilter != _filter || max != _max)
        {
            _filter = platformFilter;
            _routes = _filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _max = max;
            dirty = true;
        }

        _scratch.Clear();
        _dirNames.Clear();
        _dirCounts.Clear();
        int perColumn = Math.Min(max, ColumnRows);
        foreach (var d in _sorted)
        {
            if (d.RemainingSeconds(now) < -LingerSeconds) continue;
            if (!PassesFilter(d)) continue;
            if (perDirection)
            {
                int g = DirectionIndex(GroupOf(d));
                if (_dirCounts[g] >= perColumn) continue;
                _dirCounts[g]++;
                _scratch.Add(d);
            }
            else
            {
                _scratch.Add(d);
                if (_scratch.Count >= max) break;
            }
        }

        if (!dirty && SameAsVisible()) return false;
        Publish();
        return true;
    }

    private int DirectionIndex(string direction)
    {
        for (int i = 0; i < _dirNames.Count; i++)
            if (string.Equals(_dirNames[i], direction, StringComparison.OrdinalIgnoreCase)) return i;
        _dirNames.Add(direction);
        _dirCounts.Add(0);
        return _dirNames.Count - 1;
    }

    // What a column groups by: the route when routes are selected, the direction otherwise.
    private string GroupOf(Departure d) => _routeKeys.Length > 0 ? d.RouteKey : d.Direction;

    private bool PassesFilter(Departure d)
    {
        if (_routeKeys.Length > 0)
        {
            bool onRoute = false;
            foreach (var key in _routeKeys)
                if (string.Equals(key, d.RouteKey, StringComparison.OrdinalIgnoreCase)) { onRoute = true; break; }
            if (!onRoute) return false;
        }

        if (_filter.Length == 0) return true;
        if (!FilterByRoute) return d.PlatformFilterText.Contains(_filter, StringComparison.OrdinalIgnoreCase);

        foreach (var route in _routes)
            if (string.Equals(route, d.LineName, StringComparison.OrdinalIgnoreCase)) return true;
        return _routes.Length == 0;
    }

    private bool SameAsVisible()
    {
        if (_scratch.Count != _visible.Length) return false;
        for (int i = 0; i < _visible.Length; i++)
            if (!ReferenceEquals(_visible[i], _scratch[i])) return false;
        return true;
    }

    private void Publish()
    {
        _visible = _scratch.ToArray();
        _hero = _visible.Length > 0 ? [_visible[0]] : [];

        int rest = Math.Max(0, _visible.Length - 1);
        int pageCount = (rest + RowsPerPage - 1) / RowsPerPage;
        var pages = new Departure[pageCount][];
        for (int p = 0; p < pageCount; p++)
            pages[p] = _visible.Skip(1 + p * RowsPerPage).Take(RowsPerPage).ToArray();
        _pages = pages;

        PublishColumns();

        if (_tokens.Length != pageCount) _tokens = Enumerable.Range(0, pageCount).Select(i => new PageToken(i)).ToArray();
    }

    // The two directions with the earliest trains, ordered by name so a direction keeps its side of the board.
    private void PublishColumns()
    {
        if (!_perDirection)
        {
            _columns = [];
            _labels = [];
            return;
        }

        var names = new List<string>();
        foreach (var d in _visible)
        {
            var group = GroupOf(d);
            if (!names.Exists(n => string.Equals(n, group, StringComparison.OrdinalIgnoreCase))) names.Add(group);
            if (names.Count == 2) break;
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);

        _columns = names
            .Select(n => _visible.Where(d => string.Equals(GroupOf(d), n, StringComparison.OrdinalIgnoreCase)).Take(ColumnRows).ToArray())
            .ToArray();
        _labels = _routeKeys.Length > 0 ? _columns.Select(c => c[0].RouteLabel).ToArray() : names.ToArray();
    }

    private void Ingest(TflArrival[] arrivals, TimeSpan now)
    {
        _source = arrivals;
        var seen = new HashSet<string>();
        var groupCounts = new Dictionary<string, int>();
        _sorted.Clear();

        foreach (var a in arrivals.OrderBy(a => a.TimeToStation))
        {
            var key = KeyOf(a, groupCounts);
            if (!seen.Add(key)) continue;

            if (!_pool.TryGetValue(key, out var d)) _pool[key] = d = new Departure { Key = key };
            d.LineId = a.LineId;
            d.LineName = a.LineName;
            d.Destination = TflApi.StripStationSuffix(PickDestination(a));
            d.PlatformNumber = Departure.PlatformNumberOf(a.PlatformName);
            d.PlatformFilterText = a.PlatformName;
            d.Direction = Departure.DirectionOf(a.PlatformName);
            var destination = TubeRoutes.DestinationOf(a);
            d.RouteKey = TubeRoutes.KeyOf(a.LineId, d.Direction, destination);
            d.RouteLabel = TubeRoutes.LabelOf(string.IsNullOrWhiteSpace(a.LineName) ? TubeRoutes.LineTitle(a.LineId) : a.LineName, d.Direction, destination);
            d.Color = TubeColors.Display(a.LineId);
            d.TimeToStation = a.TimeToStation;
            d.SeenAt = now;
            _sorted.Add(d);
        }

        // A platform number only means something where one route is served from several platforms (Baker Street, say)
        var platformsPerRoute = _sorted.Where(d => d.PlatformNumber.Length > 0)
            .GroupBy(d => d.RouteKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(d => d.PlatformNumber).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var d in _sorted) d.ShowPlatform = platformsPerRoute.GetValueOrDefault(d.RouteKey) > 1;

        foreach (var key in _pool.Keys.Where(k => !seen.Contains(k)).ToList()) _pool.Remove(key);
    }

    // The vehicle id identifies a physical train; TfL reuses "000" (or nothing) where it has none, so those fall back to
    // line, platform and destination plus the position in the queue of identical services.
    private static string KeyOf(TflArrival a, Dictionary<string, int> groupCounts)
    {
        if (!string.IsNullOrWhiteSpace(a.VehicleId) && a.VehicleId != "000") return $"{a.LineId}|{a.VehicleId}";
        var group = $"{a.LineId}|{a.PlatformName}|{a.DestinationName}|{a.Towards}";
        groupCounts[group] = groupCounts.GetValueOrDefault(group) + 1;
        return $"{group}#{groupCounts[group]}";
    }

    private static string PickDestination(TflArrival a) =>
        !string.IsNullOrWhiteSpace(a.Towards) && !a.Towards.StartsWith("Check Front", StringComparison.OrdinalIgnoreCase) ? a.Towards : a.DestinationName;
}
