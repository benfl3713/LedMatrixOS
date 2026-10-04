using LedMatrixOS.Apps.Briefing;
using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Apps.Calendar;
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
/// A one-shot morning briefing: full-width cards (greeting, weather, calendar, commute, bins, sign-off) shown one after another with a slide,
/// <c>Page Seconds</c> each, stopping on the sign-off. Cards whose data is missing or not configured are skipped, so only greeting and sign-off may remain.
/// The data comes from the same adapters as the Home chips, Commute and Bin Day apps (feed URLs and keys stay configuration: <c>Calendar:IcsUrl</c>,
/// <c>TFL:AppKey</c>, <c>Weather:Location</c>). Activating the app again replays from the first card; give the playlist entry <see cref="TotalSeconds"/>.
/// </summary>
public class MorningBriefingApp : WidgetApp
{
    public override string Id => "morning-briefing";
    public override string Name => "Morning Briefing";
    public override int FrameRate => 30;

    /// <summary>Length of the slide between two cards.</summary>
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(500);

    [Setting("Station Search", Description = "Type a station name (e.g. Baker Street), then choose it in Station Select.")]
    public string StationSearch { get; set; } = "";

    [Setting("Station ID", Description = "TfL Naptan ID for the commute card (filled in when you choose a result from Station Select). Empty skips the card.")]
    public string StationId { get; set; } = "";

    [Setting("Walk Minutes", Description = "How long it takes you to get to the platform.", Min = 0, Max = 60)]
    public int WalkMinutes { get; set; } = 8;

    [Setting("Page Seconds", Description = "How long each card is on screen, including its slide.", Min = 3, Max = 20)]
    public int PageSeconds { get; set; } = 6;

    [Setting("Bins", Description = "Collection rules, same syntax as Bin Day: " + BinParser.BinSyntax + ". The bins card appears only when something is due today or tomorrow.")]
    public string Bins { get; set; } = "";

    [Setting("Show Weather", Description = "Include the weather card.")]
    public bool ShowWeather { get; set; } = true;

    [Setting("Show Calendar", Description = "Include today's events (needs Calendar:IcsUrl in the configuration).")]
    public bool ShowCalendar { get; set; } = true;

    [Setting("Show Commute", Description = "Include the leave-in card (needs a station).")]
    public bool ShowCommute { get; set; } = true;

    [Setting("Show Bins", Description = "Include the bins card when bins are due.")]
    public bool ShowBins { get; set; } = true;

    [Setting("Units", Description = "Temperature units.", Options = ["Celsius", "Fahrenheit"])]
    public string Units { get; set; } = "Celsius";

    /// <summary>Where each card's data comes from. A null source means that card is not configured and is skipped.</summary>
    internal sealed record BriefingSources(
        Func<bool, CancellationToken, Task<WeatherSnapshot>>? Weather,
        Func<CancellationToken, Task<List<CalEvent>>>? Events,
        Func<string, CancellationToken, Task<TflArrival[]>>? Arrivals,
        Func<ILiveData<TflArrival[]>, CancellationToken, Task<LineStatus[]>>? Statuses);

    private readonly HttpClient? _http;
    private readonly TflStopPicker _picker;
    private readonly BriefingModel _model = new();
    private readonly BriefingProgress _progress = new();

    private BriefingSources? _sources;
    private bool _sourcesInjected, _dataInjected, _active;
    private CancellationTokenSource? _cts;
    private volatile ILiveData<WeatherSnapshot>? _weather;
    private volatile ILiveData<List<CalEvent>>? _events;
    private volatile ILiveData<TflArrival[]>? _arrivals;
    private volatile ILiveData<LineStatus[]>? _statuses;

    private Pager? _pager;
    private int _appliedSeconds = -1, _shownIndex = -1;
    private TimeSpan _shownSince;

    public MorningBriefingApp() : this(null) { }

