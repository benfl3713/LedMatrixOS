using System.Diagnostics;
using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class TubeDeparturesAppTests(ITestOutputHelper output)
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static TubeDeparturesApp NewApp(TflStubHandler? handler = null, string station = "940GZZLUBST")
    {
        Fonts.Load();
        return new(new HttpClient(handler ?? new TflStubHandler())) { Time = new FakeTime(), StationId = station };
    }

    private static (TubeDeparturesApp App, AppStage Stage, MutableLive<TflArrival[]> Arrivals) Board(
        TflArrival[]? arrivals = null, LineStatus[]? statuses = null, string name = "Baker Street", int warmMs = 1500, int max = 3, string style = "Hero")
    {
        var app = NewApp();
        app.MaxDepartures = max;
        app.BoardStyle = style;
        var live = new MutableLive<TflArrival[]> { Value = arrivals ?? CommuteBoard() };
        app.UseData(live, new MutableLive<LineStatus[]> { Value = statuses ?? StationLines() }, new MutableLive<string> { Value = name });
        var stage = new AppStage(app);
        stage.Step(33, warmMs / 33);
        return (app, stage, live);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    private static bool HasPixelsIn(FrameBuffer f, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (f.GetPixel(x, y) != Pixel.Black) return true;
        return false;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time");
            await Task.Delay(10);
        }
    }

    // ---- settings (behaviour preserved from the old app) ---------------------------------------------------------

    [Fact]
    public void Settings_KeepTheirKeysOrderAndTypes()
    {
        var settings = NewApp().GetSettings().ToList();

        Assert.Equal(
            new[] { "stationSearch", "stationSelect", "stationId", "platformFilter", "boardStyle", "maxDepartures", "colorDeparturesByLine", "pageSeconds" },
            settings.Select(s => s.Key).ToArray());
        Assert.Equal(
            new[] { AppSettingType.String, AppSettingType.Select, AppSettingType.String, AppSettingType.String, AppSettingType.Select, AppSettingType.Integer, AppSettingType.Boolean, AppSettingType.Integer },
            settings.Select(s => s.Type).ToArray());

        var max = settings.Single(s => s.Key == "maxDepartures");
        Assert.Equal("Split", settings.Single(s => s.Key == "boardStyle").CurrentValue);
        Assert.Equal(new[] { "Split", "Platform", "Hero" }, settings.Single(s => s.Key == "boardStyle").Options);
        Assert.Equal(3, max.CurrentValue);
        Assert.Equal(1, max.MinValue);
        Assert.Equal(12, max.MaxValue);
        Assert.Equal(false, settings.Single(s => s.Key == "colorDeparturesByLine").CurrentValue);
        Assert.Equal(new[] { "Type at least 2 chars" }, settings.Single(s => s.Key == "stationSelect").Options);
        Assert.Equal("tube-departures", NewApp().Id);
        Assert.Equal("Tube Departures", NewApp().Name);
    }

    [Fact]
    public void UpdateSetting_AcceptsPersistedValueShapes()
    {
        var app = NewApp(station: "");

        app.UpdateSetting("maxDepartures", 99);
        Assert.Equal(12, app.MaxDepartures);
        app.UpdateSetting("maxDepartures", Json("0"));
        Assert.Equal(1, app.MaxDepartures);
        app.UpdateSetting("maxDepartures", Json("\"6\""));
        Assert.Equal(6, app.MaxDepartures);

        app.UpdateSetting("colorDeparturesByLine", true);
        Assert.True(app.ColorDeparturesByLine);
        app.UpdateSetting("colorDeparturesByLine", Json("false"));
        Assert.False(app.ColorDeparturesByLine);

        app.UpdateSetting("platformFilter", Json("\"East\""));
        Assert.Equal("East", app.PlatformFilter);

        // The empty stationSelect that gets persisted must not clear the station
        app.UpdateSetting("stationId", "940GZZLUBST");
        app.UpdateSetting("stationSelect", "");
        app.UpdateSetting("stationSelect", "not a selection");
        Assert.Equal("940GZZLUBST", app.StationId);

        app.UpdateSetting("stationSelect", "940GZZLUOXC | Oxford Circus");
        Assert.Equal("940GZZLUOXC", app.StationId);
    }

    // ---- data against a stub TfL --------------------------------------------------------------------------------

    [Fact]
    public async Task Renders_NoStationMessage_ThenDataOnceFetched()
    {
        var handler = new TflStubHandler();
        var app = NewApp(handler, station: "");
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);
        var stage = new AppStage(app);

        stage.Step(33, 20);
        Assert.False(SnapshotHelper.IsBlank(stage.Render()));
        Assert.Empty(handler.Requests);

        app.UpdateSetting("stationId", "940GZZLUBST");
        // The line pills (bottom left) only appear once arrivals and line statuses have both loaded
        await WaitFor(() =>
        {
            stage.Step(33);
            return handler.Requests.Any(r => r.Contains("/Line/")) && app.Board.Visible.Count == 2;
        });

        Assert.Contains(handler.Requests, r => r.Contains("/StopPoint/940GZZLUBST/Arrivals"));
        Assert.Contains(handler.Requests, r => r.Contains("/Line/circle,jubilee/Status"));
        Assert.Equal("Stanmore", app.Board.Hero[0].Destination);
        Assert.Equal("Aldgate via Baker St", app.Board.Visible[1].Destination);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlatformFilter_AppliesImmediatelyToFetchedArrivals()
    {
        var app = NewApp(new TflStubHandler());
        app.UseData(new MutableLive<TflArrival[]> { Value = [
            Arrival("1", "jubilee", "Stanmore", 30, "Southbound - Platform 1"),
            Arrival("2", "circle", "Aldgate", 300, "Eastbound - Platform 3")] },
            new MutableLive<LineStatus[]> { Value = [] }, new MutableLive<string> { Value = "Baker Street" });
        var stage = new AppStage(app);
        stage.Step(33, 10);
        Assert.Equal(2, app.Board.Visible.Count);

        app.UpdateSetting("platformFilter", "nonexistent platform");
        stage.Step(33, 2);
        Assert.Empty(app.Board.Visible);

        app.UpdateSetting("platformFilter", "Eastbound");
        stage.Step(33, 2);
        Assert.Single(app.Board.Visible);
        Assert.Equal("Aldgate", app.Board.Hero[0].Destination);
        Assert.Equal("3", app.Board.Hero[0].PlatformNumber);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StationSearch_PublishesRailStopsAsIdPipeName()
    {
        var app = NewApp(new TflStubHandler(), station: "");
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);

        app.UpdateSetting("stationSearch", "b");
        Assert.Equal(new[] { "Type at least 2 chars" }, app.GetSettings().Single(s => s.Key == "stationSelect").Options);

        app.UpdateSetting("stationSearch", "baker");
        await WaitFor(() => app.GetSettings().Single(s => s.Key == "stationSelect").Options!.Contains("940GZZLUBST | Baker Street"));
        Assert.Equal(new[] { "940GZZLUBST | Baker Street" }, app.GetSettings().Single(s => s.Key == "stationSelect").Options);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    // ---- board behaviour -----------------------------------------------------------------------------------------

    [Fact]
    public void Minutes_CountDownWithTheAppClock_AndDepartedTrainsDropOut()
    {
        var (app, stage, _) = Board(CommuteBoard(190), warmMs: 330);
        var hero = app.Board.Hero[0];
        Assert.Equal(3, hero.Minutes(app.Board.Now));

        stage.Step(1000, 69);   // about 70 s later the 190 s train is at about 120 s
        Assert.Equal(2, hero.Minutes(app.Board.Now));

        stage.Step(1000, 190);  // long gone: lingers at DUE for a few seconds, then leaves and the next train is the hero
        Assert.NotSame(hero, app.Board.Hero[0]);
        Assert.DoesNotContain(hero, app.Board.Visible);
    }

    [Fact]
    public void Trains_KeepTheirIdentityAcrossPolls_ByVehicleId()
    {
        var (app, stage, live) = Board();
        var first = app.Board.Visible.ToArray();

        // A fresh poll: the same vehicles, new estimates, one new train behind them
        live.Value = [.. CommuteBoard(230), Arrival("606", "victoria", "Brixton", 1000)];
        stage.Step(33, 3);
        for (int i = 0; i < 3; i++) Assert.Same(first[i], app.Board.Visible[i]);
        Assert.Equal(3, app.Board.Hero[0].Minutes(app.Board.Now));  // 230 s
    }

    [Fact]
    public void Reshuffle_SlidesRowsUp_WhenTheHeroLeaves()
    {
        var (app, stage, live) = Board(max: 4);
        var heroNode = app.Root!;
        var before = stage.Snapshot();

        live.Value = CommuteBoard(190).Skip(1).ToArray();   // the hero left
        stage.Step(33, 6);
        var mid = stage.Snapshot();
        Assert.False(Stage.Same(before, mid));
        Golden(stage, "tube_departures_reshuffle_mid");

        stage.Step(33, 20);
        var settled = stage.Snapshot();
        Assert.False(Stage.Same(mid, settled));
        Assert.NotNull(heroNode);
        Golden(stage, "tube_departures_reshuffle_settled");
    }

    [Fact]
    public void Pager_CyclesPagesOfFollowingTrains()
    {
        var (app, stage, _) = Board(max: 5, warmMs: 330);
        Assert.Equal(2, app.RestPager!.PageCount);

        int guard = 0;
        while (!app.RestPager.IsTransitioning && guard++ < 1000) stage.Step(33);
        Assert.True(app.RestPager.IsTransitioning);
        stage.Step(33, 7);
        Golden(stage, "tube_departures_page_transition");
        stage.Step(33, 20);
        Assert.Equal(1, app.RestPager.PageIndex);
    }

    [Fact]
    public void Ticker_AlternatesStationNameWithDisruptions()
    {
        var (app, stage, _) = Board(warmMs: 330);
        Assert.Equal("BAKER STREET", app.StripTicker!.Current);
        stage.Step(1000, 13);
        Assert.Contains("SEVERE DELAYS", app.StripTicker.Current);
    }

    // ---- goldens -------------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_NormalBoard() => Golden(Board().Stage, "tube_departures_board");

    [Fact]
    public void Golden_DueTrain()
    {
        var (_, stage, _) = Board(CommuteBoard(40));
        Golden(stage, "tube_departures_due");
    }

    [Fact]
    public void Golden_ColourByLine()
    {
        var (app, stage, _) = Board(CommuteBoard(40));
        app.ColorDeparturesByLine = true;
        stage.Step(33, 3);
        Golden(stage, "tube_departures_colour_by_line");
    }

    [Fact]
    public void Golden_DisruptionPulse()
    {
        var (_, stage, _) = Board();
        stage.Step(33, 1);
        var a = stage.Snapshot();
        stage.Step(33, 18);   // about half a pulse later
        var b = stage.Snapshot();
        Assert.False(Stage.Same(a, b));
        SnapshotHelper.AssertMatchesSnapshot(a, "tube_departures_pulse_a");
        Preview(a, "tube_departures_pulse_a");
        SnapshotHelper.AssertMatchesSnapshot(b, "tube_departures_pulse_b");
        Preview(b, "tube_departures_pulse_b");
    }

    [Fact]
    public void Golden_StatesWithoutTrains()
    {
        var app = NewApp(station: "");
        var none = new AppStage(app);
        none.Step(33, 30);
        Golden(none, "tube_departures_no_station");

        var loading = NewApp();
        loading.UseData(new MutableLive<TflArrival[]>(), new MutableLive<LineStatus[]>(), new MutableLive<string>());
        var ls = new AppStage(loading);
        ls.Step(33, 30);
        Golden(ls, "tube_departures_loading");

        var empty = NewApp();
        empty.UseData(new MutableLive<TflArrival[]> { Value = [] }, new MutableLive<LineStatus[]> { Value = [] }, new MutableLive<string> { Value = "Baker Street" });
        var es = new AppStage(empty);
        es.Step(33, 30);
        Golden(es, "tube_departures_no_trains");

        var offline = NewApp();
        offline.UseData(new MutableLive<TflArrival[]> { Error = new HttpRequestException("down") }, null, null);
        var os = new AppStage(offline);
        os.Step(33, 30);
        Golden(os, "tube_departures_offline");
    }

    // ---- board styles --------------------------------------------------------------------------------------------

    private static TflArrival[] OneDirection() =>
    [
        Arrival("101", "victoria", "Brixton", 190, "Southbound - Platform 2"),
        Arrival("303", "victoria", "Brixton", 500, "Southbound - Platform 2"),
        Arrival("505", "victoria", "Brixton", 840, "Southbound - Platform 2"),
    ];

    private static TflArrival[] NoDirection() =>
    [
        Arrival("101", "victoria", "Brixton", 190, ""),
        Arrival("202", "victoria", "Walthamstow Central", 340, ""),
        Arrival("303", "victoria", "Brixton", 500, ""),
        Arrival("404", "victoria", "Walthamstow Central", 660, ""),
    ];

    [Fact]
    public void Direction_IsTheTextBeforeThePlatform()
    {
        Assert.Equal("Northbound", Departure.DirectionOf("Northbound - Platform 1"));
        Assert.Equal("Eastbound", Departure.DirectionOf("Eastbound"));
        Assert.Equal("", Departure.DirectionOf("Platform 3"));
        Assert.Equal("", Departure.DirectionOf(""));
    }

    [Fact]
    public void Split_GroupsByDirection_ThreePerColumn()
    {
        var (app, _, _) = Board(style: "Split");
        Assert.Equal(2, app.Board.ColumnCount);
        Assert.Equal("Northbound", app.Board.ColumnLabel(0));
        Assert.Equal("Southbound", app.Board.ColumnLabel(1));
        Assert.Equal(2, app.Board.Column(0).Count);
        Assert.Equal(3, app.Board.Column(1).Count);
        Assert.Equal(5, app.Board.Visible.Count);

        var (one, _, _) = Board(OneDirection(), style: "Split");
        Assert.Equal(1, one.Board.ColumnCount);
        var (none, _, _) = Board(NoDirection(), style: "Split");
        Assert.Equal(1, none.Board.ColumnCount);
        Assert.Equal("", none.Board.ColumnLabel(0));
        Assert.Equal(3, none.Board.Column(0).Count);   // Max Departures is per column
    }

    [Fact]
    public void Golden_Split()
    {
        var (_, stage, _) = Board(style: "Split");
        Golden(stage, "tube_departures_split");
    }

    [Fact]
    public void Golden_SplitDue()
    {
        var (_, stage, _) = Board(CommuteBoard(40), style: "Split");
        Golden(stage, "tube_departures_split_due");
    }

    [Fact]
    public void Golden_SplitOneDirection()
    {
        var (_, stage, _) = Board(OneDirection(), style: "Split");
        Golden(stage, "tube_departures_split_one_direction");
    }

    [Fact]
    public void Golden_SplitNoDirection()
    {
        var (_, stage, _) = Board(NoDirection(), style: "Split");
        Golden(stage, "tube_departures_split_no_direction");
    }

    [Fact]
    public void Golden_SplitColourByLine()
    {
        var (app, stage, _) = Board(CommuteBoard(40), style: "Split");
        app.ColorDeparturesByLine = true;
        stage.Step(33, 3);
        Golden(stage, "tube_departures_split_colour_by_line");
    }

    [Fact]
    public void Golden_Platform()
    {
        var (_, stage, _) = Board(CommuteBoard(), style: "Platform");
        Golden(stage, "tube_departures_platform");
    }

    [Fact]
    public void Golden_PlatformDue()
    {
        var (_, stage, _) = Board(CommuteBoard(40), style: "Platform");
        Golden(stage, "tube_departures_platform_due");
    }

    [Fact]
    public void Style_CanBeSwitchedAtRuntime()
    {
        var (app, stage, _) = Board(style: "Split");
        var split = stage.Snapshot();
        app.UpdateSetting("boardStyle", "Platform");
        stage.Step(33, 20);
        var platform = stage.Snapshot();
        Assert.False(Stage.Same(split, platform));
        app.UpdateSetting("boardStyle", "Hero");
        stage.Step(33, 20);
        Assert.False(Stage.Same(platform, stage.Snapshot()));
    }

    [Theory]
    [InlineData("Split")]
    [InlineData("Platform")]
    public void NewStyles_DoNotAllocateInSteadyState(string style)
    {
        var (app, stage, _) = Board(max: 5, style: style);
        for (int i = 0; i < 400; i++) { stage.Step(33); stage.Render(); }

        int measured = 0;
        long least = long.MaxValue;
        for (int window = 0; window < 12; window++)
        {
            string text = app.StripTicker!.Current;
            int page = app.RestPager!.PageIndex;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (text != app.StripTicker.Current || page != app.RestPager.PageIndex || app.RestPager.IsTransitioning) continue;
            measured++;
            least = Math.Min(least, allocated);
        }

        Assert.True(measured >= 3);
        Assert.True(least < 256, $"{style}: least allocation in a steady window: {least} bytes");
    }

    // ---- performance ---------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate_AndIsFast()
    {
        var (app, stage, _) = Board(max: 5);
        for (int i = 0; i < 400; i++) { stage.Step(33); stage.Render(); }   // through a page change, so pooled buffers exist

        // A page change or a new ticker message builds nodes and glyph maps (events, not steady state); measure windows without one.
        int measured = 0;
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 12; window++)
        {
            string text = app.StripTicker!.Current;
            int page = app.RestPager!.PageIndex;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (text != app.StripTicker.Current || page != app.RestPager.PageIndex || app.RestPager.IsTransitioning) continue;
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        output.WriteLine($"departures: {ms:F3} ms/frame (update+render per frame), {measured} steady windows");
        Assert.True(measured >= 3);
        // Runtime housekeeping (tiered JIT) can add a few KB to a window; the steady state itself must be allocation free.
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
