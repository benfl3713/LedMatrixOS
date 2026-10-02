using System.Numerics;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// The morning-commute board. The next train is the hero (line-coloured glow, big destination, rolling minutes, a flashing DUE under a
/// minute); the following trains sit below it and page every few seconds; a strip along the bottom carries a pulsing pill per line serving the
/// station, the station name (which swaps to the current disruption when there is one) and the clock. TfL polling lives here, the board
/// derivation in <see cref="DepartureBoardModel"/>, the HTTP calls in <see cref="TflApi"/>.
/// </summary>
public class TubeDeparturesApp : WidgetApp
{
    public override string Id => "tube-departures";
    public override string Name => "Tube Departures";
    public override int FrameRate => 30;

    private const string NoStationSearchHint = "Type at least 2 chars";
    private static readonly IReadOnlyList<LineStatus> NoStatuses = [];

    [Setting("Station Search", Description = "Type a station name (e.g. Baker Street).")]
    public string StationSearch { get; set; } = "";

    [Setting("Station ID", Description = "TfL Naptan ID (auto-filled when you select from Station Select).")]
    public string StationId { get; set; } = "";

    [Setting("Platform Filter", Description = "Filter by platform name (e.g. 'Eastbound'). Leave empty to show all platforms.")]
    public string PlatformFilter { get; set; } = "";

    [Setting("Max Departures", Description = "Number of departures to cycle through", Min = 1, Max = 12)]
    public int MaxDepartures { get; set; } = 3;

    [Setting("Colour Departures By Line", Description = "When enabled, each departure uses its line colour for text and a stronger glow.")]
    public bool ColorDeparturesByLine { get; set; }

