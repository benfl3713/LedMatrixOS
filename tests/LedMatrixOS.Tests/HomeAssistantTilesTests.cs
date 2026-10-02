using System.Net;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.HomeAssistant;
using LedMatrixOS.Graphics.Text;
using Xunit;

namespace LedMatrixOS.Tests;

public class HomeAssistantTilesTests
{
    private static HaState S(string id, string state, string? unit = null, string? name = null) => new(id, state, unit, name);

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

    private static (HomeAssistantTilesApp App, AppStage Stage) Rig(string entities, HaState?[]? states, int steps = 20, string baseUrl = "http://ha.local")
    {
        Fonts.Load();
        var app = new HomeAssistantTilesApp(new HttpClient()) { Time = new FakeTime() };
        app.UseData(entities, new MutableLive<HaState?[]> { Value = states }, baseUrl, baseUrl.Length == 0 ? "" : "t");
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
        Assert.Equal(["entities", "pageSeconds"], app.GetSettings().Select(s => s.Key));
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
        Assert.Equal(0, app.Board.PageAt(stage.Time));
        Assert.Equal(1, app.Board.PageAt(TimeSpan.FromSeconds(app.PageSeconds + 0.5)));
        stage.Step(33, (int)(app.PageSeconds * 1000 / 33) + 15);
        Golden(stage, "ha_tiles_page_two");
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