    [ActivatorUtilitiesConstructor]
    public MorningBriefingApp(HttpClient? http)
    {
        _http = http;
        if (http is not null)
        {
            try { http.Timeout = TimeSpan.FromSeconds(20); } catch (InvalidOperationException) { }
        }

        _picker = new TflStopPicker("stationSearch", "stationSelect", "Station Select", "Choose a result to set the station automatically.",
            () => StationSearch, (q, ct) => new TflApi(http ?? new HttpClient()) { AppKey = _appKey }.SearchStationsAsync(q, ct),
            id =>
            {
                StationId = id;
                if (_active) StartPolls();
            }, RunInBackground);
    }

    private string? _appKey;

    // ---- public surface for the playlist and tests ------------------------------------------------------------------------------------

    /// <summary>The cards that are currently in the sequence (greeting and sign-off always; the rest when their data is there).</summary>
    public int PageCount => _model.Pages.Count;

    /// <summary>How long the whole briefing takes: cards x Page Seconds. Use it as the playlist entry's length.</summary>
    public int TotalSeconds => PageCount * Math.Clamp(PageSeconds, 3, 20);

    internal IReadOnlyList<BriefingPage> Pages => _model.Pages;
    internal Pager? Pager => _pager;
    internal BriefingModel Model => _model;
    internal BriefingProgress Progress => _progress;

