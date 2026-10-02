using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

public class WeatherAppTests(ITestOutputHelper output)
{
    private const string ForecastJson = """
        {"utc_offset_seconds":3600,
         "current":{"time":"2026-10-02T14:15","temperature_2m":17.4,"apparent_temperature":15.9,"weather_code":61,"wind_speed_10m":12.3,"wind_direction_10m":225,"is_day":1},
         "hourly":{"time":["2026-10-02T13:00","2026-10-02T14:00","2026-10-02T15:00","2026-10-02T16:00"],
                   "temperature_2m":[17,17.4,16.8,16],"weather_code":[3,61,61,2],"precipitation_probability":[10,60,70,30],"is_day":[1,1,1,1]},
         "daily":{"time":["2026-10-02","2026-10-03"],"weather_code":[61,3],"temperature_2m_max":[18.2,16],"temperature_2m_min":[9.1,8],
                  "sunrise":["2026-10-02T06:52","2026-10-03T06:54"],"sunset":["2026-10-02T18:30","2026-10-03T18:28"],"precipitation_probability_max":[70,20]}}
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            var body = url.Contains("geocoding")
                ? url.Contains("Nowhere") ? "{}" : """{"results":[{"name":"Leeds","latitude":53.8,"longitude":-1.55}]}"""
                : ForecastJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static readonly DateTimeOffset DayTime = new(2026, 1, 2, 13, 45, 7, TimeSpan.Zero);
    private static readonly DateTimeOffset NightTime = new(2026, 1, 2, 22, 30, 0, TimeSpan.Zero);

    private static async Task<(WeatherApp App, long Frame, TimeSpan Time)> Run(IWeatherSource source, DateTimeOffset now, int ms = 4000)
    {
        Fonts.Load();
        var app = new WeatherApp(source) { Time = new FakeTime { Now = now } };
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);
        return (app, 0, TimeSpan.Zero);
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!cond()) { if (DateTime.UtcNow > end) throw new TimeoutException(); await Task.Delay(5); }
    }

    private static TimeSpan Step(WeatherApp app, ref long frame, TimeSpan t, int ms, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var d = TimeSpan.FromMilliseconds(ms);
            t += d;
            app.Update(new FrameContext(t, d, frame++), CancellationToken.None);
        }
        return t;
    }

    private static FrameBuffer Draw(WeatherApp app)
    {
        var f = new FrameBuffer(256, 64);
        app.Render(f, CancellationToken.None);
        return f;
    }

    private static async Task<FrameBuffer> Scene(int code, bool day, double temp = 21, int ms = 5000, DateTimeOffset? now = null)
    {
        var (app, _, _) = await Run(new FakeWeatherSource(code, day, temp), now ?? (day ? DayTime : NightTime));
        await WaitFor(() => app.Current is not null);
        long frame = 0;
        Step(app, ref frame, TimeSpan.Zero, 33, ms / 33);
        var f = Draw(app);
        await app.OnDeactivatedAsync(CancellationToken.None);
        return f;
    }

    public static IEnumerable<object[]> Conditions =>
    [
        [0, "clear"], [2, "partly"], [3, "cloudy"], [45, "fog"], [53, "drizzle"], [65, "rain"], [73, "snow"], [95, "thunder"],
    ];

    [Theory]
    [MemberData(nameof(Conditions))]
    public async Task Golden_Day(int code, string name)
    {
        var f = await Scene(code, true);
        Assert.False(SnapshotHelper.IsBlank(f));
        SnapshotHelper.AssertMatchesSnapshot(f, $"weather_{name}_day");
    }

    [Theory]
    [MemberData(nameof(Conditions))]
    public async Task Golden_Night(int code, string name)
    {
        var f = await Scene(code, false, 4);
        Assert.False(SnapshotHelper.IsBlank(f));
        SnapshotHelper.AssertMatchesSnapshot(f, $"weather_{name}_night");
    }

    [Fact]
    public async Task Golden_NegativeTemperature_FahrenheitPage()
    {
        var f = await Scene(73, true, -8, 5000);
        SnapshotHelper.AssertMatchesSnapshot(f, "weather_snow_negative");
    }

    [Fact]
    public async Task Golden_PagerMidTransition()
    {
        var (app, _, _) = await Run(new FakeWeatherSource(2, true), DayTime);
        await WaitFor(() => app.Current is not null);
        long frame = 0;
        var t = Step(app, ref frame, TimeSpan.Zero, 33, 1);
        t = Step(app, ref frame, t, 33, 6000 / 33 + 6);
        SnapshotHelper.AssertMatchesSnapshot(Draw(app), "weather_pager_mid");
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Golden_Loading()
    {
        var tcs = new TaskCompletionSource<WeatherSnapshot>();
        var (app, _, _) = await Run(new BlockingSource(tcs.Task), DayTime);
        long frame = 0;
        Step(app, ref frame, TimeSpan.Zero, 33, 30);
        SnapshotHelper.AssertMatchesSnapshot(Draw(app), "weather_loading");
        tcs.TrySetCanceled();
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Golden_OfflineNoData()
    {
        var (app, _, _) = await Run(new FakeWeatherSource(fail: () => true), DayTime);
        await WaitFor(() => app.HasError);
        long frame = 0;
        Step(app, ref frame, TimeSpan.Zero, 33, 30);
        SnapshotHelper.AssertMatchesSnapshot(Draw(app), "weather_offline");
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    private sealed class BlockingSource(Task<WeatherSnapshot> task) : IWeatherSource
    {
        public Task<WeatherSnapshot> GetAsync(WeatherQuery q, CancellationToken ct) => task;
    }

    [Fact]
    public void Parse_CannedOpenMeteoJson()
    {
        var s = OpenMeteoWeatherSource.Parse(ForecastJson, "Leeds", false);
        Assert.Equal("Leeds", s.Location);
        Assert.Equal(17.4, s.Temp);
        Assert.Equal(61, s.Code);
        Assert.Equal(WeatherKind.Rain, s.Kind);
        Assert.Equal(70, s.PrecipChance);
        Assert.Equal(18.2, s.High);
        Assert.Equal(new TimeSpan(6, 52, 0), s.Sunrise);
        Assert.Equal(TimeSpan.FromHours(1), s.UtcOffset);
        Assert.Equal(3, s.Hourly.Count); // the 13:00 point is in the past
        Assert.Equal(2, s.Daily.Count);
    }

    [Fact]
    public async Task OpenMeteoSource_GeocodesOnceAndFetches()
    {
        var h = new StubHandler();
        var src = new OpenMeteoWeatherSource(new HttpClient(h));
        var a = await src.GetAsync(new WeatherQuery("Leeds", true), CancellationToken.None);
        await src.GetAsync(new WeatherQuery("Leeds", true), CancellationToken.None);
        Assert.Equal("Leeds", a.Location);
        Assert.Single(h.Urls, u => u.Contains("geocoding"));
        Assert.Contains(h.Urls, u => u.Contains("temperature_unit=fahrenheit") && u.Contains("latitude=53.8"));
        await Assert.ThrowsAsync<LocationNotFoundException>(() => src.GetAsync(new WeatherQuery("Nowhere", false), CancellationToken.None));
    }

    [Fact]
    public void WeatherCodes_MapKindsAndText()
    {
        Assert.Equal(WeatherKind.Thunderstorm, WeatherCodes.KindOf(96));
        Assert.Equal(WeatherKind.Snow, WeatherCodes.KindOf(85));
        Assert.Equal("Sunny", WeatherCodes.Describe(0, true));
        Assert.Equal("Clear", WeatherCodes.Describe(0, false));
        Assert.Equal(3, WeatherCodes.IntensityOf(65));
    }

    [Fact]
    public async Task FakeSource_IsDeterministic()
    {
        var a = await new FakeWeatherSource(61).GetAsync(new WeatherQuery("x", false), default);
        var b = await new FakeWeatherSource(61).GetAsync(new WeatherQuery("x", false), default);
        Assert.Equal(a.Hourly.Select(h => h.Temp), b.Hourly.Select(h => h.Temp));
    }

    [Fact]
    public void Settings_KeepKeysAndRoundTripPersistedValues()
    {
        var app = new WeatherApp(new FakeWeatherSource());
        Assert.Equal(new[] { "location", "units", "pageSeconds" }, app.GetSettings().Select(s => s.Key).ToArray());
        app.UpdateSetting("location", JsonDocument.Parse("\"Leeds\"").RootElement.Clone());
        app.UpdateSetting("units", JsonDocument.Parse("\"Fahrenheit\"").RootElement.Clone());
        app.UpdateSetting("pageSeconds", JsonDocument.Parse("99").RootElement.Clone());
        Assert.Equal("Leeds", app.Location);
        Assert.Equal("Fahrenheit", app.Units);
        Assert.Equal(30, app.PageSeconds);
        app.UpdateSetting("units", "Kelvin");
        Assert.Equal("Fahrenheit", app.Units);
    }

    [Fact]
    public async Task SteadyState_DoesNotAllocate()
    {
        foreach (var code in new[] { 0, 65, 73, 95 })
        {
            var (app, _, _) = await Run(new FakeWeatherSource(code, code != 0), DayTime);
            await WaitFor(() => app.Current is not null);
            long frame = 0;
            var t = Step(app, ref frame, TimeSpan.Zero, 33, 400); var warm = new FrameBuffer(256, 64); for (int i = 0; i < 300; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(warm, default); }
            var f = new FrameBuffer(256, 64);
            for (int i = 0; i < 20; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(f, default); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(f, default); }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"code {code}: {bytes} bytes / 300 frames");
            Assert.True(bytes < 8_000, $"code {code} allocated {bytes} bytes");
            await app.OnDeactivatedAsync(default);
        }
    }

    [Fact]
    public async Task Perf_ReportsFrameTime()
    {
        foreach (var code in new[] { 0, 65, 73, 95 })
        {
            var (app, _, _) = await Run(new FakeWeatherSource(code), DayTime);
            await WaitFor(() => app.Current is not null);
            long frame = 0;
            var t = Step(app, ref frame, TimeSpan.Zero, 33, 200);
            var f = new FrameBuffer(256, 64);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 300; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(f, default); }
            output.WriteLine($"code {code}: {sw.Elapsed.TotalMilliseconds / 300:F2} ms/frame");
            Assert.True(sw.Elapsed.TotalMilliseconds / 300 < 25);
            await app.OnDeactivatedAsync(default);
        }
    }
}
