using System.Numerics;
using LedMatrixOS.Apps.Commute;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// The "do I need to leave yet?" board. The hero is the time until you must leave to catch the first train you can still walk to
/// (green, amber, then a flashing GO), with how long after the previous train it comes (a long gap means a packed train) and how long the
/// wait is if you miss it, next to the current weather. The strip along the bottom carries the status of the commute's lines (those of the
/// chosen routes plus any watched further along the journey), spelling out anything worse than minor delays, and the clock.
/// Reuses the Tube departures data and the Open-Meteo weather source; the maths lives in <see cref="CommutePlanner"/>.
/// </summary>
[LegacySettingKey("stationSearch")]
[LegacySettingKey("stationSelect", "stationId")]
public class CommuteApp : WidgetApp
{
    public override string Id => "commute";
    public override string Name => "Commute";
    public override int FrameRate => 30;

    /// <summary>Trains kept on the (unseen) board: enough to judge the usual interval even at a busy interchange.</summary>
    private const int MaxTrains = 24;

    private static readonly IReadOnlyList<LineStatus> NoStatuses = [];


    [Setting("Station", Description = "Search for a station (e.g. Baker Street).", Search = true)]
    public string StationId { get; set; } = "";

    [Setting("Routes", Description = "Only trains on these lines and directions (e.g. Metropolitan towards Aldgate). Leave empty for all.", MultiSearch = true, Browse = true)]
    public string Routes { get; set; } = "";

    [Setting("Watch Lines", Description = "Other lines your journey uses (e.g. Central if you change onto it). Their status shows alongside your station's.", MultiSearch = true, Browse = true)]
    public string WatchLines { get; set; } = "";

    [Setting("Platform Filter", Description = "Advanced: only trains whose platform contains this text (e.g. 'Eastbound'). Prefer Routes; leave empty for all.", Advanced = true)]
    public string PlatformFilter { get; set; } = "";

    [Setting("Walk Minutes", Description = "How long it takes you to get to the platform.", Min = 0, Max = 60)]
    public int WalkMinutes { get; set; } = 8;

    [Setting("Location", Description = "Search for a place for the weather.", Search = true)]
    public string Location { get; set; } = "London";

    [Setting("Units", Description = "Temperature units.", Options = ["Celsius", "Fahrenheit"])]
    public string Units { get; set; } = "Celsius";

    private readonly TflApi _api;
    private IWeatherSource _weatherSource;
    private readonly DepartureBoardModel _model = new();
    private readonly DepartureHistory _history = new();

    private volatile ILiveData<TflArrival[]>? _arrivals;
    private volatile ILiveData<LineStatus[]>? _lineStatuses;
    private volatile ILiveData<WeatherSnapshot>? _weather;
    private CancellationTokenSource? _stationCts, _statusCts, _weatherCts;
    private bool _active, _locationFromUser;

    private LeaveCard? _card;
    private WeatherChip? _chip;
    private StateScreen? _state;
    private Node? _body, _strip;
    private bool _entered, _bodyShown;

    public CommuteApp(HttpClient httpClient)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        _api = new TflApi(httpClient);
        _weatherSource = new OpenMeteoWeatherSource(httpClient);
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _card = new LeaveCard();
        _chip = new WeatherChip { Width = 56 };
        _state = new StateScreen(Fonts.Big, Fonts.Small);

