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
[LegacySettingKey("stationSearch")]
[LegacySettingKey("stationSelect", "stationId")]
public class TubeDeparturesApp : WidgetApp
{
    public override string Id => "tube-departures";
    public override string Name => "Tube Departures";
    public override int FrameRate => 30;

    private static readonly IReadOnlyList<LineStatus> NoStatuses = [];

    [Setting("Station", Description = "Search for a station (e.g. Baker Street).", Search = true)]
    public string StationId { get; set; } = "";

    [Setting("Routes", Description = "Only show these lines and directions (e.g. Metropolitan towards Aldgate). Leave empty to show everything.", MultiSearch = true, Browse = true)]
    public string Routes { get; set; } = "";

    [Setting("Platform Filter", Description = "Advanced: filter by platform name text (e.g. 'Eastbound'). Prefer Routes; leave empty to show everything.")]
    public string PlatformFilter { get; set; } = "";

    [Setting("Board Style", Description = "Split: a column per direction with the next trains of each. Platform: classic amber platform sign. Hero: the next train big, the rest paged below.",
        Options = ["Split", "Platform", "Hero"])]
    public string BoardStyle { get; set; } = "Split";

    [Setting("Max Departures", Description = "Hero: number of departures to cycle through. Split: trains per direction column (at most 3 fit). Platform: always 5 rows.", Min = 1, Max = 12)]
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

    private Node? _board, _strip;
    private Node? _heroBody, _splitBody, _platformBody, _splitDivider, _splitSecond, _stripNormal, _stripPlatform;
    private Ticker? _platformTicker;
    private Block? _stripe0, _stripe1;
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
        _heroBody = new Stack(Orientation.Vertical, gap: 1) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { hero, _pager } };
        _splitBody = BuildSplit(styles, byLine);
        _platformBody = BuildPlatform(styles, byLine);
        _board = new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _heroBody, _splitBody, _platformBody } };
        _ticker = new Ticker(styles.Strip, styles.TinyAmber, () => _stationName?.Value, () => _lineStatuses?.Value);
        _platformTicker = new Ticker(styles.TinyAmber, styles.TinyAmber, () => _stationName?.Value, () => _lineStatuses?.Value);

        _stripNormal = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
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

        _stripPlatform = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Visible = false,
            Children =
            {
                new Block(TubeGfx.Amber.WithBrightness(0.3f), height: 1) { VAlign = Align.Start },
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    HAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(6, 1, 6, 0),
                    Children = { _platformTicker, new Clock("HH:mm", Time) { Style = styles.SmallAmber } },
                },
            },
        };

        _strip = new Panel { Height = 12, Children = { _stripNormal, _stripPlatform } };

        return new Dock { Bottom = _strip, Fill = new Panel { _board, _state } };
    }

    /// <summary>Two direction columns, each headed by its direction and listing that direction's next trains.</summary>
    private Node BuildSplit(BoardStyles styles, Func<bool> byLine)
    {
        Node Column(int index)
        {
            // With routes selected the header carries the line colour as a stripe; the label reads "Metropolitan towards Aldgate"
            var stripe = new Block(default, width: 3) { Visible = false, HAlign = Align.Start };
            if (index == 0) _stripe0 = stripe; else _stripe1 = stripe;
            var header = new Panel
            {
                Height = 10,
                HAlign = Align.Stretch,
                Children =
                {
                    new Block(new Pixel(22, 22, 30)),
                    stripe,
                    new Label(() => _model.ColumnLabel(index) is { Length: > 0 } label ? label : "Next trains")
                        { Style = styles.TinyAmber, VAlign = Align.Center, Margin = new Thickness(7, 0, 0, 0) },
                },
            };
            var rows = new ListView<Departure>(() => _model.Column(index),
                d => new ScrollSlot(new CompactRow(d, _model, styles, byLine, countdownWidth: 34, platformWidth: 11), CompactRow.RowHeight), d => d.Key)
                { HAlign = Align.Stretch, VAlign = Align.Stretch, Grow = 1, ClipChildren = true, EnterOffset = 0, EnterDuration = 350.Ms(), ExitDuration = 350.Ms() };
            return new Stack(Orientation.Vertical) { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { header, rows } };
        }

        _splitSecond = Column(1);
        _splitDivider = new Block(new Pixel(40, 40, 52), width: 1);
        return new Stack(Orientation.Horizontal) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { Column(0), _splitDivider, _splitSecond } };
    }

    /// <summary>The platform sign: column captions, then amber rows on black.</summary>
    private Node BuildPlatform(BoardStyles styles, Func<bool> byLine)
    {
        var caption = new TextStyle(Fonts.QuiteSmall, TubeGfx.Amber.WithBrightness(0.5f), Shadow: false);
        var header = new Panel
        {
            Height = 10,
            HAlign = Align.Stretch,
            Children =
            {
                new Label("DESTINATION") { Style = caption, VAlign = Align.Center, Margin = new Thickness(20, 0, 0, 0) },
                new Label("MINS") { Style = caption, VAlign = Align.Center, HAlign = Align.End, Margin = new Thickness(0, 0, 8, 0) },
                new Block(TubeGfx.Amber.WithBrightness(0.3f), height: 1) { VAlign = Align.End },
            },
        };
        var rows = new ListView<Departure>(() => _model.Visible, d => new ScrollSlot(new PlatformRow(d, _model, styles, byLine), PlatformRow.RowHeight), d => d.Key)
            { HAlign = Align.Stretch, VAlign = Align.Stretch, Grow = 1, ClipChildren = true, EnterOffset = 0, EnterDuration = 350.Ms(), ExitDuration = 350.Ms(), Margin = new Thickness(0, 2, 0, 0) };
        return new Stack(Orientation.Vertical) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { header, rows } };
    }

    private enum Style { Split, Platform, Hero }

    private Style CurrentStyle => BoardStyle switch { "Hero" => Style.Hero, "Platform" => Style.Platform, _ => Style.Split };

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        var style = CurrentStyle;
        int max = style == Style.Platform ? 5 : MaxDepartures;
        _model.Refresh(context.Time, _arrivals?.Value, PlatformFilter, max, perDirection: style == Style.Split, Routes);
        bool byRoute = _model.ColumnsByRoute;
        _stripe0!.Visible = _stripe1!.Visible = byRoute;
        if (byRoute)
        {
            _stripe0.Color = _model.ColumnColor(0);
            _stripe1.Color = _model.ColumnColor(1);
        }

        _heroBody!.Visible = style == Style.Hero;
        _splitBody!.Visible = style == Style.Split;
        _platformBody!.Visible = style == Style.Platform;
        _splitSecond!.Visible = _splitDivider!.Visible = _model.ColumnCount > 1;
        _stripNormal!.Visible = style != Style.Platform;
        _stripPlatform!.Visible = style == Style.Platform;

        bool hasTrains = _model.Visible.Count > 0;
        _state!.State = StateFor(hasTrains);
        _state.Detail = _stationName?.Value is { Length: > 0 } name ? name : "";
        _state.Visible = !hasTrains;
        _ticker!.Tick(context.Time);
        _platformTicker!.Tick(context.Time);

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

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
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
        _lineStatuses = Poll(_lineStatusRefreshInterval, ct => TflLookups.FetchLineStatusesAsync(_api, arrivals, ct), cts.Token);
        _stationName = Poll(_stationNameRefreshInterval, ct => _api.GetStationNameAsync(stationId, ct), cts.Token);
    }

    private void ApplyStationId(string stationId)
    {
        StationId = stationId;
        RestartStationPolling();
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
