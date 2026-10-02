using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

public class TubeLineAppTests(ITestOutputHelper output)
{
    private static string Stations()
    {
        var names = new[] { "Stanmore", "Canons Park", "Queensbury", "Kingsbury", "Wembley Park", "Neasden", "Dollis Hill", "Willesden Green", "Kilburn", "West Hampstead",
            "Finchley Road", "Swiss Cottage", "St. John's Wood", "Baker Street", "Bond Street", "Green Park", "Westminster", "Waterloo", "Southwark", "London Bridge",
            "Bermondsey", "Canada Water", "Canary Wharf", "North Greenwich", "Canning Town", "West Ham", "Stratford" };
        // Listed east to west to prove the app flips the route.
        var rev = names.Reverse().ToArray();
        var stations = string.Join(",", names.Select((n, i) => $$"""{"id":"940GZZLU{{i:D3}}","name":"{{n}} Underground Station","lat":51.5,"lon":{{-0.4 + i * 0.03}}}"""));
        var ids = string.Join(",", rev.Select(n => $"\"940GZZLU{Array.IndexOf(names, n):D3}\""));
        return $$"""{"stations":[{{stations}}],"orderedLineRoutes":[{"name":"Stratford - Stanmore","naptanIds":[{{ids}}],"serviceType":"Regular"},{"name":"Short","naptanIds":["940GZZLU000","940GZZLU001"],"serviceType":"Irregular"}]}""";
    }

    private static readonly string Arrivals = """
        [{"vehicleId":"A1","stationName":"Baker Street Underground Station","naptanId":"940GZZLU013","timeToStation":60,"direction":"inbound","destinationName":"Stratford"},
         {"vehicleId":"A1","stationName":"Bond Street Underground Station","naptanId":"940GZZLU014","timeToStation":300,"direction":"inbound","destinationName":"Stratford"},
         {"vehicleId":"B2","stationName":"Canary Wharf Underground Station","naptanId":"940GZZLU022","timeToStation":150,"direction":"outbound","destinationName":"Stanmore"},
         {"vehicleId":"C3","stationName":"Nowhere","naptanId":"nope","timeToStation":10,"direction":"outbound","destinationName":"x"}]
        """;

    private const string GoodStatus = """[{"id":"jubilee","lineStatuses":[{"statusSeverity":10,"statusSeverityDescription":"Good Service"}]}]""";

    private const string BadStatus = """
        [{"id":"jubilee","lineStatuses":[{"statusSeverity":9,"statusSeverityDescription":"Minor Delays","reason":"JUBILEE LINE: Minor delays between Stratford and Canning Town due to an earlier signal failure."}]}]
        """;

    private sealed class StubHandler(string status = GoodStatus) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            lock (Urls) Urls.Add(url);
            var body = url.Contains("/Route/Sequence") ? Stations() : url.Contains("/Arrivals") ? Arrivals : url.Contains("/Status") ? status : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static readonly DateTimeOffset T0 = new(2026, 1, 2, 13, 45, 7, TimeSpan.Zero);

