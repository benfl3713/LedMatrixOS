namespace LedMatrixOS.Apps.Rail;

/// <summary>
/// One service as the board shows it. The instance lives across polls (same key), so a list view keeps its row and
/// only the changed text updates. Strings are formatted when the service changes, never per frame.
/// </summary>
internal sealed class RailRow
{
    public string Key = "";
    public RailService Service = null!;
    public int Version;

    public string Time = "", Status = "", Destination = "", Platform = "", Info = "";
    public RailStatus State;

    public DateTimeOffset DepartsAt => Service.DepartsAt;

    public void Set(RailService service)
    {
        if (ReferenceEquals(Service, service) || (Service is not null && Service.Equals(service))) return;

        Service = service;
        Key = service.Key;
        State = service.Status;
        Time = service.ScheduledTime.ToString("HH:mm");
        Destination = service.Destination;
        Platform = service.Platform;
        Status = service.Status switch
        {
            RailStatus.Cancelled => "Cancelled",
            RailStatus.Delayed => "Exp " + service.DepartsAt.ToString("HH:mm"),
            _ => "On time",
        };

        var calling = service.CallingPoints.Length > 0 ? "Calling at: " + service.CallingPoints : "";
        var extra = service.Operator.Length > 0 ? service.Operator : "";
        if (service.Coaches is { } c) extra = extra.Length > 0 ? $"{extra}, {c} coaches" : $"{c} coaches";
        Info = service.Status == RailStatus.Cancelled
            ? "This service has been cancelled" + (extra.Length > 0 ? ". " + extra : "")
            : service.Status == RailStatus.Delayed
                ? "Delayed. " + calling + (extra.Length > 0 ? ". " + extra : "")
                : calling + (extra.Length > 0 ? (calling.Length > 0 ? ". " : "") + extra : "");
        Version++;
    }
}

internal readonly record struct RailPageToken(int Index);

/// <summary>
/// Turns the polled services into what the board shows, derived from the app clock so it is deterministic in tests: the platform filter and
/// maximum applied, trains that have left dropped, the next train as the hero and the rest in pages. Published lists only change when the
/// trains themselves change, so a per-frame <see cref="Refresh"/> allocates nothing.
/// </summary>
internal sealed class RailBoardModel
{
    public const int RowsPerPage = 3;

    /// <summary>The "last train" pill appears this close to the departure.</summary>
    public static readonly TimeSpan LastTrainWindow = TimeSpan.FromMinutes(30);

    private readonly List<RailRow> _all = new();
    private readonly List<RailRow> _filtered = new();
    private readonly List<RailRow> _scratch = new();
    private Dictionary<string, RailRow> _pool = new();
    private RailService[]? _source;
    private string _filter = "";
    private int _max = -1;
    private RailRow[] _visible = [];
    private RailRow[] _hero = [];
    private RailPageToken[] _tokens = [];
    private RailRow[][] _pages = [];
    private bool _dirty;

    public bool HasSource => _source is not null;

    public IReadOnlyList<RailRow> Visible => _visible;
    public IReadOnlyList<RailRow> Hero => _hero;
    public IReadOnlyList<RailPageToken> Pages => _tokens;
    public IReadOnlyList<RailRow> Page(int index) => index >= 0 && index < _pages.Length ? _pages[index] : [];

    public void Refresh(DateTimeOffset now, RailService[]? source, string? platformFilter, int max)
    {
        platformFilter ??= "";
        if (!ReferenceEquals(source, _source) || max != _max || platformFilter != _filter)
        {
            _source = source;
            _max = max;
            _filter = platformFilter;
            Rebuild();
        }

        _scratch.Clear();
        foreach (var row in _filtered)
        {
            if (row.DepartsAt <= now) continue;
            _scratch.Add(row);
            if (_scratch.Count >= _max) break;
        }

        if (!SameAs(_visible, _scratch) || _dirty) Publish();
    }

    /// <summary>
    /// Minutes until the flagged last train leaves, when it is still ahead (null otherwise). Ignores the platform filter and the maximum,
    /// so the last train is never hidden by either.
    /// </summary>
    public double? LastTrainMinutes(DateTimeOffset now) => LastTrain(now)?.Minutes;

    /// <summary>The flagged last train still ahead (its key identifies the service, so a change of service can be told apart), or null.</summary>
    public (string Key, string Destination, double Minutes)? LastTrain(DateTimeOffset now)
    {
        foreach (var row in _all)
            if (row.Service.IsLastTrain && row.State != RailStatus.Cancelled && row.DepartsAt > now)
                return (row.Service.Key, row.Service.Destination, (row.DepartsAt - now).TotalMinutes);
        return null;
    }

    /// <summary>Whole minutes to the next train still running, or -1 when there is none.</summary>
    public int NextMinutes(DateTimeOffset now)
    {
        foreach (var row in _visible)
            if (row.State != RailStatus.Cancelled)
                return Math.Max(0, (int)(row.DepartsAt - now).TotalMinutes);
        return -1;
    }

    private void Rebuild()
    {
        var pool = new Dictionary<string, RailRow>();
        _all.Clear();
        _filtered.Clear();

        if (_source is not null)
        {
            var platforms = _filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var service in _source)
            {
                if (!_pool.TryGetValue(service.Key, out var row)) row = new RailRow();
                row.Set(service);
                pool[service.Key] = row;
                _all.Add(row);
                if (platforms.Length == 0 || Array.Exists(platforms, p => p.Equals(service.Platform, StringComparison.OrdinalIgnoreCase)))
                    _filtered.Add(row);
            }

            _all.Sort((a, b) => a.DepartsAt.CompareTo(b.DepartsAt));
            _filtered.Sort((a, b) => a.DepartsAt.CompareTo(b.DepartsAt));
        }

        _pool = pool;
        _dirty = true;
    }

    private static bool SameAs(RailRow[] a, List<RailRow> b)
    {
        if (a.Length != b.Count) return false;
        for (int i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    private void Publish()
    {
        _dirty = false;
        _visible = _scratch.ToArray();
        _hero = _visible.Length > 0 ? [_visible[0]] : [];

        int rest = Math.Max(0, _visible.Length - 1);
        int pages = (rest + RowsPerPage - 1) / RowsPerPage;
        _pages = new RailRow[pages][];
        _tokens = new RailPageToken[pages];
        for (int p = 0; p < pages; p++)
        {
            int start = 1 + p * RowsPerPage;
            _pages[p] = _visible[start..Math.Min(_visible.Length, start + RowsPerPage)];
            _tokens[p] = new RailPageToken(p);
        }
    }
}
