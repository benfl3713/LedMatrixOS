using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Commute;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class CommuteAppTests(ITestOutputHelper output)
{
    private static Departure Train(int secondsAway) => new() { Key = "t" + secondsAway, TimeToStation = secondsAway, SeenAt = TimeSpan.Zero };

    // ---- planner ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Planner_EmptyBoard_HasNothingToCatch()
    {
        var plan = CommutePlanner.Plan([], TimeSpan.Zero, 5);
        Assert.Null(plan.Train);
        Assert.False(plan.AllMissed);
    }

    [Fact]
    public void Planner_PicksFirstTrainThatIsFarEnoughAway()
    {
        var board = new[] { Train(120), Train(400), Train(700) };

        var plan = CommutePlanner.Plan(board, TimeSpan.Zero, walkMinutes: 5);

        Assert.Same(board[1], plan.Train);       // 120s is too soon for a 300s walk
        Assert.Equal(100, plan.LeaveInSeconds);
    }

    [Fact]
    public void Planner_AllTrainsTooSoon_ReportsMissed()
    {
        var plan = CommutePlanner.Plan([Train(60), Train(120)], TimeSpan.Zero, walkMinutes: 10);
        Assert.Null(plan.Train);
        Assert.True(plan.AllMissed);
    }

    [Theory]
    [InlineData(600, 0)]
    [InlineData(181, 0)]
    [InlineData(180, 1)]
    [InlineData(46, 1)]
    [InlineData(45, 2)]
    [InlineData(0, 2)]
    public void Planner_UrgencyThresholds(int leaveInSeconds, int expected)
    {
        var plan = CommutePlanner.Plan([Train(leaveInSeconds + 300)], TimeSpan.Zero, walkMinutes: 5);
        Assert.Equal(leaveInSeconds, plan.LeaveInSeconds);
        Assert.Equal((Urgency)expected, plan.Urgency);
    }

    [Fact]
    public void Planner_CountsDownWithTheClock()
    {
        var board = new[] { Train(600) };
        Assert.Equal(300, CommutePlanner.Plan(board, TimeSpan.Zero, 5).LeaveInSeconds);
        Assert.Equal(240, CommutePlanner.Plan(board, TimeSpan.FromSeconds(60), 5).LeaveInSeconds);
    }

    [Fact]
    public void Planner_ZeroWalk_LeavesWhenTheTrainArrives()
    {
        var plan = CommutePlanner.Plan([Train(75)], TimeSpan.Zero, walkMinutes: 0);
        Assert.Equal(75, plan.LeaveInSeconds);
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    private static (CommuteApp App, AppStage Stage) Board(int walk, TflArrival[]? arrivals = null, bool weather = true, string station = "940GZZLUBST", bool fahrenheit = false)
    {
        Fonts.Load();
        var app = new CommuteApp(new HttpClient(new TflStubHandler())) { Time = new FakeTime(), StationId = station, WalkMinutes = walk };
        if (fahrenheit) app.Units = "Fahrenheit";
        var snapshot = weather ? new FakeWeatherSource(code: 61, isDay: true, tempC: 11).GetAsync(new WeatherQuery("London", fahrenheit), default).Result : null;
        app.UseData(new MutableLive<TflArrival[]> { Value = arrivals ?? CommuteBoard() }, new MutableLive<LineStatus[]> { Value = StationLines() },
            new MutableLive<WeatherSnapshot> { Value = snapshot });
        var stage = new AppStage(app);
        stage.Step(33, 45);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new CommuteApp(new HttpClient(new TflStubHandler()));
        Assert.Equal("commute", app.Id);
        Assert.Equal(new[] { "stationId", "routes", "platformFilter", "walkMinutes", "location", "units" }, app.GetSettings().Select(s => s.Key).ToArray());
        var routes = app.GetSettings().Single(s => s.Key == "routes");
        Assert.Equal(AppSettingType.MultiSearch, routes.Type);
        Assert.True(routes.Browse);
        var walk = app.GetSettings().Single(s => s.Key == "walkMinutes");
        Assert.Equal(8, walk.CurrentValue);
        Assert.Equal(0, walk.MinValue);
        Assert.Equal(60, walk.MaxValue);
    }

    [Fact]
    public void StationSelect_SetsTheStationFromAChosenResult()
    {
        Fonts.Load();
        var app = new CommuteApp(new HttpClient(new TflStubHandler()));

        app.UpdateSetting("stationSelect", "940GZZLUBST | Baker Street Underground Station");

        Assert.Equal("940GZZLUBST", app.StationId);
    }

    [Fact]
    public void App_TargetsTheFirstCatchableTrain()
    {
        // Fixture trains are ~190, 340, 500 s away; with an 8 minute walk the third is the first you can still reach
        var (app, _) = Board(walk: 8);
        Assert.Equal("Brixton", app.CurrentPlan.Train?.Destination);
        Assert.Equal(Urgency.Now, app.CurrentPlan.Urgency);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(8, 2)]
    public void App_UrgencyFollowsWalkTime(int walk, int expected)
    {
        var (app, _) = Board(walk);
        Assert.Equal((Urgency)expected, app.CurrentPlan.Urgency);
    }

    [Fact]
    public void App_SettingsRoundTripThroughJson()
    {
        var (app, _) = Board(walk: 3);
        app.UpdateSetting("walkMinutes", System.Text.Json.JsonDocument.Parse("12").RootElement);
        Assert.Equal(12, app.WalkMinutes);
    }

    [Theory]
    [InlineData(0, "commute_relaxed")]
    [InlineData(2, "commute_soon")]
    [InlineData(8, "commute_go_now")]
    [InlineData(60, "commute_all_missed")]
    public void Golden_Urgencies(int walk, string name)
    {
        var (_, stage) = Board(walk);
        if (name == "commute_go_now") while (LedMatrixOS.Apps.Tube.TubeGfx.Wave(stage.Time, 0.6) < 0.95) stage.Step(10);   // land on the bright part of the flash
        Golden(stage, name);
    }

    [Fact]
    public void Golden_Fahrenheit()
    {
        var (_, stage) = Board(walk: 0, fahrenheit: true);
        Golden(stage, "commute_fahrenheit");
    }

    [Fact]
    public void Golden_NoStation()
    {
        var (_, stage) = Board(walk: 5, station: "", arrivals: []);
        Golden(stage, "commute_no_station");
    }

    [Fact]
    public void Golden_WeatherStillLoading()
    {
        var (_, stage) = Board(walk: 0, weather: false);
        Golden(stage, "commute_weather_loading");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage) = Board(walk: 0);
        for (int i = 0; i < 100; i++) { stage.Step(33); stage.Render(); }

        // Strings are rebuilt when the minute changes; measure windows where neither the leave-in nor the train minute moved.
        int measured = 0;
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 12; window++)
        {
            int leaveMinute = app.CurrentPlan.LeaveInSeconds / 60;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            int after = app.CurrentPlan.LeaveInSeconds / 60;
            if (leaveMinute != after || (app.CurrentPlan.LeaveInSeconds + 2) / 60 != after) continue;
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        output.WriteLine($"commute: {ms:F3} ms/frame, {measured} steady windows");
        Assert.True(measured >= 3);
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
