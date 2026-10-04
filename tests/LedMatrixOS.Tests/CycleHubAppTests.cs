using System.Diagnostics;
using System.Net;
using System.Text;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Cycle;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

/// <summary>Stub for the TfL BikePoint endpoints.</summary>
internal sealed class BikePointStubHandler : HttpMessageHandler
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Requests { get; } = new();

    public const string Dock = """
        {"id":"BikePoints_42","commonName":"Hyde Park Corner , Hyde Park","lat":51.5,"lon":-0.15,
         "additionalProperties":[
           {"key":"TerminalName","value":"001042"},
           {"key":"NbBikes","value":"12"},
           {"key":"NbEmptyDocks","value":"7"},
           {"key":"NbDocks","value":"20"},
           {"key":"NbStandardBikes","value":"9"},
           {"key":"NbEBikes","value":"3"}]}
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Enqueue(url);
        var body = url.Contains("/BikePoint/Search")
            ? """[{"id":"BikePoints_42","commonName":"Hyde Park Corner , Hyde Park"},{"id":"BikePoints_7","commonName":"Park Lane, Mayfair"},{"id":"BikePoints_42","commonName":"dupe"}]"""
            : Dock;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

public class CycleHubAppTests(ITestOutputHelper output)
{
    private static CycleHubApp NewApp(HttpMessageHandler? handler = null)
    {
        Fonts.Load();
        return new(new HttpClient(handler ?? new BikePointStubHandler()), new FakeWeatherSource()) { Time = new FakeTime() };
    }

    private static BikePointInfo Dock(string id, string name, int bikes, int empty, int ebikes) =>
        new(id, name, bikes, empty, bikes - ebikes, ebikes);

    private static WeatherSnapshot Forecast(int[] rain, double wind, double tempC)
    {
        var hours = rain.Select((r, i) => new HourlyPoint(FakeWeatherSource.Epoch.AddHours(i), tempC, r > 40 ? 61 : 2, true, r, wind)).ToList();
        return new WeatherSnapshot("London", new DateTimeOffset(FakeWeatherSource.Epoch, TimeSpan.Zero), tempC, tempC, 2, true, wind, 270,
            rain.Max(), tempC + 3, tempC - 4, new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), false, hours, []);
    }

    private static WeatherSnapshot GoodDay() => Forecast([0, 0, 5, 10, 10, 15, 20], 11, 17);
    private static WeatherSnapshot WetDay() => Forecast([45, 55, 60, 65, 50, 40, 20], 18, 11);

    private static (CycleHubApp App, AppStage Stage) Board(ILiveData<WeatherSnapshot>? weather, int warmMs = 1500, params (string Id, BikePointInfo? Info)[] docks)
    {
        var app = NewApp();
        app.UseData(weather, docks.Select(d => (d.Id, (ILiveData<BikePointInfo>)new MutableLive<BikePointInfo> { Value = d.Info })).ToArray());
        var stage = new AppStage(app);
        stage.Step(33, warmMs / 33);
        return (app, stage);
    }

    private static MutableLive<WeatherSnapshot> Live(WeatherSnapshot? snap) => new() { Value = snap };

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- settings -----------------------------------------------------------------------------------------------

    [Fact]
    public void Settings_KeepTheirKeysOrderAndTypes()
    {
        var app = NewApp();
        var settings = app.GetSettings().ToList();

        Assert.Equal(new[] { "dockIds", "pageSeconds", "units" }, settings.Select(s => s.Key).ToArray());
        Assert.Equal(
            new[] { AppSettingType.MultiSearch, AppSettingType.Integer, AppSettingType.Select },
            settings.Select(s => s.Type).ToArray());
        Assert.Equal("cycle-hub", app.Id);
        Assert.Equal("Cycle Hub", app.Name);
    }

    [Fact]
    public void ParseDockIds_TrimsDedupesAndCaps()
    {
        Assert.Equal(["a", "b", "c"], CycleHubApp.ParseDockIds(" a, b ,A,, c "));
        Assert.Equal(CycleHubApp.MaxDocks, CycleHubApp.ParseDockIds("1,2,3,4,5").Length);
        Assert.Empty(CycleHubApp.ParseDockIds(null));
    }

    [Fact]
    public void DockSelect_AddsDocks_AndDropsTheOldestBeyondTheLimit()
    {
        var app = NewApp();
        app.UpdateSetting("dockSelect", "not a selection");
        Assert.Empty(app.Feeds);

        foreach (var id in new[] { "d1", "d2", "d3" }) app.UpdateSetting("dockSelect", $"{id} | Somewhere");
        app.UpdateSetting("dockSelect", "d2 | Somewhere");
        Assert.Equal(["d1", "d2", "d3"], app.Feeds.Select(f => f.DockId).ToArray());

        app.UpdateSetting("dockSelect", "d4 | Somewhere");
        Assert.Equal("d2,d3,d4", app.DockIds);
    }

    // ---- verdict ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 10, 15, Ride.Good)]
    [InlineData(19, 19, 15, Ride.Good)]
    [InlineData(RideVerdict.DampRain, 10, 15, Ride.Ok)]
    [InlineData(0, RideVerdict.BreezyKmh, 15, Ride.Ok)]
    [InlineData(0, 10, 4.9, Ride.Ok)]
    [InlineData(0, 10, 33, Ride.Ok)]
    [InlineData(RideVerdict.WetRain, 10, 15, Ride.Wet)]
    [InlineData(60, 29, 15, Ride.Wet)]
    [InlineData(0, RideVerdict.WindyKmh, 15, Ride.Windy)]
    [InlineData(39, 44, 15, Ride.Windy)]
    [InlineData(RideVerdict.AvoidRain, 5, 15, Ride.Avoid)]
    [InlineData(0, RideVerdict.AvoidWindKmh, 15, Ride.Avoid)]
    [InlineData(RideVerdict.AvoidCombinedRain, RideVerdict.WindyKmh, 15, Ride.Avoid)]
    [InlineData(0, 5, RideVerdict.FreezingC, Ride.Avoid)]
    [InlineData(0, 5, -3, Ride.Avoid)]
    [InlineData(0, 5, 1, Ride.Ok)]
    public void RideVerdict_Table(int rain, double wind, double temp, Ride expected) =>
        Assert.Equal(expected, RideVerdict.Evaluate(rain, wind, temp));

    [Fact]
    public void Outlook_LooksTwoHoursAhead_AndChartsSix()
    {
        // The downpour at +3h must not spoil the verdict, but it does show on the chart.
        var outlook = RideOutlook.From(Forecast([0, 5, 0, 90, 0, 0, 0, 0], 10, 15));
        Assert.Equal(Ride.Good, outlook.Ride);
        Assert.Equal(6, outlook.Rain.Length);
        Assert.Equal(90f, outlook.Rain[3]);
        Assert.Equal("Wind 10 km/h", outlook.WindText);

        Assert.Equal(Ride.Wet, RideOutlook.From(Forecast([0, 45, 0, 0, 0, 0], 10, 15)).Ride);
    }

    [Fact]
    public void Outlook_ConvertsImperialUnits()
    {
        // 20 mph is about 32 km/h (windy); 41 F is 5 C (not chilly).
        var snap = Forecast([0, 0, 0, 0, 0, 0], 20, 41) with { Fahrenheit = true };
        var outlook = RideOutlook.From(snap);
        Assert.Equal(Ride.Windy, outlook.Ride);
        Assert.Equal("Wind 20 mph", outlook.WindText);
    }

    // ---- data against a stub TfL --------------------------------------------------------------------------------

    [Fact]
    public async Task GetBikePoint_ParsesCountsFromAdditionalProperties()
    {
        var handler = new BikePointStubHandler();
        var api = new TflApi(new HttpClient(handler)) { AppKey = "k" };

        var info = await api.GetBikePointAsync("BikePoints_42", CancellationToken.None);

        Assert.Equal(new BikePointInfo("BikePoints_42", "Hyde Park Corner, Hyde Park", 12, 7, 9, 3), info);
        Assert.Equal(19, info.Capacity);
        Assert.Contains(handler.Requests, r => r.Contains("/BikePoint/BikePoints_42") && r.Contains("app_key=k"));
    }

    [Fact]
    public async Task FindBikePoints_ReturnsDistinctIdAndNameMatches()
    {
        var handler = new BikePointStubHandler();
        var matches = await new TflApi(new HttpClient(handler)).FindBikePointsAsync("hyde park", CancellationToken.None);

        Assert.Equal(["BikePoints_42 | Hyde Park Corner, Hyde Park", "BikePoints_7 | Park Lane, Mayfair"], matches.Select(m => $"{m.Id} | {m.Name}").ToArray());
        Assert.Contains(handler.Requests, r => r.Contains("/BikePoint/Search?query=hyde park"));
    }

    [Fact]
    public async Task Activation_PollsEveryConfiguredDock_AndTheForecast()
    {
        var handler = new BikePointStubHandler();
        var app = NewApp(handler);
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CycleHub:DockIds"] = "BikePoints_42,BikePoints_7",
        }).Build(), CancellationToken.None);
        var stage = new AppStage(app);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (app.Feeds.Any(f => f.Info?.Value is null) || app.CurrentRide is null)
        {
            Assert.True(DateTime.UtcNow < deadline, "data was not fetched in time");
            stage.Step(33);
            await Task.Delay(10);
        }

        Assert.Equal(12, app.Feeds[0].Info!.Value!.NbBikes);
        Assert.Contains(handler.Requests, r => r.Contains("/BikePoint/BikePoints_7"));
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    // ---- rendering ----------------------------------------------------------------------------------------------

    [Fact]
    public void Snapshots()
    {
        var hyde = Dock("BikePoints_42", "Hyde Park Corner, Hyde Park", 12, 7, 3);

        var (good, goodStage) = Board(Live(GoodDay()), 1500, ("BikePoints_42", hyde));
        Assert.Equal(Ride.Good, good.CurrentRide);
        Golden(goodStage, "cycle_hub_good");

        var (wet, wetStage) = Board(Live(WetDay()), 1500, ("BikePoints_42", Dock("BikePoints_42", "Hyde Park Corner, Hyde Park", 3, 16, 0)));
        Assert.Equal(Ride.Wet, wet.CurrentRide);
        Golden(wetStage, "cycle_hub_wet");

        var (_, noBikes) = Board(Live(Forecast([20, 30, 45, 40, 30, 10], 22, 12)), 1500, ("BikePoints_7", Dock("BikePoints_7", "Park Lane, Mayfair", 0, 24, 0)));
        Golden(noBikes, "cycle_hub_no_bikes");

        var (_, none) = Board(Live(GoodDay()), 1500);
        Golden(none, "cycle_hub_no_dock");

        var offline = NewApp();
        offline.UseData(new MutableLive<WeatherSnapshot> { Error = new HttpRequestException("down") },
            ("BikePoints_42", new MutableLive<BikePointInfo> { Error = new HttpRequestException("down") }));
        var os = new AppStage(offline);
        os.Step(33, 45);
        Golden(os, "cycle_hub_offline");
    }

    [Fact]
    public void TwoDocks_PageBetweenThem()
    {
        var (app, stage) = Board(Live(GoodDay()), 300,
            ("BikePoints_42", Dock("BikePoints_42", "Hyde Park Corner, Hyde Park", 12, 7, 3)),
            ("BikePoints_7", Dock("BikePoints_7", "Park Lane, Mayfair", 2, 22, 1)));
        Assert.Equal(2, app.DockPager!.PageCount);

        stage.Step(33, 8 * 30 + 40);
        Assert.Equal(1, app.DockPager.PageIndex);
    }

    // ---- performance --------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage) = Board(Live(GoodDay()), 1500,
            ("BikePoints_42", Dock("BikePoints_42", "Hyde Park Corner, Hyde Park", 12, 7, 3)),
            ("BikePoints_7", Dock("BikePoints_7", "Park Lane, Mayfair", 2, 22, 1)));
        for (int i = 0; i < 400; i++) { stage.Step(33); stage.Render(); }

        int measured = 0;
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 12; window++)
        {
            int page = app.DockPager!.PageIndex;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (page != app.DockPager.PageIndex || app.DockPager.IsTransitioning) continue;
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        output.WriteLine($"cycle hub: {ms:F3} ms/frame, {measured} steady windows");
        Assert.True(measured >= 3);
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