    private static async Task<TubeLineApp> Started(HttpMessageHandler handler, string line = "jubilee")
    {
        Fonts.Load();
        var app = new TubeLineApp(new HttpClient(handler)) { Time = new FakeTime { Now = T0 } };
        app.UpdateSetting("lineId", line);
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);
        return app;
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!cond()) { if (DateTime.UtcNow > end) throw new TimeoutException(); await Task.Delay(5); }
    }

    private static TimeSpan Step(TubeLineApp app, ref long frame, TimeSpan t, int ms, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var d = TimeSpan.FromMilliseconds(ms);
            t += d;
            app.Update(new FrameContext(t, d, frame++), CancellationToken.None);
        }
        return t;
    }

    private static FrameBuffer Draw(TubeLineApp app)
    {
        var f = new FrameBuffer(256, 64);
        app.Render(f, CancellationToken.None);
        return f;
    }

    [Fact]
    public async Task Fetch_ParsesRouteTrainsAndStatus()
    {
        var client = new TubeLineClient(new HttpClient(new StubHandler()));
        var snap = await client.FetchAsync("jubilee", false, "auto", CancellationToken.None);
        Assert.Equal(27, snap.Stops.Count);
        Assert.Equal("Stanmore Underground Station", snap.Stops[0].Name); // flipped west to east
        Assert.Equal(2, snap.Trains.Count); // C3 is not on the route
        var a1 = snap.Trains.Single(t => t.Id == "A1");
        Assert.Equal(13, a1.StopIndex);
        Assert.Equal(60, a1.Seconds); // the nearest of the vehicle's arrivals
        Assert.Equal("Baker Street", a1.NextStation);
        Assert.Equal(ServiceHealth.Good, snap.Status.Health);
        Assert.Contains("Stratford - Stanmore", snap.RouteOptions);
    }

    [Fact]
    public async Task Fetch_PinnedBranchUsesThatRoute_AndCachesRoute()
    {
        var h = new StubHandler();
        var client = new TubeLineClient(new HttpClient(h), "key123");
        var snap = await client.FetchAsync("jubilee", true, "Short", CancellationToken.None);
        Assert.Equal(2, snap.Stops.Count);
        await client.FetchAsync("jubilee", false, "auto", CancellationToken.None);
        Assert.Single(h.Urls, u => u.Contains("/Route/Sequence"));
        Assert.All(h.Urls, u => Assert.Contains("app_key=key123", u));
    }

    [Fact]
    public void ParseStatus_PicksWorstAndReason()
    {
        var s = TubeLineClient.ParseStatus(BadStatus);
        Assert.Equal(ServiceHealth.Minor, s.Health);
        Assert.Contains("signal failure", s.Reason);
        Assert.Equal(ServiceHealth.Severe, TubeLineClient.Classify(2));
        Assert.Equal(ServiceHealth.Good, TubeLineClient.Classify(10));
    }

    [Fact]
    public void Settings_KeepKeysAndAcceptPersistedValues()
    {
        var app = new TubeLineApp(new HttpClient(new StubHandler()));
        var s = app.GetSettings().ToList();
        Assert.Equal(new[] { "lineId", "branchMode", "branchRoute" }, s.Select(x => x.Key).ToArray());
        Assert.All(s, x => Assert.Equal(AppSettingType.Select, x.Type));
        Assert.Equal("jubilee", s[0].CurrentValue);
        app.UpdateSetting("lineId", JsonDocument.Parse("\"Victoria Line\"").RootElement.Clone());
        Assert.Equal("victoria", app.LineId);
        app.UpdateSetting("lineId", "overground");
        Assert.Equal("london-overground", app.LineId);
        app.UpdateSetting("lineId", "nonsense");
        Assert.Equal("jubilee", app.LineId);
        app.UpdateSetting("branchMode", JsonDocument.Parse("\"Pinned\"").RootElement.Clone());
        Assert.Equal("Pinned", app.BranchMode);
        app.UpdateSetting("branchRoute", "not-a-route");
        Assert.Equal("auto", app.BranchRoute);
    }

    [Theory]
    [InlineData("jubilee", false)]
    [InlineData("circle", false)]
    [InlineData("jubilee", true)]
    public async Task Golden_Line(string line, bool disrupted)
    {
        var app = await Started(new StubHandler(disrupted ? BadStatus : GoodStatus), line);
        await WaitFor(() => app.Current is not null);
        long frame = 0;
        Step(app, ref frame, TimeSpan.Zero, 33, 120);
        var f = Draw(app);
        Assert.False(SnapshotHelper.IsBlank(f));
        SnapshotHelper.AssertMatchesSnapshot(f, $"tubeline_{line}_{(disrupted ? "disrupted" : "good")}");
        await app.OnDeactivatedAsync(default);
    }

    [Fact]
    public async Task Golden_Loading()
    {
        var gate = new TaskCompletionSource();
        var app = await Started(new GateHandler(gate.Task));
        long frame = 0;
        Step(app, ref frame, TimeSpan.Zero, 33, 30);
        SnapshotHelper.AssertMatchesSnapshot(Draw(app), "tubeline_loading");
        gate.TrySetCanceled();
        await app.OnDeactivatedAsync(default);
    }

    [Fact]
    public async Task Golden_NoData()
    {
        var app = await Started(new FailHandler());
        await WaitFor(() => app.HasError);
        long frame = 0;
        Step(app, ref frame, TimeSpan.Zero, 33, 30);
        SnapshotHelper.AssertMatchesSnapshot(Draw(app), "tubeline_nodata");
        await app.OnDeactivatedAsync(default);
    }

    [Fact]
    public async Task Golden_PagerMidTransition()
    {
        var app = await Started(new StubHandler());
        await WaitFor(() => app.Current is not null);
        long frame = 0;
        var t = Step(app, ref frame, TimeSpan.Zero, 33, 1);
        Draw(app);
        t = Step(app, ref frame, t, 33, 5000 / 33 + 4);
        SnapshotHelper.AssertMatchesSnapshot(Draw(app), "tubeline_pager_mid");
        await app.OnDeactivatedAsync(default);
    }

    private sealed class GateHandler(Task gate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    [Fact]
    public async Task SteadyState_DoesNotAllocate()
    {
        var app = await Started(new StubHandler(BadStatus));
        await WaitFor(() => app.Current is not null);
        long frame = 0;
        var f = new FrameBuffer(256, 64);
        var t = TimeSpan.Zero;
        for (int i = 0; i < 700; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(f, default); }
        // Measure inside one 4 s spotlight slot: moving the spotlight re-rasterises its label (a one-off allocation per train change).
        while (t.TotalSeconds % 4 > 0.05) t = Step(app, ref frame, t, 33, 1);
        t = Step(app, ref frame, t, 33, 3);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 90; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(f, default); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{bytes} bytes / 90 frames");
        Assert.True(bytes < 4_000, $"allocated {bytes}");
        await app.OnDeactivatedAsync(default);
    }

    [Fact]
    public async Task Perf_ReportsFrameTime()
    {
        var app = await Started(new StubHandler());
        await WaitFor(() => app.Current is not null);
        long frame = 0;
        var f = new FrameBuffer(256, 64);
        var t = TimeSpan.Zero;
        for (int i = 0; i < 200; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(f, default); }
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++) { t = Step(app, ref frame, t, 33, 1); app.Render(f, default); }
        output.WriteLine($"{sw.Elapsed.TotalMilliseconds / 300:F2} ms/frame");
        Assert.True(sw.Elapsed.TotalMilliseconds / 300 < 25);
        await app.OnDeactivatedAsync(default);
    }
}
