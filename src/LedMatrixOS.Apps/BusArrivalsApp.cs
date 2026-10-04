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
/// Live bus arrivals for up to four stops. Each stop is a page (stop name and clock on top, the next buses below as route badge,
/// destination and rolling minutes); the pager slides between stops. Buses enter and leave the list with an animation as they
/// arrive and depart. TfL polling lives here, the board derivation in <see cref="DepartureBoardModel"/>, HTTP in <see cref="TflApi"/>.
/// </summary>
[LegacySettingKey("stopSearch")]
[LegacySettingKey("stopSelect", "stopIds")]
public class BusArrivalsApp : WidgetApp
{
    public override string Id => "bus-arrivals";
    public override string Name => "Bus Arrivals";
    public override int FrameRate => 30;

    public const int MaxStops = 4;
    private static readonly IReadOnlyList<StopToken> NoTokens = [];

    [Setting("Stops", Description = "Search for bus stops, up to 4.", MultiSearch = true, Max = MaxStops)]
    public string StopIds { get; set; } = "";

    [Setting("Route Filter", Description = "Only show these routes, comma separated (e.g. 73,38). Leave empty to show all.")]
    public string RouteFilter { get; set; } = "";

    [Setting("Buses Per Stop", Description = "Number of buses listed for each stop", Min = 1, Max = 4)]
    public int MaxBuses { get; set; } = 4;

    [Setting("Page Seconds", Description = "How long each stop stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 8;

    private readonly HttpClient _http;
    private readonly TflApi _api;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _labelRefreshInterval = TimeSpan.FromHours(6);

    private volatile BusStopFeed[] _feeds = [];
    private IReadOnlyList<StopToken> _tokens = NoTokens;
    private CancellationTokenSource? _pollCts;

    private Pager? _pager;
    private Label? _message;
    private bool _entered, _boardShown;

    public BusArrivalsApp(HttpClient httpClient)
    {
        _http = httpClient;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _api = new TflApi(_http);
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var styles = new BoardStyles();

        _pager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Left) { Duration = 500.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1 }
            .Bind(() => _tokens, token => BuildStopPage(token, styles));

        _message = new Label(() => MessageText()) { Style = styles.SmallAmber, HAlign = Align.Center, VAlign = Align.Center, Visible = false };
        return new Panel { _pager, _message };
    }

    private Node BuildStopPage(StopToken token, BoardStyles styles)
    {
        var feed = Array.Find(_feeds, f => f.StopId == token.StopId) ?? new BusStopFeed(token.StopId);

        var header = new Panel
        {
            HAlign = Align.Stretch,
            Height = 12,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(3, 1),
                    Children =
                    {
                        new Pill("BUS", BusColors.Day) { Style = new TextStyle(Fonts.QuiteSmall, Pixel.White, Shadow: false), Height = 9 },
                        new MarqueeLabel(() => feed.Label?.Value ?? "") { Style = styles.Strip, Grow = 1 },
                        new Clock("HH:mm", Time) { Style = styles.Clock },
                    },
                },
            },
        };

        var rows = new ListView<Departure>(() => feed.Model.Visible, d => new ScrollSlot(new BusRow(d, feed.Model, styles), BusRow.RowHeight), d => d.Key)
            { HAlign = Align.Stretch, EnterOffset = 0, EnterDuration = 350.Ms(), ExitDuration = 350.Ms() };

        return new Stack(Orientation.Vertical) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { header, rows } };
    }

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        bool anyBuses = false;
        foreach (var feed in _feeds)
        {
            feed.Model.Refresh(context.Time, feed.Arrivals?.Value, RouteFilter, MaxBuses);
            if (feed.Model.Visible.Count > 0) anyBuses = true;
        }

        _message!.Visible = !anyBuses;
        _pager!.Visible = anyBuses;

        base.Update(context, cancellationToken);

        if (anyBuses && !_boardShown) Motion.SlideIn(_pager, Animator, new Vector2(0, 16), TimeSpan.Zero, 380.Ms());
        _boardShown = anyBuses;
        if (!_entered) PlayEntrance();
    }

    private string MessageText()
    {
        if (_feeds.Length == 0) return "Choose a bus stop in the app";
        foreach (var feed in _feeds)
        {
            var arrivals = feed.Arrivals;
            if (arrivals is { Value: null, Error: not null }) return "No data, retrying";
            if (arrivals?.Value is null) return "Loading buses";
        }

        return "No buses due";
    }

    private void PlayEntrance()
    {
        _entered = true;
        Motion.SlideIn(_message!, Animator, new Vector2(-120, 0), TimeSpan.Zero, 500.Ms(), Easing.OutBack);
    }

    // ---- settings ---------------------------------------------------------------------------------------------------------------

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "stopIds": RestartPolling(); break;
            case "pageSeconds":
                if (_pager is not null) _pager.Interval = PageSeconds.Seconds();
                break;
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _api.AppKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_api.AppKey)) Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");

        if (configuration["BusArrivals:StopIds"] is { Length: > 0 } stops) StopIds = stops;
        if (configuration["BusArrivals:RouteFilter"] is { Length: > 0 } routes) RouteFilter = routes;
        if (int.TryParse(configuration["BusArrivals:MaxBuses"], out var max) && max > 0) MaxBuses = Math.Clamp(max, 1, 4);

        _entered = _boardShown = false;
        RestartPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    /// <summary>The configured stop IDs: trimmed, distinct, at most <see cref="MaxStops"/>.</summary>
    internal static string[] ParseStopIds(string? ids) => (ids ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxStops)
        .ToArray();

    /// <summary>(Re)starts the polls for the configured stops, stopping any previous ones.</summary>
    private void RestartPolling()
    {
        _pollCts?.Cancel();

        var cts = new CancellationTokenSource();
        _pollCts = cts;

        var feeds = ParseStopIds(StopIds).Select(id => new BusStopFeed(id)).ToArray();
        foreach (var feed in feeds)
        {
            var id = feed.StopId;
            feed.Arrivals = Poll(_refreshInterval, ct => _api.GetArrivalsAsync(id, ct), cts.Token);
            feed.Label = Poll(_labelRefreshInterval, ct => _api.GetStopLabelAsync(id, ct), cts.Token);
        }

        PublishFeeds(feeds);
    }

    private void PublishFeeds(BusStopFeed[] feeds)
    {
        _feeds = feeds;
        _tokens = feeds.Select((f, i) => new StopToken(i, f.StopId)).ToArray();
    }

    internal IReadOnlyList<BusStopFeed> Feeds => _feeds;
    internal Pager? StopPager => _pager;

    /// <summary>Test seam: replaces the polled data with fixed sources (call before the first frame).</summary>
    internal void UseData(params (string StopId, ILiveData<TflArrival[]> Arrivals, ILiveData<string> Label)[] stops)
    {
        _pollCts?.Cancel();
        PublishFeeds(stops.Select(s => new BusStopFeed(s.StopId) { Arrivals = s.Arrivals, Label = s.Label }).ToArray());
    }
}
