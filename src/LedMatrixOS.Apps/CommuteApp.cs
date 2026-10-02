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
/// (green, amber, then a flashing GO), next to the current weather; the strip along the bottom carries the status of every line serving the
/// station, and the clock. Reuses the Tube departures data and the Open-Meteo weather source; the maths lives in <see cref="CommutePlanner"/>.
/// </summary>
public class CommuteApp : WidgetApp
{
    public override string Id => "commute";
    public override string Name => "Commute";
    public override int FrameRate => 30;

    private static readonly IReadOnlyList<LineStatus> NoStatuses = [];

    private const string NoStationSearchHint = "Type at least 2 chars";

    [Setting("Station Search", Description = "Type a station name (e.g. Baker Street), then choose it in Station Select.")]
    public string StationSearch { get; set; } = "";

    [Setting("Station ID", Description = "TfL Naptan ID (filled in when you choose a result from Station Select).")]
    public string StationId { get; set; } = "";

    [Setting("Platform Filter", Description = "Only trains whose platform contains this text (e.g. 'Eastbound'). Leave empty for all.")]
    public string PlatformFilter { get; set; } = "";

    [Setting("Walk Minutes", Description = "How long it takes you to get to the platform.", Min = 0, Max = 60)]
    public int WalkMinutes { get; set; } = 8;

    [Setting("Location", Description = "Place name (e.g. London) or coordinates as 'lat,lon' for the weather.")]
    public string Location { get; set; } = "London";

    [Setting("Units", Description = "Temperature units.", Options = ["Celsius", "Fahrenheit"])]
    public string Units { get; set; } = "Celsius";

    private readonly TflApi _api;
    private IWeatherSource _weatherSource;
    private readonly DepartureBoardModel _model = new();

    private volatile ILiveData<TflArrival[]>? _arrivals;
    private volatile ILiveData<LineStatus[]>? _lineStatuses;
    private volatile ILiveData<WeatherSnapshot>? _weather;
    private CancellationTokenSource? _stationCts, _weatherCts;
    private bool _active, _locationFromUser;

    private volatile string[] _stationSearchOptions = [NoStationSearchHint];
    private string _stationSearchLastQuery = "";

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
        _chip = new WeatherChip { Width = 90 };
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
                    CrossAlign = Align.Center,
                    Padding = new Thickness(2, 1, 3, 1),
                    Children =
                    {
                        new ListView<LineStatus>(() => _lineStatuses?.Value ?? NoStatuses, s => new LinePill(s, Fonts.QuiteSmall), s => s.LineId)
                            { Orientation = Orientation.Horizontal, Gap = 1, CrossAlign = Align.Center, EnterOffset = 6, ItemChanged = (n, s) => ((LinePill)n).Apply(s) },
                        new Panel { Grow = 1 },
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
        _model.Refresh(context.Time, _arrivals?.Value, PlatformFilter, 12);

        bool hasTrains = _model.Visible.Count > 0;
        _card!.Plan = CommutePlanner.Plan(_model.Visible, context.Time, WalkMinutes);
        _card.WalkMinutes = WalkMinutes;
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
        _stationCts?.Cancel();
        _weatherCts?.Cancel();
        await base.OnDeactivatedAsync(cancellationToken);
    }

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
        if (split > 0 && selected[..split].Trim() is { Length: > 0 } id)
        {
            StationId = id;
            if (_active) RestartStationPolling();
        }
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
            if (string.Equals(query, _stationSearchLastQuery, StringComparison.OrdinalIgnoreCase)) return;

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

    protected override void OnSettingChanged(string key)
    {
        if (key == "stationSearch") SearchStations();
        if (!_active) return;
        switch (key)
        {
            case "stationId": RestartStationPolling(); break;
            case "location": _locationFromUser = true; RestartWeatherPolling(); break;
            case "units": RestartWeatherPolling(); break;
        }
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    private void RestartStationPolling()
    {
        _stationCts?.Cancel();
        _arrivals = null;
        _lineStatuses = null;
        _model.Reset();

        var stationId = StationId;
        if (string.IsNullOrWhiteSpace(stationId)) return;

        var cts = _stationCts = new CancellationTokenSource();
        var arrivals = Poll(TimeSpan.FromSeconds(30), ct => _api.GetArrivalsAsync(stationId, ct), cts.Token);
        _arrivals = arrivals;
        _lineStatuses = Poll(TimeSpan.FromMinutes(5), ct => FetchLineStatusesAsync(arrivals, ct), cts.Token);
    }

    private async Task<LineStatus[]> FetchLineStatusesAsync(ILiveData<TflArrival[]> arrivals, CancellationToken ct)
    {
        string[] lineIds;
        while ((lineIds = LineIdsOf(arrivals.Value)).Length == 0) await Task.Delay(250, ct);
        return await _api.GetLineStatusesAsync(lineIds, ct);
    }

    private static string[] LineIdsOf(TflArrival[]? arrivals) => (arrivals ?? [])
        .Where(a => !string.IsNullOrWhiteSpace(a.LineId)).Select(a => a.LineId)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id).ToArray();

    private void RestartWeatherPolling()
    {
        _weatherCts?.Cancel();
        var cts = _weatherCts = new CancellationTokenSource();
        var query = new WeatherQuery(Location, Units == "Fahrenheit");
        _weather = Poll(TimeSpan.FromMinutes(10), ct => _weatherSource.GetAsync(query, ct), cts.Token);
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
    }
}
