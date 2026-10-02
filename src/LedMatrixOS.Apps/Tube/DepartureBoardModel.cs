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

    private readonly Dictionary<string, Departure> _pool = new();
    private readonly List<Departure> _sorted = new();
    private readonly List<Departure> _scratch = new();
    private TflArrival[]? _source;
    private string _filter = "";
    private string[] _routes = [];
    private int _max;
    private Departure[] _visible = [];
    private Departure[] _hero = [];
    private PageToken[] _tokens = [];
    private Departure[][] _pages = [];

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
    }

    /// <summary>Re-derives the board. Returns true when the visible trains changed.</summary>
    public bool Refresh(TimeSpan now, TflArrival[]? arrivals, string platformFilter, int max)
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
        if (platformFilter != _filter || max != _max)
        {
            _filter = platformFilter;
            _routes = _filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _max = max;
            dirty = true;
        }

        _scratch.Clear();
        foreach (var d in _sorted)
        {
            if (d.RemainingSeconds(now) < -LingerSeconds) continue;
            if (!PassesFilter(d)) continue;
            _scratch.Add(d);
            if (_scratch.Count >= max) break;
        }

        if (!dirty && SameAsVisible()) return false;
        Publish();
        return true;
    }

    private bool PassesFilter(Departure d)
    {
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

        if (_tokens.Length != pageCount) _tokens = Enumerable.Range(0, pageCount).Select(i => new PageToken(i)).ToArray();
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
            d.Color = TubeColors.Display(a.LineId);
            d.TimeToStation = a.TimeToStation;
            d.SeenAt = now;
            _sorted.Add(d);
        }

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