    // ---- view ---------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var transition = new SlideTransition(MoveDirection.Left) { Duration = SlideTime };
        _pager = new Pager(1, TimeSpan.FromSeconds(3), transition, Easing.InOutCubic) { Loop = false }
            .Bind(() => _model.Pages, page => BriefingNodes.Card(page, _model, Animator));
        _appliedSeconds = -1;
        _shownIndex = -1;
        return new Dock { Bottom = new ProgressStrip(_progress), Fill = _pager };
    }

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        _model.Refresh(new BriefingInputs(
            Time.GetLocalNow(), Time.LocalTimeZone, context.Time,
            _weather?.Value, _events?.Value, _arrivals?.Value, _statuses?.Value,
            ShowWeather, ShowCalendar, ShowCommute, ShowBins, StationId ?? "", WalkMinutes, Bins ?? ""));

        // A card's whole cycle (rest plus slide) is Page Seconds, so N cards take exactly N x Page Seconds and the sign-off gets its full share.
        int seconds = Math.Clamp(PageSeconds, 3, 20);
        if (seconds != _appliedSeconds)
        {
            _appliedSeconds = seconds;
            _pager!.Interval = TimeSpan.FromSeconds(seconds) - SlideTime;
        }

        base.Update(context, cancellationToken);

        int count = _model.Pages.Count;
        int index = Math.Min(count - 1, _pager!.PageIndex + (_pager.IsTransitioning ? 1 : 0));
        if (index != _shownIndex)
        {
            _shownIndex = index;
            _shownSince = context.Time;
        }

        _progress.Count = count;
        _progress.Index = Math.Max(0, index);
        _progress.Fraction = (float)((context.Time - _shownSince).TotalSeconds / seconds);
        if (index >= 0) _progress.Accent = BriefingNodes.AccentOf(_model.Pages[index]);
    }

    // ---- lifecycle & settings -----------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);

        _appKey = configuration["TFL:AppKey"];
        if (!_sourcesInjected) _sources = BuildSources(configuration);
        if (string.IsNullOrEmpty(StationId)) StationId = configuration["Commute:StationId"] ?? "";
        if (Bins.Length == 0 && configuration["BinDay:Bins"] is { Length: > 0 } bins) Bins = bins;

        _model.Reset();
        _shownIndex = -1;
        _active = true;
        StartPolls();
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        StopPolls();
        await base.OnDeactivatedAsync(cancellationToken);
    }

    // stationSelect has options that change as the user types, so it is not a [Setting] property.
    public override IEnumerable<AppSetting> GetSettings()
    {
        foreach (var setting in _picker.WithSelect(base.GetSettings()))
        {
            if (setting.Key == "pageSeconds")
            {
                int s = Math.Clamp(PageSeconds, 3, 20);
                string total = PageCount > 0
                    ? $" Total Seconds (read only): {PageCount} cards x {s} s = {TotalSeconds} s; make the playlist entry at least that long."
                    : $" Total Seconds (read only): number of cards x {s} s, known once the app is running.";
                yield return setting with { Description = setting.Description + total };
            }
            else yield return setting;
        }
    }

    public override void UpdateSetting(string key, object value)
    {
        if (!_picker.TryUpdate(key, value)) base.UpdateSetting(key, value);
    }

    protected override void OnSettingChanged(string key)
    {
        if (key == "stationSearch") _picker.OnQueryChanged();
        if (_active && key is "stationId" or "units" or "showWeather" or "showCalendar" or "showCommute") StartPolls();
    }

    // ---- data ---------------------------------------------------------------------------------------------------------------------

    private BriefingSources BuildSources(IConfiguration config)
    {
        var http = _http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // Weather: the same configuration as WeatherApp (Weather:Source = Fake, Weather:Location).
        IWeatherSource weather = string.Equals(config["Weather:Source"], "Fake", StringComparison.OrdinalIgnoreCase)
            ? new FakeWeatherSource()
            : new OpenMeteoWeatherSource(http);
        string location = config["Weather:Location"] is { Length: > 0 } loc ? loc : "London";

        // Calendar: the private feed URL is configuration only, like CalendarApp.
        var ics = (config["Calendar:IcsUrl"] ?? "").Trim();
        Func<CancellationToken, Task<List<CalEvent>>>? events = null;
        if (ics.Length > 0)
        {
            var url = ics.StartsWith("webcal", StringComparison.OrdinalIgnoreCase) ? "https" + ics[6..] : ics;
            var zone = Time.LocalTimeZone;
            events = async ct =>
            {
                var text = await http.GetStringAsync(url, ct);
                var now = DateTimeOffset.UtcNow;
                return IcsParser.Parse(text, now.AddDays(-1), now.AddDays(2), zone);
            };
        }

        var api = new TflApi(http) { AppKey = config["TFL:AppKey"] };
        return new BriefingSources(
            (fahrenheit, ct) => weather.GetAsync(new WeatherQuery(location, fahrenheit), ct),
            events,
            api.GetArrivalsAsync,
            (arrivals, ct) => TflStopPicker.FetchLineStatusesAsync(api, arrivals, ct));
    }

    private void StopPolls()
    {
        _cts?.Cancel();
        _cts = null;
        if (_dataInjected) return;
        _weather = null;
        _events = null;
        _arrivals = null;
        _statuses = null;
    }

    private void StartPolls()
    {
        StopPolls();
        if (_dataInjected || _sources is not { } s) return;

        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        bool fahrenheit = Units == "Fahrenheit";
        if (ShowWeather && s.Weather is not null) _weather = Poll(TimeSpan.FromMinutes(10), c => s.Weather(fahrenheit, c), ct);
        if (ShowCalendar && s.Events is not null) _events = Poll(TimeSpan.FromMinutes(15), s.Events, ct);

        string station = (StationId ?? "").Trim();
        if (ShowCommute && station.Length > 0 && s.Arrivals is not null)
        {
            var arrivals = Poll(TimeSpan.FromSeconds(30), c => s.Arrivals(station, c), ct);
            _arrivals = arrivals;
            if (s.Statuses is not null) _statuses = Poll(TimeSpan.FromMinutes(5), c => s.Statuses(arrivals, c), ct);
        }
    }

    /// <summary>Test seam: replaces every source with fakes (call before activating). Polls then run for the switched-on cards.</summary>
    internal void UseSources(BriefingSources sources)
    {
        _sources = sources;
        _sourcesInjected = true;
        _dataInjected = false;
    }

    /// <summary>Test seam: fixed data as if the feeds had loaded (call before the first frame). A null feed means that card is not configured.</summary>
    internal void UseData(ILiveData<WeatherSnapshot>? weather = null, ILiveData<List<CalEvent>>? events = null,
        ILiveData<TflArrival[]>? arrivals = null, ILiveData<LineStatus[]>? statuses = null)
    {
        _dataInjected = true;
        _weather = weather;
        _events = events;
        _arrivals = arrivals;
        _statuses = statuses;
    }
}
