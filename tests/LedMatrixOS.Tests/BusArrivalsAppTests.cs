using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class BusArrivalsAppTests(ITestOutputHelper output)
{
    private static BusArrivalsApp NewApp(TflStubHandler? handler = null)
    {
        Fonts.Load();
        return new(new HttpClient(handler ?? new TflStubHandler())) { Time = new FakeTime() };
    }

    private static TflArrival Bus(string vehicle, string route, string destination, int seconds) => new()
    {
        Id = vehicle + seconds,
        VehicleId = vehicle,
        LineId = route.ToLowerInvariant(),
        LineName = route,
        DestinationName = destination,
        TimeToStation = seconds,
        PlatformName = "",
    };

    private static TflArrival[] VictoriaStopB() =>
    [
        Bus("LX01", "73", "Stoke Newington", 45),
        Bus("LX02", "38", "Clapton Pond", 240),
        Bus("LX03", "N73", "Seven Sisters", 410),
        Bus("LX04", "11", "Liverpool Street", 600),
        Bus("LX05", "52", "Willesden", 780),
    ];

    private static TflArrival[] VictoriaStopC() =>
    [
        Bus("LY01", "24", "Hampstead Heath", 120),
        Bus("LY02", "C1", "White City", 300),
    ];

    private static (BusArrivalsApp App, AppStage Stage) Board(int warmMs = 1500, params (string Id, TflArrival[] Arrivals, string Label)[] stops)
    {
        var app = NewApp();
        app.UseData(stops.Select(s => (s.Id, (Core.Data.ILiveData<TflArrival[]>)new MutableLive<TflArrival[]> { Value = s.Arrivals }, (Core.Data.ILiveData<string>)new MutableLive<string> { Value = s.Label })).ToArray());
        var stage = new AppStage(app);
        stage.Step(33, warmMs / 33);
        return (app, stage);
    }

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

        Assert.Equal(new[] { "stopSearch", "stopSelect", "stopIds", "routeFilter", "maxBuses", "pageSeconds" }, settings.Select(s => s.Key).ToArray());
        Assert.Equal(
            new[] { AppSettingType.String, AppSettingType.Select, AppSettingType.String, AppSettingType.String, AppSettingType.Integer, AppSettingType.Integer },
            settings.Select(s => s.Type).ToArray());
        Assert.Equal(4, settings.Single(s => s.Key == "maxBuses").MaxValue);
        Assert.Equal("bus-arrivals", app.Id);
    }

    [Fact]
    public void ParseStopIds_TrimsDedupesAndCaps()
    {
        Assert.Equal(["a", "b", "c"], BusArrivalsApp.ParseStopIds(" a, b ,A,, c "));
        Assert.Equal(BusArrivalsApp.MaxStops, BusArrivalsApp.ParseStopIds("1,2,3,4,5,6").Length);
        Assert.Empty(BusArrivalsApp.ParseStopIds(null));
    }

    [Fact]
    public void StopSelect_AddsStops_AndDropsTheOldestBeyondTheLimit()
    {
        var app = NewApp();
        app.UpdateSetting("stopSelect", "");
        app.UpdateSetting("stopSelect", "not a selection");
        Assert.Empty(app.Feeds);

        foreach (var id in new[] { "s1", "s2", "s3", "s4" }) app.UpdateSetting("stopSelect", $"{id} | Somewhere");
        app.UpdateSetting("stopSelect", "s2 | Somewhere");   // already present: no change
        Assert.Equal(["s1", "s2", "s3", "s4"], app.Feeds.Select(f => f.StopId).ToArray());

        app.UpdateSetting("stopSelect", "s5 | Somewhere");
        Assert.Equal(["s2", "s3", "s4", "s5"], app.Feeds.Select(f => f.StopId).ToArray());
        Assert.Equal("s2,s3,s4,s5", app.StopIds);
    }

    // ---- data against a stub TfL --------------------------------------------------------------------------------

    [Fact]
    public async Task Fetches_ArrivalsAndLabel_ForEveryConfiguredStop()
    {
        var handler = new TflStubHandler { Arrivals = """[{"destinationName":"Stoke Newington","timeToStation":90,"platformName":"null","lineName":"73","lineId":"73","vehicleId":"LX01"}]""" };
        var app = NewApp(handler);
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BusArrivals:StopIds"] = "490000266B,490000266C",
        }).Build(), CancellationToken.None);
        var stage = new AppStage(app);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (app.Feeds.Any(f => f.Model.Visible.Count == 0))
        {
            Assert.True(DateTime.UtcNow < deadline, "arrivals were not fetched in time");
            stage.Step(33);
            await Task.Delay(10);
        }

        Assert.Contains(handler.Requests, r => r.Contains("/StopPoint/490000266B/Arrivals"));
        Assert.Contains(handler.Requests, r => r.Contains("/StopPoint/490000266C/Arrivals"));
        Assert.Equal("73", app.Feeds[0].Model.Hero[0].LineName);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public void RouteFilter_KeepsOnlyListedRoutes_ExactlyAndCaseInsensitively()
    {
        var (app, stage) = Board(300, ("490000266B", VictoriaStopB(), "Victoria Station (Stop B)"));
        Assert.Equal(4, app.Feeds[0].Model.Visible.Count);   // five buses, capped by the default MaxBuses

        app.RouteFilter = "73, n73";
        stage.Step(33, 3);
        Assert.Equal(["73", "N73"], app.Feeds[0].Model.Visible.Select(d => d.LineName).ToArray());

        app.RouteFilter = "7";   // a prefix of 73 must not match
        stage.Step(33, 3);
        Assert.Empty(app.Feeds[0].Model.Visible);

        app.RouteFilter = "";
        stage.Step(33, 3);
        Assert.Equal(4, app.Feeds[0].Model.Visible.Count);
    }

    [Fact]
    public void NightRoutes_GetNightBlue()
    {
        Assert.Equal(BusColors.Night, BusColors.For("N73"));
        Assert.Equal(BusColors.Day, BusColors.For("73"));
        Assert.Equal(BusColors.Day, BusColors.For("N"));
        Assert.Equal(BusColors.Day, BusColors.For("NX2"));
    }

    // ---- rendering ----------------------------------------------------------------------------------------------

    [Fact]
    public void Snapshots()
    {
        var (_, one) = Board(1500, ("490000266B", VictoriaStopB(), "Victoria Station (Stop B)"));
        Golden(one, "bus_arrivals_one_stop");

        var (due, dueStage) = Board(1500, ("490000266B", [Bus("LX01", "73", "Stoke Newington", 20), Bus("LX02", "38", "Clapton Pond", 130)], "Victoria Station (Stop B)"));
        Assert.Equal(2, due.Feeds[0].Model.Visible.Count);
        Golden(dueStage, "bus_arrivals_due");

        var (_, none) = Board(1500);
        Golden(none, "bus_arrivals_no_stop");

        var offline = NewApp();
        offline.UseData(("490000266B", new MutableLive<TflArrival[]> { Error = new HttpRequestException("down") }, new MutableLive<string> { Value = "" }));
        var os = new AppStage(offline);
        os.Step(33, 45);
        Golden(os, "bus_arrivals_offline");
    }

    [Fact]
    public void TwoStops_PageBetweenThem()
    {
        var (app, stage) = Board(300,
            ("490000266B", VictoriaStopB(), "Victoria Station (Stop B)"),
            ("490000266C", VictoriaStopC(), "Victoria Station (Stop C)"));
        Assert.Equal(2, app.StopPager!.PageCount);
        Assert.Equal(0, app.StopPager.PageIndex);

        stage.Step(33, 8 * 30 + 40);   // past the page interval and through the slide
        Assert.Equal(1, app.StopPager.PageIndex);
        Golden(stage, "bus_arrivals_second_stop");
    }

    // ---- performance --------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage) = Board(1500,
            ("490000266B", VictoriaStopB(), "Victoria Station (Stop B)"),
            ("490000266C", VictoriaStopC(), "Victoria Station (Stop C)"));
        for (int i = 0; i < 400; i++) { stage.Step(33); stage.Render(); }

        int measured = 0;
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 12; window++)
        {
            int page = app.StopPager!.PageIndex;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (page != app.StopPager.PageIndex || app.StopPager.IsTransitioning) continue;
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        output.WriteLine($"bus arrivals: {ms:F3} ms/frame, {measured} steady windows");
        Assert.True(measured >= 3);
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
