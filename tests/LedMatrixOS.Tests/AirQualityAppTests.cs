using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.AirQuality;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class AirQualityAppTests(ITestOutputHelper output)
{
    private const string OpenMeteoJson = """
        {"utc_offset_seconds":0,
         "current":{"time":"2026-01-02T13:00","interval":3600,"european_aqi":35,"pm2_5":9.6,"pm10":14.2,"ozone":61.0,"nitrogen_dioxide":18.5,"uv_index":2.4,
                    "alder_pollen":null,"birch_pollen":null,"grass_pollen":null},
         "hourly":{"time":["2026-01-02T11:00","2026-01-02T12:00","2026-01-02T13:00","2026-01-02T14:00","2026-01-02T15:00"],
                   "european_aqi":[30,32,35,null,41]}}
        """;

    private static AirQualitySnapshot Reading(int aqi, double pm25, double pm10, double uv = 4.5, bool pollen = true) =>
        new FakeAirQualitySource(aqi, pm25, pm10, uv, "London", pollen).GetAsync(new AirQualityQuery("London"), default).Result;

    private static (AirQualityApp App, AppStage Stage) Screen(MutableLive<AirQualitySnapshot> live, int warmFrames = 45)
    {
        Fonts.Load();
        var app = new AirQualityApp(new FakeAirQualitySource()) { Time = new FakeTime() };
        app.UseData(live);
        var stage = new AppStage(app);
        stage.Step(33, warmFrames);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- model -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, AqiBand.Good)]
    [InlineData(19, AqiBand.Good)]
    [InlineData(20, AqiBand.Fair)]
    [InlineData(45, AqiBand.Moderate)]
    [InlineData(79, AqiBand.Poor)]
    [InlineData(95, AqiBand.VeryPoor)]
    [InlineData(140, AqiBand.Extreme)]
    public void Band_FollowsEuropeanAqi(int aqi, AqiBand expected) => Assert.Equal(expected, AirQualityScale.BandOf(aqi));

    [Fact]
    public void Verdict_UsesTheWorseOfAqiAndFineParticles()
    {
        Assert.Equal(AqiBand.Good, AirQualityScale.RunVerdict(10, 3).Band);
        Assert.Equal(AqiBand.Poor, AirQualityScale.RunVerdict(10, 60).Band);
        Assert.Equal(AqiBand.VeryPoor, AirQualityScale.RunVerdict(90, 3).Band);
    }

    [Fact]
    public void Parse_ReadsCurrentAndHourlyAndTolerantOfMissingPollen()
    {
        var s = OpenMeteoAirQualitySource.Parse(OpenMeteoJson, "Leeds");

        Assert.Equal("Leeds", s.Location);
        Assert.Equal(35, s.Aqi);
        Assert.Equal(9.6, s.Pm25);
        Assert.Equal(2.4, s.Uv);
        Assert.Null(s.Alder);
        Assert.Equal([35f, 35f, 41f], s.Hourly);   // starts this hour, a null hour repeats the last value
    }

    [Fact]
    public void Url_UsesTheAirQualityEndpoint()
    {
        var url = OpenMeteoAirQualitySource.Url(51.5, -0.12);
        Assert.StartsWith("https://air-quality-api.open-meteo.com/v1/air-quality?latitude=51.5&longitude=-0.12", url);
        Assert.Contains("european_aqi", url);
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new AirQualityApp(new FakeAirQualitySource());
        Assert.Equal("air-quality", app.Id);
        Assert.Equal(new[] { "location", "pageSeconds" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Equal(AppSettingType.Search, app.GetSettings().Single(s => s.Key == "location").Type);
    }

    [Fact]
    public async Task App_PollsTheSourceWithTheLocation()
    {
        Fonts.Load();
        string? asked = null;
        var app = new AirQualityApp(new RecordingSource(q => asked = q.Location)) { Time = new FakeTime(), Location = "Leeds|53.8,-1.55" };
        await app.OnActivatedAsync((64, 256), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), default);
        for (int i = 0; i < 100 && app.Current is null; i++) await Task.Delay(20);
        await app.OnDeactivatedAsync(default);

        Assert.Equal("Leeds|53.8,-1.55", asked);
        Assert.NotNull(app.Current);
    }

    private sealed class RecordingSource(Action<AirQualityQuery> record) : IAirQualitySource
    {
        public Task<AirQualitySnapshot> GetAsync(AirQualityQuery query, CancellationToken ct)
        {
            record(query);
            return new FakeAirQualitySource().GetAsync(query, ct);
        }
    }

    [Fact]
    public void Golden_Good()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(14, 4.2, 9.1) });
        Golden(stage, "air_quality_good");
    }

    [Fact]
    public void Golden_Moderate()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(48, 22.4, 38, uv: 6.8) });
        Golden(stage, "air_quality_moderate");
    }

    [Fact]
    public void Golden_Poor()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(68, 56, 104, uv: 9.1) });
        Golden(stage, "air_quality_poor");
    }

    [Fact]
    public void Golden_Extreme()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(104, 150, 280, uv: 11.5) });
        Golden(stage, "air_quality_extreme");
    }

    [Fact]
    public void Golden_UvAndPollenPage()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(30, 8, 15, uv: 6.8) }, warmFrames: 270);
        Golden(stage, "air_quality_uv_pollen");
    }

    [Fact]
    public void Golden_NoPollenData()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(30, 8, 15, uv: 1.2, pollen: false) }, warmFrames: 270);
        Golden(stage, "air_quality_no_pollen");
    }

    [Fact]
    public void Golden_Loading()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot>());
        Golden(stage, "air_quality_loading");
    }

    [Fact]
    public void Golden_Error()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Error = new HttpRequestException("offline") });
        Golden(stage, "air_quality_error");
    }

    [Fact]
    public void Golden_UnknownLocation()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Error = new LocationNotFoundException("Nowhereville") });
        Golden(stage, "air_quality_unknown_location");
    }

    [Fact]
    public void Golden_StaleKeepsTheLastReading()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(14, 4.2, 9.1), Error = new HttpRequestException("offline") });
        Golden(stage, "air_quality_stale");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Screen(new MutableLive<AirQualitySnapshot> { Value = Reading(48, 22.4, 38, uv: 6.8) });
        for (int i = 0; i < 100; i++) { stage.Step(33); stage.Render(); }

        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 8; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"air quality: {ms:F3} ms/frame");
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
