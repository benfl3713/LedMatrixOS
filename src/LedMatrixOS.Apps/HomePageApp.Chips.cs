using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>The optional data chips: which feeds exist, and polling that only runs for the chips that are switched on.</summary>
public sealed partial class HomePageApp
{
    /// <summary>One chip's feed, with the token that stops its poll and the input it was started for.</summary>
    internal sealed class ChipFeed<T>(ILiveData<T> data, CancellationTokenSource? cts, string key)
    {
        public ILiveData<T> Data { get; } = data;
        public CancellationTokenSource? Cts { get; } = cts;
        public string Key { get; } = key;
    }

    /// <summary>Where each chip's data comes from; a null source means the chip is not configured and stays hidden.</summary>
    internal sealed record ChipSources(
        Func<CancellationToken, Task<WeatherSnapshot>>? Weather,
        Func<CancellationToken, Task<List<CalEvent>>>? Events,
        Func<CancellationToken, Task<LineStatus[]>>? Lines,
        Func<string, CancellationToken, Task<TflArrival[]>>? Bus);

    private readonly HttpClient? _http;
    private bool _active, _dataInjected, _sourcesInjected;
    private ChipSources? _sources;

    internal ChipFeed<WeatherSnapshot>? WeatherFeed { get; private set; }
    internal ChipFeed<List<CalEvent>>? EventFeed { get; private set; }
    internal ChipFeed<LineStatus[]>? LineFeed { get; private set; }
    internal ChipFeed<TflArrival[]>? BusFeed { get; private set; }

    public HomePageApp() { }

    [ActivatorUtilitiesConstructor]
    public HomePageApp(HttpClient http)
    {
        _http = http;
        try { http.Timeout = TimeSpan.FromSeconds(20); } catch (InvalidOperationException) { }
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken ct)
    {
        await base.OnActivatedAsync(dimensions, configuration, ct);
        if (!_sourcesInjected) _sources = BuildSources(configuration);
        _active = true;
    }

    public override async Task OnDeactivatedAsync(CancellationToken ct)
    {
        _active = false;
        StopFeed(WeatherFeed); StopFeed(EventFeed); StopFeed(LineFeed); StopFeed(BusFeed);
        if (!_dataInjected)
        {
            WeatherFeed = null;
            EventFeed = null;
            LineFeed = null;
            BusFeed = null;
        }
        await base.OnDeactivatedAsync(ct);
    }

    private ChipSources BuildSources(IConfiguration config)
    {
        var http = _http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // Weather: the same config as WeatherApp (Weather:Source = Fake, Weather:Location); Weather:Units = Fahrenheit is optional.
        IWeatherSource weather = string.Equals(config["Weather:Source"], "Fake", StringComparison.OrdinalIgnoreCase)
            ? new FakeWeatherSource()
            : new OpenMeteoWeatherSource(http);
        var query = new WeatherQuery(config["Weather:Location"] is { Length: > 0 } loc ? loc : "London",
            string.Equals(config["Weather:Units"], "Fahrenheit", StringComparison.OrdinalIgnoreCase));

        // Calendar: the private feed URL is configuration only, like CalendarApp.
        var ics = (config["Calendar:IcsUrl"] ?? "").Trim();
        Func<CancellationToken, Task<List<CalEvent>>>? events = null;
        if (ics.Length > 0)
        {
            var url = ics.StartsWith("webcal", StringComparison.OrdinalIgnoreCase) ? "https" + ics[6..] : ics;
            events = async ct =>
            {
                var text = await http.GetStringAsync(url, ct);
                var now = DateTimeOffset.UtcNow;
                return IcsParser.Parse(text, now.AddDays(-1), now.AddDays(30), Time.LocalTimeZone);
            };
        }

        var api = new TflApi(http) { AppKey = config["TFL:AppKey"] };
        return new ChipSources(ct => weather.GetAsync(query, ct), events, ct => api.GetModeStatusesAsync("tube", ct), api.GetArrivalsAsync);
    }

    /// <summary>Starts a poll for each chip that is switched on and stops the ones that are not. Cheap enough to call every frame.</summary>
    internal void SyncChipPolls()
    {
        if (!_active || _dataInjected || _sources is not { } s) return;
        string stop = (ChipStopId ?? "").Trim();

        WeatherFeed = Sync(WeatherFeed, ShowWeatherChip && s.Weather is not null, "w", TimeSpan.FromMinutes(10), s.Weather!);
        EventFeed = Sync(EventFeed, ShowEventChip && s.Events is not null, "e", TimeSpan.FromMinutes(15), s.Events!);
        LineFeed = Sync(LineFeed, ShowLineChip && s.Lines is not null, "l", TimeSpan.FromMinutes(5), s.Lines!);

        bool wantBus = ShowBusChip && stop.Length > 0 && s.Bus is not null;
        if (BusFeed is null ? !wantBus : wantBus && BusFeed.Key == stop) return;   // steady state: nothing to do
        BusFeed = StartBus(wantBus, stop, s.Bus);
    }

    // Kept apart so the closure is only allocated when the poll really changes, never on the per-frame path.
    private ChipFeed<TflArrival[]>? StartBus(bool want, string stop, Func<string, CancellationToken, Task<TflArrival[]>>? bus) =>
        Sync(BusFeed, want, stop, TimeSpan.FromSeconds(30), ct => bus!(stop, ct));

    private ChipFeed<T>? Sync<T>(ChipFeed<T>? feed, bool want, string key, TimeSpan interval, Func<CancellationToken, Task<T>> fetch)
    {
        if (!want)
        {
            StopFeed(feed);
            return null;
        }
        if (feed is not null && feed.Key == key) return feed;

        StopFeed(feed);
        var cts = new CancellationTokenSource();
        return new ChipFeed<T>(Poll(interval, fetch, cts.Token), cts, key);
    }

    private static void StopFeed<T>(ChipFeed<T>? feed) => feed?.Cts?.Cancel();

    /// <summary>Test seam: replaces every chip source with fakes (call before the first frame). Polls start only for chips that are switched on.</summary>
    internal void UseSources(ChipSources sources)
    {
        _sources = sources;
        _sourcesInjected = true;
        _dataInjected = false;
    }

    /// <summary>Test seam: fixed chip data, as if the feeds had loaded (call before the first frame).</summary>
    internal void UseData(ILiveData<WeatherSnapshot>? weather = null, ILiveData<List<CalEvent>>? events = null,
        ILiveData<LineStatus[]>? lines = null, ILiveData<TflArrival[]>? bus = null)
    {
        _dataInjected = true;
        WeatherFeed = weather is null ? null : new(weather, null, "test");
        EventFeed = events is null ? null : new(events, null, "test");
        LineFeed = lines is null ? null : new(lines, null, "test");
        BusFeed = bus is null ? null : new(bus, null, "test");
    }
}