    [Setting("Page Seconds", Description = "How long each page of following trains stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 8;

    private readonly HttpClient _http;
    private readonly TflApi _api;
    private readonly DepartureBoardModel _model = new();
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _lineStatusRefreshInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _stationNameRefreshInterval = TimeSpan.FromHours(6);

    // Live data for the current station; replaced (and the old polls stopped) whenever the station changes
    private volatile ILiveData<TflArrival[]>? _arrivals;
    private volatile ILiveData<LineStatus[]>? _lineStatuses;
    private volatile ILiveData<string>? _stationName;
    private CancellationTokenSource? _stationPollCts;

    private volatile string[] _stationSearchOptions = [NoStationSearchHint];
    private string _stationSearchLastQuery = "";

    private Node? _board, _strip;
    private Node? _rest;
    private Pager? _pager;
    private StateScreen? _state;
    private Ticker? _ticker;
    private bool _entered, _boardShown;

    public TubeDeparturesApp(HttpClient httpClient)
    {
        _http = httpClient;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _api = new TflApi(_http);
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var styles = new BoardStyles();
        Func<bool> byLine = () => ColorDeparturesByLine;

        var hero = new ListView<Departure>(() => _model.Hero, d => new ScrollSlot(new HeroRow(d, _model, styles, byLine), HeroRow.RowHeight), d => d.Key)
            { HAlign = Align.Stretch, Height = HeroRow.RowHeight, ClipChildren = true, EnterOffset = 0, EnterDuration = 420.Ms(), ExitDuration = 420.Ms(), Easing = Easing.InOutCubic };

        _pager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Up) { Duration = 550.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1 }
            .Bind(() => _model.Pages, token => new ListView<Departure>(() => _model.Page(token.Index),
                d => new ScrollSlot(new CompactRow(d, _model, styles, byLine), CompactRow.RowHeight), d => d.Key)
                { EnterOffset = 0, EnterDuration = 350.Ms(), ExitDuration = 350.Ms() });
        _rest = _pager;

        _state = new StateScreen(Fonts.Big, Fonts.Small);
        _board = new Stack(Orientation.Vertical, gap: 1) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { hero, _pager } };
        _ticker = new Ticker(styles.Strip, styles.TinyAmber, () => _stationName?.Value, () => _lineStatuses?.Value);

        _strip = new Panel
        {
            Height = 12,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    CrossAlign = Align.Center,
                    Padding = new Thickness(2, 1, 3, 1),
                    Children =
                    {
                        new ListView<LineStatus>(() => _lineStatuses?.Value ?? NoStatuses, s => new LinePill(s, Fonts.QuiteSmall), s => s.LineId)
                            { Orientation = Orientation.Horizontal, Gap = 1, CrossAlign = Align.Center, EnterOffset = 6, ItemChanged = (n, s) => ((LinePill)n).Apply(s) },
                        _ticker,
                        new Clock("HH:mm", Time) { Style = styles.Clock },
                    },
                },
            },
        };

        return new Dock { Bottom = _strip, Fill = new Panel { _board, _state } };
    }

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        _model.Refresh(context.Time, _arrivals?.Value, PlatformFilter, MaxDepartures);

        bool hasTrains = _model.Visible.Count > 0;
        _state!.State = StateFor(hasTrains);
        _state.Detail = _stationName?.Value is { Length: > 0 } name ? name : "";
        _state.Visible = !hasTrains;
        _ticker!.Tick(context.Time);

        base.Update(context, cancellationToken);

        if (hasTrains && !_boardShown) Motion.SlideIn(_board!, Animator, new Vector2(0, 16), TimeSpan.Zero, 380.Ms());
        _boardShown = hasTrains;
        _board!.Visible = hasTrains;
        if (!_entered) PlayEntrance();
    }

    private BoardState StateFor(bool hasTrains)
    {
        if (string.IsNullOrWhiteSpace(StationId)) return BoardState.NoStation;
        var arrivals = _arrivals;
        if (arrivals is { Value: null, Error: not null }) return BoardState.Offline;
        return arrivals?.Value is null ? BoardState.Loading : BoardState.NoTrains;
    }

    // The bottom strip rises, then the state card (or the board, once data lands) slides in from the side.
    private void PlayEntrance()
    {
        _entered = true;
        Motion.SlideIn(_strip!, Animator, new Vector2(0, 14), 150.Ms());
        Motion.SlideIn(_state!, Animator, new Vector2(-120, 0), TimeSpan.Zero, 500.Ms(), Easing.OutBack);
    }

    // ---- settings ---------------------------------------------------------------------------------------------------------------

    // stationSelect has options that change as the user types, so it is not a [Setting] property.
    public override IEnumerable<AppSetting> GetSettings()
    {
        foreach (var setting in base.GetSettings())
        {
            yield return setting;
            if (setting.Key == "stationSearch")
                yield return new AppSetting("stationSelect", "Station Select", "Choose a result to set the station automatically.", AppSettingType.Select, "", "", Options: _stationSearchOptions);
        }
    }

    public override void UpdateSetting(string key, object value)
    {
        if (key != "stationSelect")
        {
            base.UpdateSetting(key, value);
            return;
        }

        var selected = value.ToString() ?? "";
        var split = selected.IndexOf(" | ", StringComparison.Ordinal);
        if (split > 0 && selected[..split].Trim() is { Length: > 0 } id) ApplyStationId(id);
    }

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "stationSearch": SearchStations(); break;
            case "stationId": ApplyStationId(StationId); break;
            case "pageSeconds":
                if (_pager is not null) _pager.Interval = PageSeconds.Seconds();
                break;
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _api.AppKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_api.AppKey)) Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");

        if (configuration["TubeDeparturesApp:StationId"] is { Length: > 0 } station) StationId = station;
        if (configuration["TubeDeparturesApp:PlatformFilter"] is { Length: > 0 } platform) PlatformFilter = platform;
        if (int.TryParse(configuration["TubeDeparturesApp:MaxDepartures"], out var max) && max > 0) MaxDepartures = Math.Clamp(max, 1, 12);
        if (bool.TryParse(configuration["TubeDeparturesApp:ColorDeparturesByLine"], out var colour)) ColorDeparturesByLine = colour;

        _entered = _boardShown = false;
        RestartStationPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    /// <summary>(Re)starts the polls that depend on the current station, stopping any previous ones.</summary>
    private void RestartStationPolling()
    {
        _stationPollCts?.Cancel();
        _arrivals = null;
        _lineStatuses = null;
        _stationName = null;
        _model.Reset();

        var stationId = StationId;
        if (string.IsNullOrWhiteSpace(stationId)) return;

        var cts = new CancellationTokenSource();
        _stationPollCts = cts;

        var arrivals = Poll(_refreshInterval, ct => _api.GetArrivalsAsync(stationId, ct), cts.Token);
        _arrivals = arrivals;
        _lineStatuses = Poll(_lineStatusRefreshInterval, ct => FetchLineStatusesAsync(arrivals, ct), cts.Token);
        _stationName = Poll(_stationNameRefreshInterval, ct => _api.GetStationNameAsync(stationId, ct), cts.Token);
    }

    private async Task<LineStatus[]> FetchLineStatusesAsync(ILiveData<TflArrival[]> arrivals, CancellationToken ct)
    {
        // Which lines serve the station is only known once arrivals have loaded
        string[] lineIds;
        while ((lineIds = LineIdsOf(arrivals.Value)).Length == 0) await Task.Delay(250, ct);
        return await _api.GetLineStatusesAsync(lineIds, ct);
    }

    // All unique line IDs that serve this station (from unfiltered arrivals)
    private static string[] LineIdsOf(TflArrival[]? arrivals) => (arrivals ?? [])
        .Where(a => !string.IsNullOrWhiteSpace(a.LineId)).Select(a => a.LineId)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id).ToArray();

    private void ApplyStationId(string stationId)
    {
        StationId = stationId;
        RestartStationPolling();
    }

    private void SearchStations()
    {
        var query = StationSearch.Trim();
        if (query.Length < 2)
        {
            _stationSearchOptions = [NoStationSearchHint];
            return;
        }

        RunInBackground(ct => SearchStationsAsync(query, ct));
    }

    private async Task SearchStationsAsync(string query, CancellationToken ct)
    {
        try
        {
            if (string.Equals(query, _stationSearchLastQuery, StringComparison.OrdinalIgnoreCase) && _stationSearchOptions.Length > 0) return;

            var (options, succeeded) = await _api.SearchStationsAsync(query, ct);
            if (succeeded) _stationSearchLastQuery = query;

            // The user may have kept typing while this was in flight; only the latest query may publish its results
            if (string.Equals(query, StationSearch.Trim(), StringComparison.OrdinalIgnoreCase)) _stationSearchOptions = options;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch
        {
            _stationSearchOptions = ["Search failed"];
        }
    }

    internal DepartureBoardModel Board => _model;
    internal Pager? RestPager => _pager;
    internal Ticker? StripTicker => _ticker;

    /// <summary>Test seam: replaces the polled data with fixed sources (call before the first frame).</summary>
    internal void UseData(ILiveData<TflArrival[]>? arrivals, ILiveData<LineStatus[]>? statuses, ILiveData<string>? stationName)
    {
        _arrivals = arrivals;
        _lineStatuses = statuses;
        _stationName = stationName;
        _model.Reset();
    }
}
