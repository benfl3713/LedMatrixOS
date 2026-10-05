using System.Net;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.HomeAssistant;
using LedMatrixOS.Graphics.Text;
using Xunit;

namespace LedMatrixOS.Tests;

public class HomeAssistantTilesTests
{
    private static HaState S(string id, string state, string? unit = null, string? name = null, string? deviceClass = null) => new(id, state, unit, name, deviceClass);

    // ---- parsing & formatting ------------------------------------------------------------------------------------

    [Fact]
    public void ParseEntities_HandlesLabelsSeparatorsAndJunk()
    {
        var list = HaFormat.ParseEntities(" sensor.temp|Lounge, light.kitchen ;bogus, switch.fan| \n climate.hall|Hall ");
        Assert.Equal(["sensor.temp", "light.kitchen", "switch.fan", "climate.hall"], list.Select(e => e.EntityId));
        Assert.Equal(["Lounge", null, null, "Hall"], list.Select(e => e.Label));
        Assert.Empty(HaFormat.ParseEntities(null));
    }

    [Fact]
    public void ParseState_ReadsUnitAndFriendlyName()
    {
        var s = HaFormat.ParseState("""{"entity_id":"sensor.t","state":"21.46","attributes":{"unit_of_measurement":"°C","friendly_name":"Lounge Temp"}}""");
        Assert.Equal(new HaState("sensor.t", "21.46", "°C", "Lounge Temp"), s);
        Assert.Null(HaFormat.ParseState("""{"entity_id":"light.a","state":"on","attributes":{}}""").Unit);
    }

    [Theory]
    [InlineData("21.46", "21.5", (int)TileKind.Number)]
    [InlineData("20.0", "20", (int)TileKind.Number)]
    [InlineData("on", "ON", (int)TileKind.On)]
    [InlineData("not_home", "NOT HOME", (int)TileKind.Off)]
    [InlineData("unavailable", "N/A", (int)TileKind.Unavailable)]
    [InlineData("heat_cool", "HEAT COOL", (int)TileKind.Text)]
    public void ToTile_FormatsValues(string state, string expected, int kind)
    {
        var tile = HaFormat.ToTile(new EntityRef("sensor.x", null), S("sensor.x", state, "W"));
        Assert.Equal(expected, tile.Value);
        Assert.Equal((TileKind)kind, tile.Kind);
    }

    [Fact]
    public void ToTile_LabelFallsBackFromLabelToFriendlyNameToId_AndMissingStateIsUnavailable()
    {
        Assert.Equal("MINE", HaFormat.ToTile(new EntityRef("sensor.x", "Mine"), S("sensor.x", "1", name: "Friendly")).Label);
        Assert.Equal("FRIENDLY", HaFormat.ToTile(new EntityRef("sensor.x", null), S("sensor.x", "1", name: "Friendly")).Label);
        var missing = HaFormat.ToTile(new EntityRef("sensor.back_door", null), null);
        Assert.Equal("BACK DOOR", missing.Label);
        Assert.Equal(TileKind.Unavailable, missing.Kind);
    }

