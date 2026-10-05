using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Tides;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class TidesAppTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 13, 45, 7, TimeSpan.Zero);

    /// <summary>A 12 hour sine tide, 1.0 to 5.0 m, with high water <paramref name="highInHours"/> from now. Real-looking data (not flagged as a model).</summary>
    private static TideForecast Sine(double highInHours, string place = "Dover")
    {
        var start = Now - TimeSpan.FromHours(13);
        var levels = new double[4 * 60];
        for (int i = 0; i < levels.Length; i++)
        {
            double h = i * 0.25 - 13 - highInHours;
            levels[i] = 3 + 2 * Math.Cos(2 * Math.PI * h / 12);
        }

        return new TideForecast(place, start, TimeSpan.FromMinutes(15), levels);
    }

    private static (TidesApp App, AppStage Stage) Rig(TideForecast? forecast, string location = "Dover")
    {
        Fonts.Load();
        var app = new TidesApp(new ModelTideSource()) { Time = new FakeTime { Now = Now }, Location = location };
        app.UseData(forecast is null ? new MutableLive<TideForecast> { Error = new HttpRequestException("offline") } : new MutableLive<TideForecast> { Value = forecast });
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

    // ---- forecast --------------------------------------------------------------------------------------------------

    [Fact]
    public void Forecast_FindsHighsAndLowsWithSubSampleTiming()
    {
        var f = Sine(highInHours: 2.1);
        f.TryNext(Now, out var first, out var second);
        Assert.Equal(TideKind.High, first.Kind);
        Assert.Equal(2.1, (first.Time - Now).TotalHours, 2);
        Assert.Equal(5.0, first.Height, 2);
        Assert.Equal(TideKind.Low, second!.Kind);
        Assert.Equal(8.1, (second.Time - Now).TotalHours, 2);
        Assert.Equal(1.0, second.Height, 2);
    }

    [Fact]
    public void Forecast_InterpolatesSmoothlyBetweenSamples()
    {
        var f = Sine(highInHours: 0);
        Assert.Equal(5.0, f.LevelAt(Now), 2);
        Assert.Equal(3.0, f.LevelAt(Now + TimeSpan.FromHours(3)), 2);       // a quarter of a cycle later: mid level
        Assert.Equal(3 + 2 * Math.Cos(2 * Math.PI * 1.234 / 12), f.LevelAt(Now + TimeSpan.FromHours(1.234)), 2);
        Assert.Equal(f.LevelAt(f.End), f.LevelAt(f.End + TimeSpan.FromDays(1)));   // clamped past the end
    }

    [Fact]
    public void Forecast_CoverageNeedsASampleOnEachSide()
    {
        var f = Sine(0);
        Assert.True(f.Covers(Now));
        Assert.False(f.Covers(f.End));
        Assert.False(f.Covers(f.Start));
        Assert.Throws<ArgumentException>(() => new TideForecast("x", Now, TimeSpan.FromHours(1), [1, 2, 3]));
    }

    [Fact]
    public void Model_IsDeterministic_AndAlternatesHighAndLowAboutSixHoursApart()
    {
        var a = ModelTideSource.Create("Brighton|50.8,-0.1", Now);
        var b = ModelTideSource.Create("Brighton|50.8,-0.1", Now.AddMinutes(20));   // same hour: same forecast
        Assert.Equal("Brighton", a.Place);
        Assert.True(a.IsModel);
        Assert.Equal(a.Events.Select(e => e.Time), b.Events.Select(e => e.Time));
        for (int i = 1; i < a.Events.Length; i++)
        {
            Assert.NotEqual(a.Events[i].Kind, a.Events[i - 1].Kind);
            Assert.InRange((a.Events[i].Time - a.Events[i - 1].Time).TotalHours, 5.0, 7.5);
        }

        Assert.True(a.Covers(Now) && a.Covers(Now + TimeSpan.FromHours(30)));
    }

    [Fact]
    public async Task ModelSource_NeverFails()
    {
        var f = await new ModelTideSource().GetAsync("anywhere", Now, default);
        Assert.True(f.IsModel);
    }

    [Fact]
    public void OpenMeteo_ParsesHourlySeaLevel()
    {
        const string json = """
            {"latitude":50.8,"longitude":-0.1,"hourly":{"time":["2026-01-02T00:00","2026-01-02T01:00","2026-01-02T02:00","2026-01-02T03:00","2026-01-02T04:00","2026-01-02T05:00"],
             "sea_level_height_msl":[1.0,1.8,2.2,1.9,1.1,null]}}
            """;
        var f = OpenMeteoTideSource.Parse(json, "Brighton");
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), f.Start);
        Assert.Equal(TimeSpan.FromHours(1), f.Step);
        Assert.False(f.IsModel);
        Assert.Equal(1.0, f.Low);
        Assert.Equal(2.2, f.High);
        var high = Assert.Single(f.Events);
        Assert.Equal(TideKind.High, high.Kind);
        Assert.InRange(high.Time.Hour, 1, 3);
    }

    [Fact]
    public void OpenMeteo_InlandPlaceWithNoSeaLevel_Fails()
    {
        const string json = """{"hourly":{"time":["2026-01-02T00:00","2026-01-02T01:00"],"sea_level_height_msl":[null,null]}}""";
        Assert.Throws<FormatException>(() => OpenMeteoTideSource.Parse(json, "Oxford"));
        Assert.Contains("marine-api.open-meteo.com", OpenMeteoTideSource.Url(50.8, -0.1));
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new TidesApp(new ModelTideSource());
        Assert.Equal("tides", app.Id);
        Assert.Equal(new[] { "location" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Contains(typeof(TidesApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void App_WaterIsHighestAtHighTideAndLowestAtLowTide()
    {
        var (high, _) = Rig(Sine(0));
        var (low, _) = Rig(Sine(-6));
        var (mid, _) = Rig(Sine(3));
        Assert.Equal(1.0, high.Water!.Level, 1);
        Assert.Equal(0.0, low.Water!.Level, 1);
        Assert.Equal(0.5, mid.Water!.Level, 1);
        Assert.True(high.Water.Waterline < mid.Water.Waterline && mid.Water.Waterline < low.Water.Waterline);   // y grows downward
    }

    [Fact]
    public void App_NextEventsLabelTheComingHighAndLow()
    {
        var (app, _) = Rig(Sine(2.1));
        Assert.Equal(TideKind.High, app.Water!.FirstKind);
        Assert.Equal(TideKind.Low, app.Water.SecondKind);
        var (falling, _) = Rig(Sine(-2));   // high water two hours ago: low next
        Assert.Equal(TideKind.Low, falling.Water!.FirstKind);
    }

    [Fact]
    public void App_FallsBackToTheModelWhenTheSourceFails()
    {
        var (app, _) = Rig(null);
        Assert.True(app.ActiveForecast(Now).IsModel);
        Assert.InRange(app.Water!.Level, 0, 1);
        Assert.NotNull(app.Water.FirstKind);
    }

    [Fact]
    public void App_UsesRealDataWhileItCoversTheComingHours_ThenTheModel()
    {
        var (app, _) = Rig(Sine(1));
        Assert.False(app.ActiveForecast(Now).IsModel);
        Assert.True(app.ActiveForecast(Now + TimeSpan.FromHours(40)).IsModel);
    }

    // ---- goldens ---------------------------------------------------------------------------------------------------

    [Fact] public void Golden_HighTide() => Golden(Rig(Sine(0.3)).Stage, "tides_high");
    [Fact] public void Golden_LowTide() => Golden(Rig(Sine(-6.2), "Whitby").Stage, "tides_low");
    [Fact] public void Golden_RisingMidTide() => Golden(Rig(Sine(3)).Stage, "tides_rising");
    [Fact] public void Golden_FallingMidTide() => Golden(Rig(Sine(-3)).Stage, "tides_falling");
    [Fact] public void Golden_EstimateFromModel() => Golden(Rig(null, "Brighton").Stage, "tides_model");

    // ---- performance -----------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage) = Rig(Sine(3));
        // Labels are rebuilt when the displayed level ticks over a tenth of a metre; measure windows where that did not happen.
        var run = stage.MeasureSteadyAllocation(windows: 8, beginWindow: () =>
        {
            int tenths = (int)Math.Round(app.ActiveForecast(Now).LevelAt(Now) * 10);
            return () => tenths == (int)Math.Round(app.ActiveForecast(Now).LevelAt(Now) * 10);
        });
        output.WriteLine($"tides: {run.MsPerFrame:F3} ms/frame, {run.Measured} steady windows");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }

    [Fact]
    public void SteadyState_WithTheModel_DoesNotAllocate()
    {
        var (_, stage) = Rig(null);
        var run = stage.MeasureSteadyAllocation();
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }
}
