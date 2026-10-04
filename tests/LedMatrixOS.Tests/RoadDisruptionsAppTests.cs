using System.Diagnostics;
using System.Net;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Attention;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Scheduling;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class RoadDisruptionsAppTests(ITestOutputHelper output)
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> Respond { get; set; } = _ => (HttpStatusCode.OK, "[]");
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Urls) Urls.Add(request.RequestUri!.ToString());
            var (status, body) = Respond(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static RoadDisruption Road(string id, RoadSeverity severity, string corridorId, string location, string comment = "") =>
        new(id, severity, TflApi.PrettyCorridor(corridorId), corridorId, location, comment, "Works");

    private static RoadDisruption[] Disruptions() =>
    [
        Road("TIMS-1", RoadSeverity.Severe, "a406", "A406 North Circular Road both ways at Hanger Lane", "Closed after a collision, expect long delays"),
        Road("TIMS-2", RoadSeverity.Serious, "a2", "A2 Blackheath Road eastbound", "Lane closures for carriageway repairs until 05:00"),
        Road("TIMS-3", RoadSeverity.Serious, "a40", "A40 Western Avenue at Hillingdon", "Burst water main"),
        Road("TIMS-4", RoadSeverity.Moderate, "a13", "A13 Newham Way westbound", "Traffic signal faults"),
        Road("TIMS-5", RoadSeverity.Moderate, "blackwall-tunnel-northbound-approach", "Blackwall Tunnel northbound approach", "Gas works"),
        Road("TIMS-6", RoadSeverity.Minimal, "a10", "A10 Kingsland Road", "Resurfacing"),
    ];

    private static (RoadDisruptionsApp App, AppStage Stage) Board(RoadDisruption[]? data, string minSeverity = "Moderate", Exception? error = null)
    {
        Fonts.Load();
        var app = new RoadDisruptionsApp(new HttpClient(new FakeHandler())) { Time = new FakeTime(), MinSeverity = minSeverity };
        app.UseData(new MutableLive<RoadDisruption[]> { Value = data, Error = error });
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

    // ---- API parsing -----------------------------------------------------------------------------------------------

    private const string DisruptionJson = """
        [
          {"id":"TIMS-9","category":"Works","severity":"Minimal","location":"A10 Kingsland Road","comments":"Resurfacing","corridorIds":["a10"]},
          {"id":"TIMS-2","category":"Collisions","severity":"Severe","location":"A406   North Circular\n at Hanger Lane","comments":"","currentUpdate":"Closed after a collision.","corridorIds":["a406","north-circular-a406"]},
          {"id":"TIMS-3","category":"Works","severity":"Moderate","location":"A2","comments":"Lane closed","corridorIds":[]},
          {"id":"TIMS-2","category":"Collisions","severity":"Severe","location":"duplicate","comments":"","corridorIds":["a406"]},
          {"id":"","severity":"Severe","corridorIds":["a1"]}
        ]
        """;

    [Fact]
    public async Task Api_ParsesShapesAndSortsWorstFirst()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, DisruptionJson) };
        var api = new TflApi(new HttpClient(handler)) { AppKey = "k" };

        var result = await api.GetRoadDisruptionsAsync(null, CancellationToken.None);

        Assert.Contains("/Road/all/Disruption", handler.Urls[0]);
        Assert.Contains("app_key=k", handler.Urls[0]);
        Assert.Equal(["TIMS-2", "TIMS-3", "TIMS-9"], result.Select(d => d.Id).ToArray());   // duplicate and blank ids dropped
        Assert.Equal(RoadSeverity.Severe, result[0].Severity);
        Assert.Equal("A406", result[0].Corridor);
        Assert.Equal("a406,north-circular-a406", result[0].Corridors);
        Assert.Equal("A406 North Circular at Hanger Lane", result[0].Location);
        Assert.Equal("Closed after a collision.", result[0].Comment);   // falls back to currentUpdate
        Assert.Equal("Road", result[1].Corridor);
    }

    [Fact]
    public async Task Api_AsksForTheGivenCorridors()
    {
        var handler = new FakeHandler();
        var api = new TflApi(new HttpClient(handler));

        await api.GetRoadDisruptionsAsync(["A406", " a2 ", ""], CancellationToken.None);

        Assert.EndsWith("/Road/a406,a2/Disruption", handler.Urls[0].Replace("%2C", ","));
    }

    [Fact]
    public async Task Api_Corridors_AreSortedAndNamed()
    {
        var handler = new FakeHandler
        {
            Respond = _ => (HttpStatusCode.OK, """[{"id":"A406","displayName":"North Circular (A406)"},{"id":"a1","displayName":"A1"},{"id":"a1"},{"id":"m25"},{"id":""}]"""),
        };
        var api = new TflApi(new HttpClient(handler));

        var roads = await api.GetRoadCorridorsAsync(CancellationToken.None);

        Assert.EndsWith("/Road", handler.Urls[0]);
        Assert.Equal(["a1", "m25", "a406"], roads.Select(r => r.Id).ToArray());   // by name: A1, M25, North Circular
        Assert.Equal("North Circular (A406)", roads[2].Name);
        Assert.Equal("M25", roads[1].Name);
    }

    [Theory]
    [InlineData("Severe", RoadSeverity.Severe)]
    [InlineData("serious", RoadSeverity.Serious)]
    [InlineData(" Moderate ", RoadSeverity.Moderate)]
    [InlineData("Minimal", RoadSeverity.Minimal)]
    [InlineData("Something else", RoadSeverity.Minimal)]
    [InlineData(null, RoadSeverity.Minimal)]
    public void Api_SeverityParsing(string? text, RoadSeverity expected) => Assert.Equal(expected, TflApi.ParseRoadSeverity(text));

    [Theory]
    [InlineData("a406", "A406")]
    [InlineData("north-circular-a406", "North Circular A406")]
    [InlineData("blackwall-tunnel", "Blackwall Tunnel")]
    public void Api_PrettyCorridor(string id, string expected) => Assert.Equal(expected, TflApi.PrettyCorridor(id));

    // ---- corridor picker -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Options_ListTheRoadsAndFilterByQuery()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, """[{"id":"a1","displayName":"A1"},{"id":"a406","displayName":"North Circular (A406)"}]""") };
        var provider = new TflRoadOptions(new TflApi(new HttpClient(handler)));

        Assert.True(provider.Browse);
        var all = await provider.GetOptionsAsync("road-disruptions", "corridors", "", CancellationToken.None);
        var filtered = await provider.GetOptionsAsync("road-disruptions", "corridors", "circ", CancellationToken.None);
        var label = await provider.GetLabelAsync("road-disruptions", "corridors", "A406", CancellationToken.None);
        var unknown = await provider.GetLabelAsync("road-disruptions", "corridors", "a99", CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Equal("a406", Assert.Single(filtered).Value);
        Assert.Equal("North Circular (A406)", label);
        Assert.Equal("A99", unknown);
        Assert.Single(handler.Urls);   // cached
    }

    [Fact]
    public void Options_AreRegisteredForTheApp()
    {
        var registry = new SettingOptionsRegistry();
        BuiltInSettingOptions.Register(registry, new HttpClient(new FakeHandler()), new ConfigurationBuilder().Build());
        Assert.True(registry.Has("road-disruptions", "corridors"));
        Assert.True(registry.IsBrowse("road-disruptions", "corridors"));
    }

    // ---- attention source ------------------------------------------------------------------------------------------

    private static string Json(string severity, string corridor = "a406") =>
        $$"""[{"id":"1","severity":"{{severity}}","location":"x","comments":"y","corridorIds":["{{corridor}}"]}]""";

    [Fact]
    public async Task Condition_FollowsSeverityThreshold()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, Json("Serious")) };
        using var source = new RoadDisruptionSource(new HttpClient(handler), "key", RoadSeverity.Serious, new FakeTime());

        Assert.Equal("road_disrupted", source.Kind);
        await source.RefreshAsync(["a406"], CancellationToken.None);
        Assert.True(source.IsActive("a406"));
        Assert.Contains("/Road/a406/Disruption", handler.Urls[0]);
        Assert.Contains("app_key=key", handler.Urls[0]);

        handler.Respond = _ => (HttpStatusCode.OK, Json("Moderate"));
        await source.RefreshAsync(["a406"], CancellationToken.None);
        Assert.False(source.IsActive("a406"));

        handler.Respond = _ => (HttpStatusCode.OK, "[]");
        await source.RefreshAsync(["a406"], CancellationToken.None);
        Assert.False(source.IsActive("a406"));
    }

    [Fact]
    public async Task Condition_AnyWatchesTheWholeNetwork_AndErrorsAreFalse()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, Json("Severe")) };
        using var source = new RoadDisruptionSource(new HttpClient(handler), null, RoadSeverity.Serious, new FakeTime());

        await source.RefreshAsync(["any"], CancellationToken.None);
        Assert.True(source.IsActive("ANY"));
        Assert.Contains("/Road/all/Disruption", handler.Urls[0]);

        handler.Respond = _ => (HttpStatusCode.NotFound, "no such road");
        await source.RefreshAsync(["any"], CancellationToken.None);
        Assert.False(source.IsActive("any"));
    }

    [Fact]
    public async Task Condition_MinimumSeverityComesFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Attention:RoadMinSeverity"] = "moderate", ["TFL:AppKey"] = "k" }).Build();
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, Json("Moderate")) };
        using var source = new RoadDisruptionSource(new HttpClient(handler), config, new FakeTime());

        Assert.Equal(RoadSeverity.Moderate, source.MinSeverity);
        await source.RefreshAsync(["any"], CancellationToken.None);
        Assert.True(source.IsActive("any"));
        using var fallback = new RoadDisruptionSource(new HttpClient(handler), new ConfigurationBuilder().Build(), new FakeTime());
        Assert.Equal(RoadSeverity.Serious, fallback.MinSeverity);
    }

    [Fact]
    public async Task Condition_WorksThroughTheEvaluator()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, Json("Severe")) };
        using var source = new RoadDisruptionSource(new HttpClient(handler), null, RoadSeverity.Serious, new FakeTime());
        var evaluator = new AttentionEvaluator([source]);

        await source.RefreshAsync(["a406"], CancellationToken.None);

        Assert.True(evaluator.Evaluate("road_disrupted:a406"));
        Assert.False(evaluator.Evaluate("road_disrupted:a2"));
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new RoadDisruptionsApp(new HttpClient(new FakeHandler()));
        Assert.Equal("road-disruptions", app.Id);
        Assert.Equal(new[] { "corridors", "minSeverity", "pageSeconds" }, app.GetSettings().Select(s => s.Key).ToArray());
        var corridors = app.GetSettings().Single(s => s.Key == "corridors");
        Assert.Equal(AppSettingType.MultiSearch, corridors.Type);
        Assert.True(corridors.Browse);
        Assert.Equal(AppSettingType.Select, app.GetSettings().Single(s => s.Key == "minSeverity").Type);
    }

    [Fact]
    public void ParseCorridors_TrimsLowersDedupesAndCaps()
    {
        Assert.Equal(["a406", "a2"], RoadDisruptionsApp.ParseCorridors(" A406, a2 ,a406,,"));
        Assert.Equal(RoadDisruptionsApp.MaxCorridors, RoadDisruptionsApp.ParseCorridors("a1,a2,a3,a4,a5,a6,a7,a8,a9,a10").Length);
        Assert.Empty(RoadDisruptionsApp.ParseCorridors(null));
    }

    [Theory]
    [InlineData("Minimal", 6)]
    [InlineData("Moderate", 5)]
    [InlineData("Serious", 3)]
    [InlineData("Severe", 1)]
    [InlineData("bogus", 3)]
    public void MinimumSeverity_FiltersAndOrdersWorstFirst(string min, int expected)
    {
        var (app, _) = Board(Disruptions().Reverse().ToArray(), min);
        Assert.Equal(expected, app.Visible.Count);
        Assert.Equal(app.Visible.OrderByDescending(d => d.Severity).Select(d => d.Id), app.Visible.Select(d => d.Id));
    }

    [Fact]
    public void Golden_Disruptions()
    {
        var (_, stage) = Board(Disruptions());
        Golden(stage, "road_disruptions_list");
    }

    [Fact]
    public void Golden_SecondPage()
    {
        var (app, stage) = Board(Disruptions(), "Minimal");
        stage.Step(33, 8000 / 33 + 20);   // rest, then slide
        Assert.Equal(1, app.DisruptionPager!.PageIndex);
        Golden(stage, "road_disruptions_second_page");
    }

    [Fact]
    public void Golden_SingleSevere()
    {
        var (_, stage) = Board([Disruptions()[0]]);
        Golden(stage, "road_disruptions_single");
    }

    [Fact]
    public void Golden_AllClear()
    {
        var (_, stage) = Board([]);
        Golden(stage, "road_disruptions_all_clear");
    }

    [Fact]
    public void Golden_AllClear_WhenOnlyMinorOnesRemain()
    {
        var (app, stage) = Board([Disruptions()[5]], "Serious");
        Assert.Empty(app.Visible);
        Golden(stage, "road_disruptions_all_clear_filtered");
    }

    [Fact]
    public void Golden_Loading()
    {
        var (_, stage) = Board(null);
        Golden(stage, "road_disruptions_loading");
    }

    [Fact]
    public void Golden_Error()
    {
        var (_, stage) = Board(null, error: new HttpRequestException("offline"));
        Golden(stage, "road_disruptions_error");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Board(Disruptions(), "Minimal");
        for (int i = 0; i < 100; i++) { stage.Step(33); stage.Render(); }

        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 6; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"road disruptions: {ms:F3} ms/frame");
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
