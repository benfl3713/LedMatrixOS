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
    private static Departure Train(int secondsAway, string route = "jubilee|Southbound") =>
        new() { Key = "t" + secondsAway + route, TimeToStation = secondsAway, SeenAt = TimeSpan.Zero, RouteKey = route };

    private static Departure[] Trains(params int[] seconds) => seconds.Select(t => Train(t)).ToArray();

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

    // ---- gaps ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Planner_GapsAroundTheTarget()
    {
        // 2 min walk: the 60s train is missed, so the target is 600s, 9 min after the missed one and 2 min before the next
        var plan = CommutePlanner.Plan(Trains(60, 600, 720, 900, 1080), TimeSpan.Zero, walkMinutes: 2);

        Assert.Equal(600, plan.Train!.TimeToStation);
        Assert.Equal(540, plan.GapBeforeSeconds);
        Assert.Equal(120, plan.GapAfterSeconds);
        Assert.Equal(180, plan.TypicalGapSeconds);   // median of 540, 120, 180, 180
        Assert.True(plan.Busy);
        Assert.False(plan.LongWaitAfter);
        Assert.True(plan.NextIsQuieter);
    }

    [Fact]
    public void Planner_LongWaitIfYouMissIt()
    {
        var plan = CommutePlanner.Plan(Trains(30, 200, 1100, 1280, 1460), TimeSpan.Zero, walkMinutes: 2);

        Assert.Equal(200, plan.Train!.TimeToStation);
        Assert.Equal(170, plan.GapBeforeSeconds);
        Assert.Equal(900, plan.GapAfterSeconds);
        Assert.False(plan.Busy);
        Assert.True(plan.LongWaitAfter);
        Assert.False(plan.NextIsQuieter);
    }

    [Fact]
    public void Planner_RegularService_FlagsNothing()
    {
        var plan = CommutePlanner.Plan(Trains(60, 240, 420, 600), TimeSpan.Zero, walkMinutes: 2);
        Assert.Equal(180, plan.GapBeforeSeconds);
        Assert.Equal(180, plan.GapAfterSeconds);
        Assert.False(plan.Busy || plan.LongWaitAfter || plan.NextIsQuieter);
    }

    [Fact]
    public void Planner_UnknownGaps_AreMinusOne()
    {
        var plan = CommutePlanner.Plan(Trains(300), TimeSpan.Zero, walkMinutes: 0);
        Assert.Equal(-1, plan.GapBeforeSeconds);
        Assert.Equal(-1, plan.GapAfterSeconds);
        Assert.Equal(-1, plan.TypicalGapSeconds);
        Assert.False(plan.Busy || plan.LongWaitAfter);
    }

    [Fact]
    public void Planner_WithoutTypicalInterval_UsesAFixedThreshold()
    {
        Assert.True(CommutePlanner.IsLongGap(8 * 60, -1));
        Assert.False(CommutePlanner.IsLongGap(7 * 60, -1));
        Assert.True(CommutePlanner.IsLongGap(6 * 60, 3 * 60));     // 3 min service: 6 min is long
        Assert.False(CommutePlanner.IsLongGap(14 * 60, 10 * 60));  // 10 min service: 14 min is not
    }

    [Fact]
    public void Planner_SameRouteOnly_IgnoresOtherDirections()
    {
        var board = new[] { Train(100), Train(200, "jubilee|Northbound"), Train(400), Train(500, "jubilee|Northbound"), Train(700) };

        var plan = CommutePlanner.Plan(board, TimeSpan.Zero, walkMinutes: 0, sameRouteOnly: true);

        Assert.Same(board[0], plan.Train);
        Assert.Equal(300, plan.GapAfterSeconds);
        Assert.Equal(300, plan.TypicalGapSeconds);
        Assert.Equal(100, CommutePlanner.Plan(board, TimeSpan.Zero, 0).GapAfterSeconds);   // every route counts otherwise
    }

    [Fact]
    public void History_GivesTheGapBeforeTheFirstTrainOnTheBoard()
    {
        var gone = Train(20);
        var next = Train(500);
        var history = new DepartureHistory();
        history.Observe([gone, next], TimeSpan.Zero);   // 20s away: arriving now

        // Later the feed has dropped it; the next train is 480s after it
        var plan = CommutePlanner.Plan([next], TimeSpan.FromSeconds(60), walkMinutes: 0, history: history);

        Assert.Equal(480, plan.GapBeforeSeconds);
    }

    [Fact]
    public void History_OnlyRemembersTrainsThatArrived()
    {
        var history = new DepartureHistory();
        history.Observe(Trains(200, 500), TimeSpan.Zero);
        Assert.False(history.LastBefore(Train(500), TimeSpan.Zero, false, out _));
    }

    // ---- line status -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Health.Good, false)]
    [InlineData(Health.Minor, false)]
    [InlineData(Health.Planned, true)]
    [InlineData(Health.Severe, true)]
    [InlineData(Health.Closed, true)]
    public void StatusLine_OnlyCallsOutWorseThanMinor(Health health, bool notable) => Assert.Equal(notable, CommuteStatusLine.Notable(health));

    [Fact]
    public void CommuteLines_FromRoutesAndWatchList()
    {
        Assert.Equal(["jubilee", "central"], CommuteApp.RouteLines("jubilee|Southbound, Central|Westbound,jubilee|Northbound"));
        Assert.Equal(["central", "elizabeth"], CommuteApp.ParseLines(" Central ,elizabeth,central"));
        Assert.Empty(CommuteApp.RouteLines(""));
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    private static (CommuteApp App, AppStage Stage) Board(int walk, TflArrival[]? arrivals = null, bool weather = true, string station = "940GZZLUBST",
        bool fahrenheit = false, string routes = "", LineStatus[]? statuses = null)
    {
        Fonts.Load();
        var app = new CommuteApp(new HttpClient(new TflStubHandler())) { Time = new FakeTime(), StationId = station, WalkMinutes = walk, Routes = routes };
        if (fahrenheit) app.Units = "Fahrenheit";
        var snapshot = weather ? new FakeWeatherSource(code: 61, isDay: true, tempC: 11).GetAsync(new WeatherQuery("London", fahrenheit), default).Result : null;
        app.UseData(new MutableLive<TflArrival[]> { Value = arrivals ?? CommuteBoard() }, new MutableLive<LineStatus[]> { Value = statuses ?? StationLines() },
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
        Assert.Equal(new[] { "stationId", "routes", "watchLines", "platformFilter", "walkMinutes", "location", "units" }, app.GetSettings().Select(s => s.Key).ToArray());
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

    private const string JubileeSouth = "jubilee|Southbound";

    private static TflArrival[] Jubilee(params int[] seconds) =>
        seconds.Select((t, i) => Arrival("j" + i, "jubilee", "Stratford", t, "Southbound - Platform 1")).ToArray();

    private static LineStatus[] JubileeAndCentral(int centralSeverity, string centralDescription) =>
        [Status("jubilee", 10, "Good Service"), Status("central", centralSeverity, centralDescription)];

    [Fact]
    public void Golden_BusyTrain_NextIsQuieter()
    {
        var (app, stage) = Board(walk: 2, arrivals: Jubilee(60, 600, 720, 900, 1080), routes: JubileeSouth, statuses: JubileeAndCentral(10, "Good Service"));
        Assert.True(app.CurrentPlan.NextIsQuieter);
        Golden(stage, "commute_busy_next_quieter");
    }

    [Fact]
    public void Golden_LongWaitIfMissed_AndLineDisrupted()
    {
        var (app, stage) = Board(walk: 2, arrivals: Jubilee(30, 200, 1100, 1280, 1460), routes: JubileeSouth, statuses: JubileeAndCentral(6, "Severe Delays"));
        Assert.True(app.CurrentPlan.LongWaitAfter);
        Golden(stage, "commute_long_wait_disrupted");
    }

    [Fact]
    public void Golden_MinorDelaysStayQuiet()
    {
        var (_, stage) = Board(walk: 0, arrivals: Jubilee(240, 420, 600, 780), routes: JubileeSouth, statuses: JubileeAndCentral(9, "Minor Delays"));
        Golden(stage, "commute_minor_delays");
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
        // Strings are rebuilt when the minute changes; measure windows where neither the leave-in nor the train minute moved.
        var run = stage.MeasureSteadyAllocation(windows: 12, beginWindow: () =>
        {
            int leaveMinute = app.CurrentPlan.LeaveInSeconds / 60;
            return () => leaveMinute == app.CurrentPlan.LeaveInSeconds / 60 && (app.CurrentPlan.LeaveInSeconds + 2) / 60 == leaveMinute;
        });

        output.WriteLine($"commute: {run.MsPerFrame:F3} ms/frame, {run.Measured} steady windows");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");    }
}
