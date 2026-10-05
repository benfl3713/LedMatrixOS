using LedMatrixOS.Apps.Cycle;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// Santander Cycles docks plus a "ride or Tube?" verdict. The left side pages through up to three docking stations (bikes available,
/// e-bikes, free docks); the right side shows the verdict from the next hours of rain, wind and temperature (<see cref="RideVerdict"/>)
/// with a rain sparkline. Docks come from the TfL BikePoint API, the forecast from Open-Meteo.
/// </summary>
[LegacySettingKey("dockSearch")]
[LegacySettingKey("dockSelect", "dockIds")]
public class CycleHubApp : WidgetApp
{
    public override string Id => "cycle-hub";
    public override string Name => "Cycle Hub";
    public override int FrameRate => 30;

    public const int MaxDocks = 3;
    private const int CardWidth = 104;
    private const int StripHeight = 12;
    private static readonly IReadOnlyList<DockToken> NoTokens = [];
    private static readonly float[] NoRain = [];

    [Setting("Docks", Description = "Search for docking stations, up to 3.", MultiSearch = true, Max = MaxDocks)]
    public string DockIds { get; set; } = "";

    [Setting("Page Seconds", Description = "How long each dock stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 8;

    [Setting("Units", Description = "Temperature and wind units for the forecast.", Options = ["Celsius", "Fahrenheit"])]
    public string Units { get; set; } = "Celsius";

    private readonly TflApi _api;
    private IWeatherSource _weatherSource;
    private readonly TimeSpan _dockInterval = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _weatherInterval = TimeSpan.FromMinutes(10);

    private volatile DockFeed[] _feeds = [];
    private IReadOnlyList<DockToken> _tokens = NoTokens;
    private volatile ILiveData<WeatherSnapshot>? _weather;
    private CancellationTokenSource? _dockCts, _weatherCts;
    private string _location = "London";
    private bool _locationFromUser;

    private WeatherSnapshot? _outlookFor;
    private RideOutlook? _outlook;
    private int _verdictStyle = -1;

    private CycleStyles _styles = null!;
    private Pager? _pager;
    private Label? _message, _verdict;
    private Pill? _pill;
    private bool _entered, _boardShown;

    [ActivatorUtilitiesConstructor]
    public CycleHubApp(HttpClient httpClient) : this(httpClient, new OpenMeteoWeatherSource(httpClient)) { }

    public CycleHubApp(HttpClient httpClient, IWeatherSource weatherSource)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        _api = new TflApi(httpClient);
        _weatherSource = weatherSource;
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _styles = new CycleStyles();
        var styles = _styles;

        _pager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Left) { Duration = 500.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1 }
            .Bind(() => _tokens, token => new DockPage(Array.Find(_feeds, f => f.DockId == token.DockId) ?? new DockFeed(token.DockId), styles));

        _message = new Label(() => MessageText()) { Style = styles.Message, HAlign = Align.Center, VAlign = Align.Center, Visible = false };
        var left = new Panel { _pager, _message };

        _verdict = new Label(() => VerdictText()) { Style = styles.Verdict[^1] };
        var card = new Panel
        {
            Width = CardWidth,
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children =
            {
                new Stack(Orientation.Vertical, gap: 1)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    Padding = new Thickness(7, 2, 4, 1),
                    Children =
                    {
                        new Label("RIDE OR TUBE?") { Style = styles.Muted },
                        _verdict,
                        new Sparkline { Source = () => _outlook?.Rain ?? NoRain, Min = 0, Max = 100, NaturalHeight = 12, Line = CycleStyles.Free, FillBrightness = 0.3f, Grow = 1 },
                        new Label(() => _outlook?.WindText ?? "") { Style = styles.Muted },
                    },
                },
                new Divider(Orientation.Vertical) { Color = new Pixel(255, 255, 255).WithBrightness(0.3f), HAlign = Align.Start },
            },
        };

