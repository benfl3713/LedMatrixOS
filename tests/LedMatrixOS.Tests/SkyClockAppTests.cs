using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Sky;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class SkyClockAppTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Midsummer = new(2026, 6, 21, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Midwinter = new(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);

    private static (SkyClockApp App, AppStage Stage) Rig(DateTimeOffset when, WeatherKind kind = WeatherKind.Clear, string alarm = "", int lead = 30, double lat = 51.5072, double lon = -0.1276)
    {
        Fonts.Load();
        var app = new SkyClockApp(new HttpClient()) { Time = new FakeTime { Now = when }, AlarmTime = alarm, AlarmLeadMinutes = lead };
        app.UseData(new MutableLive<SkyData> { Value = new SkyData("London", lat, lon, kind) });
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

    // ---- maths -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Sun_MidsummerNoonInLondon_IsHighInTheSky()
    {
        // Solar noon in London on 21 June is about 12:59 BST... i.e. 11:58 UTC; elevation is 90 - 51.5 + 23.4 = 61.9 degrees.
        var sun = SolarMath.SunAt(new DateTimeOffset(2026, 6, 21, 11, 58, 0, TimeSpan.Zero), 51.5072, -0.1276);
        Assert.InRange(sun.Altitude, 60.5, 63);
        Assert.True(sun.IsDay);
        Assert.InRange(sun.Progress, 0.48, 0.52);
    }

    [Fact]
    public void Sun_MidnightIsBelowTheHorizon_AndNightProgressRuns0To1()
    {
        var at = (int h, int m) => SolarMath.SunAt(new DateTimeOffset(2026, 6, 21, h, m, 0, TimeSpan.Zero), 51.5072, -0.1276);
        Assert.True(at(0, 0).Altitude < -10);
        Assert.False(at(0, 0).IsDay);
        Assert.True(at(21, 0).Progress < 0.2);          // soon after sunset
        Assert.InRange(at(0, 0).Progress, 0.4, 0.6);    // the middle of the night
        Assert.True(at(2, 30).Progress > 0.8);          // before sunrise
    }

    [Fact]
    public void Sun_SunriseAndSunsetTimesAreRealistic()
    {
        // London 21 June: sunrise ~03:43 UTC, sunset ~20:21 UTC.
        Assert.True(SolarMath.SunAt(new DateTimeOffset(2026, 6, 21, 3, 30, 0, TimeSpan.Zero), 51.5072, -0.1276).Altitude < 0);
        Assert.True(SolarMath.SunAt(new DateTimeOffset(2026, 6, 21, 3, 55, 0, TimeSpan.Zero), 51.5072, -0.1276).Altitude > 0);
        Assert.True(SolarMath.SunAt(new DateTimeOffset(2026, 6, 21, 20, 10, 0, TimeSpan.Zero), 51.5072, -0.1276).Altitude > 0);
        Assert.True(SolarMath.SunAt(new DateTimeOffset(2026, 6, 21, 20, 35, 0, TimeSpan.Zero), 51.5072, -0.1276).Altitude < 0);
    }

    [Fact]
    public void Sun_PolarExtremes_DoNotBreak()
    {
        var polarNight = SolarMath.SunAt(Midwinter, 78, 15);
        Assert.False(polarNight.IsDay);
        Assert.InRange(polarNight.Progress, 0, 1);
        var midnightSun = SolarMath.SunAt(Midsummer, 78, 15);
        Assert.True(midnightSun.IsDay);
        Assert.InRange(midnightSun.Progress, 0, 1);
    }

    [Fact]
    public void Moon_AgeIsAFractionAndFullMoonIsHalf()
    {
        // 2026-01-03 10:03 UTC was a full moon.
        Assert.InRange(SolarMath.MoonAge(new DateTimeOffset(2026, 1, 3, 10, 0, 0, TimeSpan.Zero)), 0.47, 0.53);
        Assert.InRange(SolarMath.MoonAge(Midsummer), 0, 1);
    }

    [Fact]
    public void SkyPalette_IsDarkAtNightAndBrightByDay()
    {
        SkyPalette.Evaluate(-30, out var nightZenith, out _);
        SkyPalette.Evaluate(40, out var dayZenith, out var dayHorizon);
        Assert.True(nightZenith.B < 30);
        Assert.True(dayZenith.B > 200);
        Assert.True(dayHorizon.G > 150);
    }

    // ---- alarm -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("07:00", "05:00", 0.0)]      // well before the glow starts
    [InlineData("07:00", "06:30", 0.0)]      // exactly at the start of the lead
    [InlineData("07:00", "06:45", 0.5)]
    [InlineData("07:00", "07:00", 1.0)]
    [InlineData("07:00", "07:08", 1.0)]      // held after the alarm
    [InlineData("07:00", "07:11", 0.0)]
    [InlineData("00:10", "23:55", 0.5)]      // wraps over midnight (lead 30)
    [InlineData("", "06:45", 0.0)]
    [InlineData("nonsense", "06:45", 0.0)]
    public void AlarmGlow_RampsUpToTheAlarmTime(string alarm, string now, double expected)
    {
        var app = new SkyClockApp(new HttpClient()) { AlarmTime = alarm, AlarmLeadMinutes = 30 };
        Assert.Equal(expected, app.AlarmGlow(TimeSpan.Parse(now)), 3);
    }

    [Fact]
    public void Alarm_PushesTheSkyTowardSunriseAndShowsTheSun()
    {
        var (plain, _) = Rig(new DateTimeOffset(2026, 12, 15, 6, 50, 0, TimeSpan.Zero));
        var (woken, _) = Rig(new DateTimeOffset(2026, 12, 15, 6, 50, 0, TimeSpan.Zero), alarm: "07:00");
        Assert.True(woken.Sky!.Altitude > plain.Sky!.Altitude + 3);
        Assert.True(woken.Sky.SunVisible);
        Assert.False(woken.Sky.MoonVisible);
        Assert.False(plain.Sky.SunVisible);
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new SkyClockApp(new HttpClient());
        Assert.Equal("sky-clock", app.Id);
        Assert.Equal(new[] { "location", "alarmTime", "alarmLeadMinutes" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Contains(typeof(SkyClockApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void App_PutsTheSunByDayAndTheMoonByNight()
    {
        var (day, _) = Rig(Midsummer);
        Assert.True(day.Sky!.SunVisible);
        Assert.False(day.Sky.MoonVisible);

        var (night, _) = Rig(new DateTimeOffset(2026, 6, 21, 23, 30, 0, TimeSpan.Zero));
        Assert.False(night.Sky!.SunVisible);
        Assert.True(night.Sky.MoonVisible);
    }

    [Fact]
    public void App_SunMovesAcrossTheArcThroughTheDay()
    {
        var (morning, _) = Rig(new DateTimeOffset(2026, 6, 21, 6, 0, 0, TimeSpan.Zero));
        var (noon, _) = Rig(new DateTimeOffset(2026, 6, 21, 11, 58, 0, TimeSpan.Zero));
        var (evening, _) = Rig(new DateTimeOffset(2026, 6, 21, 18, 0, 0, TimeSpan.Zero));
        Assert.True(morning.Sky!.SunX < noon.Sky!.SunX && noon.Sky.SunX < evening.Sky!.SunX);
        Assert.True(noon.Sky.SunY < morning.Sky.SunY && noon.Sky.SunY < evening.Sky.SunY);
    }

    [Fact]
    public void App_WithoutWeatherData_FallsBackToClear()
    {
        Fonts.Load();
        var app = new SkyClockApp(new HttpClient()) { Time = new FakeTime { Now = Midsummer } };
        var stage = new AppStage(app);
        stage.Step(33, 3);
        Assert.Equal(WeatherKind.Clear, app.Sky!.Kind);
        Assert.True(app.Sky.SunVisible);
    }

    [Fact]
    public async Task Source_ResolvesPlaceAndWeather_AndSurvivesWeatherFailure()
    {
        var places = new PlaceResolver(new HttpClient());
        var ok = new OpenMeteoSkySource(places, new FakeWeatherSource(code: 61));
        var data = await ok.GetAsync("51.5,-0.12", default);
        Assert.Equal(WeatherKind.Rain, data.Kind);
        Assert.Equal(51.5, data.Latitude);

        var failing = new OpenMeteoSkySource(places, new FakeWeatherSource(fail: () => true));
        Assert.Equal(WeatherKind.Clear, (await failing.GetAsync("51.5,-0.12", default)).Kind);
    }

    // ---- goldens ---------------------------------------------------------------------------------------------------

    [Fact] public void Golden_ClearNoon() => Golden(Rig(Midsummer).Stage, "sky_clock_clear_noon");
    [Fact] public void Golden_PartlyCloudyMorning() => Golden(Rig(new DateTimeOffset(2026, 6, 21, 5, 30, 0, TimeSpan.Zero), WeatherKind.PartlyCloudy).Stage, "sky_clock_partly_cloudy_morning");
    [Fact] public void Golden_Sunset() => Golden(Rig(new DateTimeOffset(2026, 6, 21, 19, 45, 0, TimeSpan.Zero), WeatherKind.PartlyCloudy).Stage, "sky_clock_sunset");
    [Fact] public void Golden_NightWithMoon() => Golden(Rig(new DateTimeOffset(2026, 1, 3, 22, 0, 0, TimeSpan.Zero)).Stage, "sky_clock_night_moon");
    [Fact] public void Golden_RainyAfternoon() => Golden(Rig(new DateTimeOffset(2026, 12, 15, 13, 0, 0, TimeSpan.Zero), WeatherKind.Rain).Stage, "sky_clock_rain");
    [Fact] public void Golden_SnowyDusk() => Golden(Rig(new DateTimeOffset(2026, 12, 15, 15, 45, 0, TimeSpan.Zero), WeatherKind.Snow).Stage, "sky_clock_snow_dusk");
    [Fact] public void Golden_AlarmGlow() => Golden(Rig(new DateTimeOffset(2026, 12, 15, 6, 50, 0, TimeSpan.Zero), alarm: "07:00").Stage, "sky_clock_alarm");

    // ---- performance -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WeatherKind.Clear)]
    [InlineData(WeatherKind.Cloudy)]
    [InlineData(WeatherKind.Rain)]
    public void SteadyState_DoesNotAllocate(WeatherKind kind)
    {
        var (_, stage) = Rig(Midsummer, kind);
        var run = stage.MeasureSteadyAllocation();
        output.WriteLine($"sky-clock {kind}: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }

    [Fact]
    public void SteadyState_AtNightWithStars_DoesNotAllocate()
    {
        var (_, stage) = Rig(new DateTimeOffset(2026, 1, 3, 22, 0, 0, TimeSpan.Zero));
        var run = stage.MeasureSteadyAllocation();
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }
}
