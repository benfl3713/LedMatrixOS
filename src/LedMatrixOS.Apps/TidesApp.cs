using LedMatrixOS.Apps.Tides;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// An animated water surface that rises and falls with the tide at a coastal place, with a bobbing buoy and the next high and low water
/// (time and height) across the top. Tide heights come from an <see cref="ITideSource"/> (Open-Meteo's marine API, no key); while that is loading or
/// unavailable a sinusoidal model keeps the water moving, and the place is marked "est." so it is not mistaken for real data.
/// </summary>
public class TidesApp : WidgetApp
{
    public override string Id => "tides";
    public override string Name => "Tides";
    public override int FrameRate => 20;

    [Setting("Location", Description = "Search for a coastal place.", Search = true)]
    public string Location { get; set; } = "Brighton";

    private readonly ITideSource _source;
    private volatile ILiveData<TideForecast>? _data;
    private CancellationTokenSource? _pollCts;
    private bool _active, _locationFromUser;

    private TideNode? _node;
    private TideForecast? _model;

    // What the labels were last built from, so they are only rebuilt (and allocate) when it changes.
    private TideEvent? _shownFirst, _shownSecond;
    private int _shownTenths = int.MinValue;
    private bool _shownRising, _shownEstimate, _shownAny;
    private string _shownPlace = "";
    private TimeZoneInfo? _shownZone;

    public TidesApp(HttpClient httpClient) : this(new OpenMeteoTideSource(Configure(httpClient)))
    {
    }

    internal TidesApp(ITideSource source) => _source = source;

    private static HttpClient Configure(HttpClient http)
    {
        http.Timeout = TimeSpan.FromSeconds(10);
        return http;
    }

    protected override Node Build() => _node = new TideNode();

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;
        var node = _node!;
        var now = Time.GetUtcNow();
        var forecast = ActiveForecast(now);

        double level = forecast.LevelAt(now);
        double range = Math.Max(0.1, forecast.High - forecast.Low);
        node.Level = (level - forecast.Low) / range;
        node.Seconds = context.Time.TotalSeconds;

        bool rising = forecast.LevelAt(now + TimeSpan.FromMinutes(10)) > level;
        forecast.TryNext(now, out var first, out var second);
        UpdateLabels(node, forecast, first, second, level, rising);

        base.Update(context, cancellationToken);
    }

    private void UpdateLabels(TideNode node, TideForecast forecast, TideEvent? first, TideEvent? second, double level, bool rising)
    {
        int tenths = (int)Math.Round(level * 10);
        var zone = Time.LocalTimeZone;
        bool estimate = forecast.IsModel;
        if (ReferenceEquals(first, _shownFirst) && ReferenceEquals(second, _shownSecond) && tenths == _shownTenths && rising == _shownRising
            && estimate == _shownEstimate && _shownAny && ReferenceEquals(zone, _shownZone) && forecast.Place == _shownPlace) return;

        _shownFirst = first; _shownSecond = second; _shownTenths = tenths; _shownRising = rising; _shownEstimate = estimate;
        _shownAny = true; _shownZone = zone; _shownPlace = forecast.Place;

        string Clock(TideEvent e) => TimeZoneInfo.ConvertTime(e.Time, zone).ToString("HH:mm");
        static string Label(TideEvent e, string time) => (e.Kind == TideKind.High ? "HIGH " : "LOW ") + time;
        static string Metres(double h) => h.ToString("0.0") + "m";

        if (first is null) node.SetFirst(null, "", "");
        else node.SetFirst(first.Kind, Label(first, Clock(first)), Metres(first.Height));
        if (second is null) node.SetSecond(null, "", "");
        else node.SetSecond(second.Kind, Label(second, Clock(second)), Metres(second.Height));
        node.SetPlace(estimate ? forecast.Place + " (est.)" : forecast.Place);
        node.SetStatus((rising ? "RISING " : "FALLING ") + Metres(level));
    }

    /// <summary>The polled forecast while it still covers the next half day; otherwise the sinusoidal model.</summary>
    internal TideForecast ActiveForecast(DateTimeOffset now)
    {
        var polled = _data?.Value;
        if (polled is not null && polled.Covers(now) && polled.Covers(now + TimeSpan.FromHours(14))) return polled;

        if (_model is null || !_model.Covers(now + TimeSpan.FromHours(14)) || !_model.Covers(now - TimeSpan.FromHours(1)))
            _model = ModelTideSource.Create(Location, now);
        return _model;
    }

    // ---- lifecycle & settings ---------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        if (!_locationFromUser && configuration["Tides:Location"] is { Length: > 0 } loc) Location = loc;
        _active = true;
        RestartPolling();
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        CancelPoll(ref _pollCts);
        await base.OnDeactivatedAsync(cancellationToken);
    }

    protected override void OnSettingChanged(string key)
    {
        if (key != "location") return;
        _model = null;
        _shownAny = false;
        if (!_active) return;
        _locationFromUser = true;
        RestartPolling();
    }

    private void RestartPolling()
    {
        var location = Location;
        _model = null;
        _data = string.IsNullOrWhiteSpace(location)
            ? null
            : RestartPoll(ref _pollCts, TimeSpan.FromMinutes(30), ct => _source.GetAsync(location, Time.GetUtcNow(), ct));
    }

    internal TideNode? Water => _node;

    /// <summary>Test seam: replaces the polled data with a fixed value (call before the first frame).</summary>
    internal void UseData(ILiveData<TideForecast>? data) { _data = data; _model = null; }
}