        _pill = new Pill("--", CycleStyles.Neutral.WithBrightness(0.6f)) { Style = styles.PillText, Height = 9, Width = 40 };
        var strip = new Panel
        {
            HAlign = Align.Stretch,
            Height = StripHeight,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 4)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(4, 1),
                    Children =
                    {
                        new Clock("HH:mm", Time) { Style = styles.Clock },
                        new Label("Santander Cycles") { Style = styles.Muted, Grow = 1 },
                        _pill,
                    },
                },
            },
        };

        return new Dock { Bottom = strip, Right = card, Fill = left };
    }

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        bool anyDock = false;
        foreach (var feed in _feeds)
            if (feed.Info?.Value is not null) anyDock = true;

        _message!.Visible = !anyDock;
        _pager!.Visible = anyDock;

        UpdateOutlook();
        base.Update(context, cancellationToken);

        if (anyDock && !_boardShown) Motion.SlideIn(_pager, Animator, new System.Numerics.Vector2(0, 16), TimeSpan.Zero, 380.Ms());
        _boardShown = anyDock;
        if (!_entered)
        {
            _entered = true;
            Motion.SlideIn(_message, Animator, new System.Numerics.Vector2(-120, 0), TimeSpan.Zero, 500.Ms(), Easing.OutBack);
        }
    }

    /// <summary>Derives the verdict card content when a new forecast arrives (once per snapshot, not per frame).</summary>
    private void UpdateOutlook()
    {
        var snap = _weather?.Value;
        if (!ReferenceEquals(snap, _outlookFor))
        {
            _outlookFor = snap;
            _outlook = snap is null ? null : RideOutlook.From(snap);
        }

        int style = _outlook is { } o ? (int)o.Ride : _styles.Verdict.Length - 1;
        if (style == _verdictStyle) return;
        _verdictStyle = style;
        _verdict!.Style = _styles.Verdict[style];
        if (_outlook is { } outlook)
        {
            _pill!.Text = RideVerdict.Label(outlook.Ride);
            _pill.Background = RideVerdict.ColorOf(outlook.Ride).WithBrightness(0.75f);
        }
        else
        {
            _pill!.Text = "--";
            _pill.Background = CycleStyles.Neutral.WithBrightness(0.6f);
        }
    }

    private string VerdictText() =>
        _outlook is { } o ? RideVerdict.Label(o.Ride)
        : _weather?.Error is not null ? "OFFLINE"
        : "...";

    private string MessageText()
    {
        if (_feeds.Length == 0) return "Choose a dock in the app";
        foreach (var feed in _feeds)
        {
            var info = feed.Info;
            if (info is { Value: null, Error: not null }) return "No data, retrying";
            if (info?.Value is null) return "Loading docks";
        }

        return "No data";
    }

    // ---- settings ---------------------------------------------------------------------------------------------------------------

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "dockIds": RestartDockPolling(); break;
            case "units": RestartWeatherPolling(); break;
            case "pageSeconds":
                if (_pager is not null) _pager.Interval = PageSeconds.Seconds();
                break;
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _api.AppKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_api.AppKey)) Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");

        if (configuration["CycleHub:DockIds"] is { Length: > 0 } docks) DockIds = docks;
        if (string.Equals(configuration["Weather:Source"], "Fake", StringComparison.OrdinalIgnoreCase)) _weatherSource = new FakeWeatherSource();
        if (!_locationFromUser)
        {
            if (configuration["CycleHub:Location"] is { Length: > 0 } cycleLocation) _location = cycleLocation;
            else if (configuration["Weather:Location"] is { Length: > 0 } location) _location = location;
        }

        _entered = _boardShown = false;
        RestartDockPolling();
        RestartWeatherPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        CancelPoll(ref _dockCts);
        CancelPoll(ref _weatherCts);
        return base.OnDeactivatedAsync(cancellationToken);
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    /// <summary>The configured dock IDs: trimmed, distinct, at most <see cref="MaxDocks"/>.</summary>
    internal static string[] ParseDockIds(string? ids) => (ids ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxDocks)
        .ToArray();

    private void RestartDockPolling()
    {
        var scope = RestartPollScope(ref _dockCts);

        var feeds = ParseDockIds(DockIds).Select(id => new DockFeed(id)).ToArray();
        foreach (var feed in feeds)
        {
            var id = feed.DockId;
            feed.Info = Poll(_dockInterval, ct => _api.GetBikePointAsync(id, ct), scope);
        }

        PublishFeeds(feeds);
    }

    private void RestartWeatherPolling()
    {
        var query = new WeatherQuery(_location, Units == "Fahrenheit");
        _weather = RestartPoll(ref _weatherCts, _weatherInterval, ct => _weatherSource.GetAsync(query, ct));
    }

    private void PublishFeeds(DockFeed[] feeds)
    {
        _feeds = feeds;
        _tokens = feeds.Select((f, i) => new DockToken(i, f.DockId)).ToArray();
    }

    internal IReadOnlyList<DockFeed> Feeds => _feeds;
    internal Pager? DockPager => _pager;
    internal Ride? CurrentRide => _outlook?.Ride;

    /// <summary>Test seam: replaces the polled data with fixed sources (call before the first frame).</summary>
    internal void UseData(ILiveData<WeatherSnapshot>? weather, params (string DockId, ILiveData<BikePointInfo> Info)[] docks)
    {
        CancelPoll(ref _dockCts);
        CancelPoll(ref _weatherCts);
        _weather = weather;
        PublishFeeds(docks.Select(d => new DockFeed(d.DockId) { Info = d.Info }).ToArray());
    }
}
