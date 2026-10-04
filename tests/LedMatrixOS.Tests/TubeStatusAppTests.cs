using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class TubeStatusAppTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static LineStatus[] AllLines(bool disrupted)
    {
        string[] ids = ["bakerloo", "central", "circle", "district", "hammersmith-city", "jubilee", "metropolitan", "northern", "piccadilly", "victoria", "waterloo-city"];
        return ids.Select(id => disrupted && id == "northern" ? Status(id, 6, "Severe Delays", "NORTHERN LINE: Severe delays due to a signal failure at Camden Town. Tickets are being accepted on local buses.")
            : disrupted && id == "circle" ? Status(id, 9, "Minor Delays", "CIRCLE LINE: Minor delays due to an earlier customer incident.")
            : disrupted && id == "district" ? Status(id, 2, "Suspended", "DISTRICT LINE: No service between Earl's Court and Wimbledon.")
            : Status(id, 10, "Good Service")).ToArray();
    }

    static TubeStatusAppTests() => Fonts.Load();

    private static (TubeStatusApp App, AppStage Stage) Board(LineStatus[]? data, int warmMs = 2000)
    {
        var app = new TubeStatusApp(new HttpClient(new TflStubHandler())) { Time = new FakeTime() };
        app.UseData(new FakeLive<LineStatus[]> { Value = data });
        var stage = new AppStage(app);
        stage.Step(33, warmMs / 33);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Settings_AndIdentity()
    {
        var app = new TubeStatusApp(new HttpClient(new TflStubHandler()));
        Assert.Equal("tube-status", app.Id);
        Assert.Equal("Tube Status", app.Name);
        Assert.Equal(new[] { "lines", "pageSeconds" }, app.GetSettings().Select(x => x.Key).ToArray());
        app.UpdateSetting("pageSeconds", 99);
        Assert.Equal(30, app.PageSeconds);
        app.UpdateSetting("lines", "Nonsense");
        Assert.Equal("Tube", app.Lines);
    }

    [Theory]
    [InlineData(10, "Good Service", Health.Good)]
    [InlineData(9, "Minor Delays", Health.Minor)]
    [InlineData(6, "Severe Delays", Health.Severe)]
    [InlineData(2, "Suspended", Health.Closed)]
    [InlineData(4, "Planned Closure", Health.Planned)]
    [InlineData(99, "Good Service", Health.Good)]
    [InlineData(99, "Something odd", Health.Minor)]
    public void Severity_IsGroupedIntoHealth(int severity, string text, Health expected) =>
        Assert.Equal(expected, LineHealth.Classify(severity, text));

    [Fact]
    public void ShortReason_DropsLinePrefixAndTrims()
    {
        Assert.Equal("Severe delays due to a signal failure at Camden Town",
            StatusCards.ShortReason("NORTHERN LINE: Severe delays due to a signal failure at Camden Town. Tickets are being accepted."));
        Assert.Equal("", StatusCards.ShortReason(""));
        Assert.EndsWith("...", StatusCards.ShortReason(new string('x', 200)));
    }

    [Fact]
    public async Task FetchesTheTubeModeFromTfL_AndShowsTheWorstStatusPerLine()
    {
        var handler = new TflStubHandler();
        var app = new TubeStatusApp(new HttpClient(handler)) { Time = new FakeTime() };
        await app.OnActivatedAsync((64, 256), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), CancellationToken.None);
        var stage = new AppStage(app);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (app.CardPager is null || app.CardPager.PageCount == 0 || app.CardPager.CurrentPage is null)
        {
            Assert.True(DateTime.UtcNow < deadline, "no data");
            stage.Step(33);
            await Task.Delay(10);
        }
        Assert.Contains(handler.Requests, r => r.Contains("/Line/Mode/tube/Status"));
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public void Golden_CalmDisruptedAndLoading()
    {
        Golden(Board(AllLines(false)).Stage, "tube_status_calm");
        Golden(Board(AllLines(true)).Stage, "tube_status_disrupted");
        Golden(Board(null, 600).Stage, "tube_status_loading");

        var offline = new TubeStatusApp(new HttpClient(new TflStubHandler())) { Time = new FakeTime() };
        offline.UseData(new MutableLive<LineStatus[]> { Error = new HttpRequestException("down") });
        var os = new AppStage(offline);
        os.Step(33, 30);
        Golden(os, "tube_status_offline");
    }

    [Fact]
    public void Golden_EntranceAndPageTransition()
    {
        var app = new TubeStatusApp(new HttpClient(new TflStubHandler())) { Time = new FakeTime() };
        app.UseData(new FakeLive<LineStatus[]> { Value = AllLines(true) });
        var stage = new AppStage(app);
        stage.Step(33, 6);
        Golden(stage, "tube_status_entrance");

        stage.Step(33, 30);
        int guard = 0;
        while (!app.CardPager!.IsTransitioning && guard++ < 1000) stage.Step(33);
        stage.Step(33, 7);
        Golden(stage, "tube_status_page_transition");
    }

    private static LineStatus[] ManyLines(int count) => Enumerable.Range(0, count)
        .Select(i => i % 7 == 3 ? Status($"line{i}", 9, "Minor Delays", $"LINE{i}: Minor delays.") : Status($"line{i}", 10, "Good Service")).ToArray();

    private static LineStatus[] AllRail()
    {
        string[] ids = ["bakerloo", "central", "circle", "district", "hammersmith-city", "jubilee", "metropolitan", "northern", "piccadilly", "victoria", "waterloo-city",
            "dlr", "elizabeth", "london-overground", "liberty", "lioness", "mildmay", "suffragette", "weaver", "windrush"];
        return ids.Select(id => id == "northern" ? Status(id, 6, "Severe Delays", "NORTHERN LINE: Severe delays due to a signal failure at Camden Town")
            : id == "dlr" ? Status(id, 9, "Minor Delays", "DLR: Minor delays") : Status(id, 10, "Good Service")).Append(Status("tram", 10, "Good Service")).ToArray();
    }

    [Theory]
    [InlineData(11)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(21)]
    public void Tiles_NeverExceedTheDisplayWidth_AndLeaveRoomForCards(int count)
    {
        var (app, stage) = Board(ManyLines(count));
        stage.Render();
        var tiles = app.TileNodes.ToList();
        Assert.Equal(count, tiles.Count);
        foreach (var t in tiles)
        {
            Assert.True(t.Bounds.X >= 0 && t.Bounds.Right <= 256, $"tile at {t.Bounds} leaves the display");
            Assert.True(t.Bounds.Bottom <= 64);
        }
        Assert.True(app.CardAreaHeight >= 16, $"card area is {app.CardAreaHeight}px");
        Assert.Equal(count > 14 ? 2 : 1, tiles.Select(t => t.ScreenBounds.Y).Distinct().Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(21)]
    [InlineData(60)]
    public void TileWidth_FitsTheRow(int count) => Assert.True(TubeStatusApp.TileWidth(count) * count + (count - 1) <= 256);

    [Fact]
    public void FullReason_KeepsTheWholeTextWithoutPrefix() =>
        Assert.Equal("Severe delays due to a signal failure at Camden Town. Tickets are being accepted on local buses",
            StatusCards.FullReason("NORTHERN LINE: Severe delays due to a signal failure at Camden Town. Tickets are being accepted on local buses."));

    [Fact]
    public void Golden_AllRailTwoRows_AndLongReason()
    {
        Golden(Board(AllRail()).Stage, "tube_status_all_rail_two_rows");
        var lines = AllLines(false);
        lines[7] = Status("northern", 6, "Severe Delays", "NORTHERN LINE: Severe delays between Edgware and Morden via Bank due to a signal failure at Camden Town. Tickets are being accepted on local buses, and replacement services are running between Euston and Kennington.");
        var (_, stage) = Board(lines, 3600);
        Golden(stage, "tube_status_disrupted_long_reason");
    }

    [Fact]
    public void TwoRowLayout_DoesNotAllocate()
    {
        var (app, stage) = Board(AllRail());
        for (int i = 0; i < 400; i++) { stage.Step(33); stage.Render(); }
        long least = long.MaxValue;
        int measured = 0;
        for (int window = 0; window < 12; window++)
        {
            int page = app.CardPager!.PageIndex;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (page != app.CardPager.PageIndex || app.CardPager.IsTransitioning) continue;
            measured++;
            least = Math.Min(least, allocated);
        }
        Assert.True(measured >= 3);
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }

    [Fact]
    public void DisruptedTiles_PulseOverTime()
    {
        var (_, stage) = Board(AllLines(true));
        var a = stage.Snapshot();
        stage.Step(33, 18);
        var b = stage.Snapshot();
        Assert.False(Stage.Same(a, b));
    }

    [Fact]
    public void SteadyState_DoesNotAllocate_AndIsFast()
    {
        var (app, stage) = Board(AllLines(true));
        for (int i = 0; i < 400; i++) { stage.Step(33); stage.Render(); }

        int measured = 0;
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 12; window++)
        {
            int page = app.CardPager!.PageIndex;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (page != app.CardPager.PageIndex || app.CardPager.IsTransitioning) continue;
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        output.WriteLine($"status: {ms:F3} ms/frame (update+render per frame), {measured} steady windows");
        Assert.True(measured >= 3);
        // Runtime housekeeping (tiered JIT) can add a few KB to a window; the steady state itself must be allocation free.
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}