        _body = new Stack(Orientation.Horizontal, gap: 2)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            CrossAlign = Align.Stretch,
            Children = { new Panel { Grow = 1, Children = { _card } }, _chip },
        };

        var clock = new TextStyle(Fonts.Small, new Pixel(245, 245, 245), Shadow: false);
        _strip = new Panel
        {
            Height = 12,
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
                        new ListView<LineStatus>(() => _lineStatuses?.Value ?? NoStatuses, s => new LinePill(s, Fonts.QuiteSmall) { QuietMinor = true }, s => s.LineId)
                            { Orientation = Orientation.Horizontal, Gap = 1, CrossAlign = Align.Center, EnterOffset = 6, ItemChanged = (n, s) => ((LinePill)n).Apply(s) },
                        new CommuteStatusLine(() => _lineStatuses?.Value),
                        new Clock("HH:mm", Time) { Style = clock },
                    },
                },
            },
        };

        return new Dock { Bottom = _strip, Fill = new Panel { _body, _state } };
    }

    // ---- per frame --------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        _model.Refresh(context.Time, _arrivals?.Value, PlatformFilter, MaxTrains, routes: Routes);
        _history.Observe(_model.Visible, context.Time);

        bool hasTrains = _model.Visible.Count > 0;
        // With routes picked, any of them will do; otherwise only the target's own line and direction says anything about the gaps.
        bool sameRoute = string.IsNullOrWhiteSpace(Routes);
        _card!.Plan = CommutePlanner.Plan(_model.Visible, context.Time, WalkMinutes, sameRoute, _history);
        _chip!.Snapshot = _weather?.Value;
        _chip.Fahrenheit = Units == "Fahrenheit";

        _state!.State = StateFor();
        _state.Visible = !hasTrains;
        _body!.Visible = hasTrains;

        base.Update(context, cancellationToken);

        if (hasTrains && !_bodyShown) Motion.SlideIn(_body, Animator, new Vector2(0, 14), TimeSpan.Zero, 380.Ms());
        _bodyShown = hasTrains;
        if (!_entered)
        {
            _entered = true;
            Motion.SlideIn(_strip!, Animator, new Vector2(0, 14), 150.Ms());
            Motion.SlideIn(_state, Animator, new Vector2(-120, 0), TimeSpan.Zero, 500.Ms(), Easing.OutBack);
        }
    }

    private BoardState StateFor()
    {
        if (string.IsNullOrWhiteSpace(StationId)) return BoardState.NoStation;
        var arrivals = _arrivals;
        if (arrivals is { Value: null, Error: not null }) return BoardState.Offline;
        return arrivals?.Value is null ? BoardState.Loading : BoardState.NoTrains;
    }

    // ---- lifecycle & settings -----------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);

        _api.AppKey = configuration["TFL:AppKey"];
        if (string.Equals(configuration["Weather:Source"], "Fake", StringComparison.OrdinalIgnoreCase)) _weatherSource = new FakeWeatherSource();

        if (string.IsNullOrEmpty(StationId))
            StationId = configuration["Commute:StationId"] ?? configuration["TubeDeparturesApp:StationId"] ?? "";
        if (configuration["Commute:PlatformFilter"] is { Length: > 0 } platform && string.IsNullOrEmpty(PlatformFilter)) PlatformFilter = platform;
        if (int.TryParse(configuration["Commute:WalkMinutes"], out var walk)) WalkMinutes = Math.Clamp(walk, 0, 60);
        if (!_locationFromUser && configuration["Weather:Location"] is { Length: > 0 } loc) Location = loc;

        _active = true;
        _entered = _bodyShown = false;
        RestartStationPolling();
        RestartWeatherPolling();
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        CancelPoll(ref _stationCts);
        CancelPoll(ref _statusCts);
        CancelPoll(ref _weatherCts);
        await base.OnDeactivatedAsync(cancellationToken);
    }

    protected override void OnSettingChanged(string key)
    {
        if (!_active) return;
        switch (key)
        {
            case "stationId": RestartStationPolling(); break;
            case "routes": _history.Clear(); RestartStatusPolling(); break;
            case "watchLines": RestartStatusPolling(); break;
            case "location": _locationFromUser = true; RestartWeatherPolling(); break;
            case "units": RestartWeatherPolling(); break;
        }
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    private void RestartStationPolling()
    {
        CancelPoll(ref _stationCts);
        _arrivals = null;
        _model.Reset();
        _history.Clear();

        var stationId = StationId;
        if (!string.IsNullOrWhiteSpace(stationId))
        {
            var scope = RestartPollScope(ref _stationCts);
            _arrivals = Poll(TimeSpan.FromSeconds(30), ct => _api.GetArrivalsAsync(stationId, ct), scope);
        }
        RestartStatusPolling();
    }

    /// <summary>
    /// The commute's lines: those of the chosen routes, or every line at the station when none are chosen, plus the watched lines.
    /// Statuses are polled every 5 minutes; with no routes chosen the station's lines are only known once arrivals have loaded.
    /// </summary>
    private void RestartStatusPolling()
    {
        CancelPoll(ref _statusCts);
        _lineStatuses = null;

        var watched = ParseLines(WatchLines);
        var routeLines = RouteLines(Routes);
        var arrivals = _arrivals;
        bool fromStation = routeLines.Length == 0 && arrivals is not null;
        if (!fromStation && routeLines.Length == 0 && watched.Length == 0) return;

        var scope = RestartPollScope(ref _statusCts);
        _lineStatuses = Poll(TimeSpan.FromMinutes(5), async ct =>
        {
            var lines = routeLines;
            if (fromStation)
            {
                while ((lines = TflLookups.LineIdsOf(arrivals!.Value)).Length == 0) await Task.Delay(250, ct);
            }

            var all = lines.Concat(watched).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var statuses = await _api.GetLineStatusesAsync(all, ct);
            // Keep the rider's order: their station's lines first, then the lines further along
            return statuses.OrderBy(s => Array.FindIndex(all, id => id.Equals(s.LineId, StringComparison.OrdinalIgnoreCase))).ToArray();
        }, scope);
    }

    /// <summary>The distinct line ids of a comma separated list of route keys ("jubilee|Eastbound,central|Westbound").</summary>
    internal static string[] RouteLines(string? routes) => (routes ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(key => key.Split('|')[0].Trim().ToLowerInvariant())
        .Where(id => id.Length > 0)
        .Distinct()
        .ToArray();

    /// <summary>The distinct line ids of a comma separated list.</summary>
    internal static string[] ParseLines(string? lines) => (lines ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(id => id.ToLowerInvariant())
        .Distinct()
        .ToArray();

    private void RestartWeatherPolling()
    {
        var query = new WeatherQuery(Location, Units == "Fahrenheit");
        _weather = RestartPoll(ref _weatherCts, TimeSpan.FromMinutes(10), ct => _weatherSource.GetAsync(query, ct));
    }

    internal CommutePlan CurrentPlan => _card?.Plan ?? CommutePlan.None;
    internal DepartureBoardModel Board => _model;

    /// <summary>Test seam: replaces the polled data with fixed sources (call before the first frame).</summary>
    internal void UseData(ILiveData<TflArrival[]>? arrivals, ILiveData<LineStatus[]>? statuses, ILiveData<WeatherSnapshot>? weather)
    {
        _arrivals = arrivals;
        _lineStatuses = statuses;
        _weather = weather;
        _model.Reset();
        _history.Clear();
    }
}
