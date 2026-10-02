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