    // ---- api -------------------------------------------------------------------------------------------------------

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Api_SendsBearerTokenAndSurvivesOneFailingEntity()
    {
        var handler = new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("light.bad")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"entity_id":"sensor.ok","state":"5","attributes":{}}""") });
        var api = new HaApi(new HttpClient(handler)) { BaseUrl = "http://ha.local:8123/", Token = "secret" };

        var states = await api.GetStatesAsync([new("sensor.ok", null), new("light.bad", null)], default);

        Assert.Equal("5", states[0]!.State);
        Assert.Null(states[1]);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer secret", r.Headers.Authorization!.ToString()));
        Assert.Equal("http://ha.local:8123/api/states/sensor.ok", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Api_NetworkErrorBecomesNull()
    {
        var api = new HaApi(new HttpClient(new Handler(_ => throw new HttpRequestException("down")))) { BaseUrl = "http://x", Token = "t" };
        Assert.Null((await api.GetStatesAsync([new("sensor.a", null)], default))[0]);
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    private static (HomeAssistantTilesApp App, AppStage Stage) Rig(string entities, HaState?[]? states, int steps = 20, string baseUrl = "http://ha.local", float[]?[]? history = null)
    {
        Fonts.Load();
        var app = new HomeAssistantTilesApp(new HttpClient()) { Time = new FakeTime() };
        app.UseData(entities, new MutableLive<HaState?[]> { Value = states }, baseUrl, baseUrl.Length == 0 ? "" : "t", history is null ? null : new MutableLive<float[]?[]> { Value = history });
        var stage = new AppStage(app);
        stage.Step(33, steps);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        TubeFixtures.Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    private static readonly string Four = "sensor.lounge_temp|Lounge, light.kitchen|Kitchen, sensor.power|Power, binary_sensor.door|Front Door";
    private static HaState?[] FourStates() =>
    [
        S("sensor.lounge_temp", "21.46", "°C"), S("light.kitchen", "on"), S("sensor.power", "1834", "W"), null,
    ];

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new HomeAssistantTilesApp(new HttpClient());
        Assert.Equal("ha-tiles", app.Id);
        Assert.Equal(["entities", "pageSeconds", "tilesPerPage"], app.GetSettings().Select(s => s.Key));
    }

    [Fact]
    public void Golden_FourTilesMixedKinds()
    {
        var (_, stage) = Rig(Four, FourStates());
        Golden(stage, "ha_tiles_four");
    }

    [Fact]
    public void Golden_SingleTileUsesTheWholeWidth()
    {
        var (_, stage) = Rig("sensor.lounge_temp|Lounge", [S("sensor.lounge_temp", "21.5", "°C")]);
        Golden(stage, "ha_tiles_single");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Golden_TilesPerPage_WiderTilesGetBiggerValues(int perPage)
    {
        var (app, stage) = Rig(Four, FourStates(), steps: 1);
        app.TilesPerPage = perPage;
        stage.Step(33, 20);
        Assert.Equal(perPage == 2 ? 2 : 2, app.Board!.PageCount);
        Golden(stage, $"ha_tiles_per_page_{perPage}");
    }

    [Fact]
    public void Golden_NotConfigured()
    {
        var (_, stage) = Rig("sensor.a", null, baseUrl: "");
        Golden(stage, "ha_tiles_setup");
    }

    [Fact]
    public void MoreThanFourEntities_PageThroughTheList()
    {
        var ids = string.Join(",", Enumerable.Range(1, 6).Select(i => $"sensor.s{i}|S{i}"));
        var (app, stage) = Rig(ids, Enumerable.Range(1, 6).Select(i => (HaState?)S($"sensor.s{i}", i.ToString())).ToArray(), steps: 3);

        Assert.Equal(2, app.Board!.PageCount);
        Assert.Equal(0, app.Board.Pager.PageIndex);
        stage.Step(33, (int)(app.PageSeconds * 1000 / 33) + 40);   // past the interval and the slide
        Assert.Equal(1, app.Board.Pager.PageIndex);
        Assert.False(app.Board.Pager.IsTransitioning);
        Golden(stage, "ha_tiles_page_two");
    }

    // ---- icons, sparklines, entity flags -----------------------------------------------------------------------------

    private static float[] Wave(double phase, double amp, double mid, int n = 48) =>
        Enumerable.Range(0, n).Select(i => (float)(mid + amp * Math.Sin(phase + i * 0.22) + i * 0.02)).ToArray();

    private const string Icons = "light.lamp|Lamp|icon, binary_sensor.front_door|Door|icon, binary_sensor.hall_motion|Hall|icon, lock.front|Lock|icon";

    private static HaState?[] IconStates() =>
        [S("light.lamp", "on"), S("binary_sensor.front_door", "on", null, null, "door"), S("binary_sensor.hall_motion", "on", null, null, "motion"), S("lock.front", "locked")];

    [Fact]
    public void Golden_IconTiles()
    {
        var (_, stage) = Rig(Icons, IconStates());
        Golden(stage, "ha_tiles_icons");
    }

    [Fact]
    public void Golden_IconTilesInTheOtherState()
    {
        var (_, stage) = Rig(Icons, [S("light.lamp", "off"), S("binary_sensor.front_door", "off", null, null, "door"), S("binary_sensor.hall_motion", "off", null, null, "motion"), S("lock.front", "unlocked")]);
        Golden(stage, "ha_tiles_icons_alt");
    }

    [Fact]
    public void Golden_SparklineTiles()
    {
        var (_, stage) = Rig("sensor.lounge_temp|Lounge|spark, sensor.power|Power|spark, sensor.outside|Outside|spark, sensor.humidity|Humidity",
            [S("sensor.lounge_temp", "21.5", "°C"), S("sensor.power", "1834", "W"), S("sensor.outside", "-3.2", "°C"), S("sensor.humidity", "48", "%")],
            history: [Wave(0, 1.5, 20), Wave(1, 600, 1500), Wave(2, 2, 2), null]);
        Golden(stage, "ha_tiles_sparkline");
    }

    [Fact]
    public void Golden_MixedIconSparklineAndPlain()
    {
        var (_, stage) = Rig("sensor.lounge_temp|Lounge|spark, light.lamp|Lamp|icon, sensor.power|Power, switch.fan|Fan|icon",
            [S("sensor.lounge_temp", "21.5", "°C"), S("light.lamp", "off"), S("sensor.power", "1834", "W"), S("switch.fan", "on")],
            history: [Wave(0, 1.5, 20), null, null, null]);
        Golden(stage, "ha_tiles_mixed");
    }

    [Fact]
    public void SteadyState_WithIconAndSparklineTiles_DoesNotAllocate()
    {
        var (_, stage) = Rig("sensor.lounge_temp|Lounge|spark, light.lamp|Lamp|icon, sensor.power|Power|spark, lock.front|Lock|icon",
            [S("sensor.lounge_temp", "21.5", "°C"), S("light.lamp", "on"), S("sensor.power", "1834", "W"), S("lock.front", "locked")],
            history: [Wave(0, 1.5, 20), null, Wave(1, 600, 1500), null]);
        for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }

        long least = long.MaxValue;
        for (int window = 0; window < 8; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++) { stage.Step(33); stage.Render(); }
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(least < 256, $"least allocation: {least} bytes");
    }

    [Fact]
    public void ParseEntities_FlagsAreBackwardsCompatibleAndUnknownOnesIgnored()
    {
        var list = HaFormat.ParseEntities("sensor.t|Living|spark, light.lamp|Lamp|icon, switch.x||ICON, sensor.y|Y|spark+bogus, sensor.z|Z|bogus, light.plain|Plain");
        Assert.Equal([TileFlags.Spark, TileFlags.Icon, TileFlags.Icon, TileFlags.Spark, TileFlags.None, TileFlags.None], list.Select(e => e.Flags));
        Assert.Equal(["Living", "Lamp", null, "Y", "Z", "Plain"], list.Select(e => e.Label));
    }

    [Fact]
    public void ToTile_IconFlagGivesGlyphsOnlyWhenRequestedAndSupported()
    {
        var icon = TileFlags.Icon;
        Assert.Equal(Glyph.Bulb, HaFormat.ToTile(new("light.a", null, icon), S("light.a", "on")).Glyph);
        Assert.Equal(Glyph.None, HaFormat.ToTile(new("light.a", null), S("light.a", "on")).Glyph);
        Assert.Equal(Glyph.Plug, HaFormat.ToTile(new("switch.a", null, icon), S("switch.a", "off")).Glyph);
        Assert.Equal(Glyph.DoorOpen, HaFormat.ToTile(new("binary_sensor.a", null, icon), S("binary_sensor.a", "on", null, null, "door")).Glyph);
        Assert.Equal(Glyph.DoorClosed, HaFormat.ToTile(new("binary_sensor.a", null, icon), S("binary_sensor.a", "off", null, null, "window")).Glyph);
        Assert.Equal(Glyph.Motion, HaFormat.ToTile(new("binary_sensor.hall_motion", null, icon), S("binary_sensor.hall_motion", "off")).Glyph);
        Assert.Equal(Glyph.LockClosed, HaFormat.ToTile(new("lock.a", null, icon), S("lock.a", "locked")).Glyph);
        Assert.Equal(Glyph.LockOpen, HaFormat.ToTile(new("lock.a", null, icon), S("lock.a", "unlocked")).Glyph);
        Assert.Equal(Glyph.None, HaFormat.ToTile(new("binary_sensor.battery", null, icon), S("binary_sensor.battery", "on")).Glyph);
        Assert.Equal("ON", HaFormat.ToTile(new("binary_sensor.battery", null, icon), S("binary_sensor.battery", "on")).Value);
        Assert.Equal(TileKind.Warm, HaFormat.ToTile(new("light.a", null, icon), S("light.a", "on")).Kind);
        Assert.Equal(TileKind.Unavailable, HaFormat.ToTile(new("light.a", null, icon), S("light.a", "unavailable")).Kind);
    }

    [Fact]
    public void ToTile_SeriesOnlyKeptForSparkNumbers_AndTileEqualityComparesSeriesContent()
    {
        float[] series = [1, 2, 3];
        Assert.Same(series, HaFormat.ToTile(new("sensor.a", null, TileFlags.Spark), S("sensor.a", "3"), series).Series);
        Assert.Null(HaFormat.ToTile(new("sensor.a", null), S("sensor.a", "3"), series).Series);
        Assert.Null(HaFormat.ToTile(new("sensor.a", null, TileFlags.Spark), S("sensor.a", "3"), [1f]).Series);
        Assert.Equal(HaFormat.ToTile(new("sensor.a", null, TileFlags.Spark), S("sensor.a", "3"), series),
                     HaFormat.ToTile(new("sensor.a", null, TileFlags.Spark), S("sensor.a", "3"), [1f, 2f, 3f]));
    }

    [Fact]
    public void ParseHistory_BucketsByTimeCarriesForwardAndSkipsNonNumeric()
    {
        var end = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        // Window 2026-01-01 00:00 .. 2026-01-02 00:00, 4 buckets of 6h.
        const string json = """
            [[{"entity_id":"sensor.t","state":"10","last_changed":"2025-12-31T23:00:00+00:00"},
              {"state":"unavailable","last_changed":"2026-01-01T01:00:00+00:00"},
              {"state":"20","last_changed":"2026-01-01T07:00:00+00:00"},
              {"state":"25","last_changed":"2026-01-01T08:00:00+00:00"},
              {"state":"40","last_changed":"2026-01-01T19:00:00+00:00"}]]
            """;
        var series = HaFormat.ParseHistory(json, end, TimeSpan.FromHours(24), buckets: 4);
        Assert.Equal([10f, 25f, 25f, 40f], series);
    }

    [Fact]
    public void ParseHistory_NoNumericDataIsNull()
    {
        var end = DateTimeOffset.UnixEpoch.AddDays(10);
        Assert.Null(HaFormat.ParseHistory("[]", end, TimeSpan.FromHours(24)));
        Assert.Null(HaFormat.ParseHistory("""[[{"entity_id":"light.a","state":"on","last_changed":"1970-01-10T05:00:00+00:00"}]]""", end, TimeSpan.FromHours(24)));
    }

    [Fact]
    public async Task Api_HistoryRequestsOnlySparkEntitiesWithMinimalResponse()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""[[{"entity_id":"sensor.a","state":"7","last_changed":"2026-01-01T12:00:00+00:00"}]]""") });
        var now = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var api = new HaApi(new HttpClient(handler)) { BaseUrl = "http://ha.local:8123/", Token = "secret", Now = () => now };

        var result = await api.GetHistoryAsync([new("sensor.a", null, TileFlags.Spark), new("sensor.b", null)], default);

        Assert.Single(handler.Requests);
        Assert.Null(result[1]);
        Assert.Equal(48, result[0]!.Length);
        Assert.Equal(7f, result[0]![^1]);
        var uri = handler.Requests[0].RequestUri!.ToString();
        Assert.StartsWith("http://ha.local:8123/api/history/period/2026-01-01T00%3A00%3A00Z?", uri);
        Assert.Contains("filter_entity_id=sensor.a", uri);
        Assert.Contains("minimal_response", uri);
        Assert.Contains("no_attributes", uri);
        Assert.Equal("Bearer secret", handler.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Api_HistoryFailureBecomesNull()
    {
        var api = new HaApi(new HttpClient(new Handler(_ => throw new HttpRequestException("down")))) { BaseUrl = "http://x", Token = "t" };
        Assert.Null((await api.GetHistoryAsync([new("sensor.a", null, TileFlags.Spark)], default))[0]);
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Rig(Four, FourStates());
        for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }

        long least = long.MaxValue;
        for (int window = 0; window < 8; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++) { stage.Step(33); stage.Render(); }   // 1s windows stay on one page (single page here)
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(least < 256, $"least allocation: {least} bytes");
    }
}
