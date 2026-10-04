using System.Net;
using System.Text;
using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

/// <summary>The Home Assistant entity picker and the "editor" hint on [Setting].</summary>
public sealed class HaEntityOptionsTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastAuth { get; private set; }
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastAuth = request.Headers.Authorization?.ToString();
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(respond(request));
        }
    }

    private const string States = """
        [
          {"entity_id":"sensor.lounge_temp","state":"21.4","attributes":{"friendly_name":"Lounge Temperature","unit_of_measurement":"C"}},
          {"entity_id":"light.kitchen","state":"on","attributes":{"friendly_name":"Kitchen Light"}},
          {"entity_id":"switch.fan","state":"off","attributes":{}}
        ]
        """;

    private static SettingOptionsRegistry Registry(Handler handler, bool configured = true)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configured
            ? new Dictionary<string, string?> { ["HomeAssistant:BaseUrl"] = "http://ha.local:8123", ["HomeAssistant:Token"] = "secret-token" }
            : new Dictionary<string, string?>()).Build();
        var registry = new SettingOptionsRegistry();
        BuiltInSettingOptions.Register(registry, new HttpClient(handler), config);
        return registry;
    }

    private static Handler Ok() => new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(States, Encoding.UTF8, "application/json") });

    [Fact]
    public async Task ListsEntitiesWithoutAQuery_AndNarrowsByIdOrFriendlyName()
    {
        var handler = Ok();
        var registry = Registry(handler);
        Assert.True(registry.IsBrowse("ha-tiles", "entities"));

        var all = await registry.SearchAsync("ha-tiles", "entities", "", CancellationToken.None);
        Assert.Equal(["light.kitchen", "sensor.lounge_temp", "switch.fan"], all.Select(o => o.Value));
        Assert.Equal("Kitchen Light", all[0].Label);
        Assert.Equal("switch.fan", all[2].Label); // no friendly name: the id

        Assert.Equal(["sensor.lounge_temp"], (await registry.SearchAsync("ha-tiles", "entities", "lounge", CancellationToken.None)).Select(o => o.Value));
        Assert.Equal(["switch.fan"], (await registry.SearchAsync("ha-tiles", "entities", "FAN", CancellationToken.None)).Select(o => o.Value));
        Assert.Equal("http://ha.local:8123/api/states", handler.LastUrl);
        Assert.Equal("Bearer secret-token", handler.LastAuth);
        Assert.Equal(1, handler.Calls); // cached between queries
    }

    [Fact]
    public async Task NotConfigured_GivesOneExplanatoryRowWithoutCallingHomeAssistant()
    {
        var handler = Ok();
        var options = await Registry(handler, configured: false).SearchAsync("ha-tiles", "entities", "", CancellationToken.None);

        var row = Assert.Single(options);
        Assert.Equal("", row.Value);
        Assert.Contains("not configured", row.Label);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Unreachable_GivesOneExplanatoryRow_AndNeverExposesTheToken()
    {
        var registry = Registry(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var options = await registry.SearchAsync("ha-tiles", "entities", "", CancellationToken.None);

        var row = Assert.Single(options);
        Assert.Equal("", row.Value);
        Assert.DoesNotContain("secret-token", JsonSerializer.Serialize(options));
    }

    [Fact]
    public async Task ResultsAreCapped()
    {
        var many = "[" + string.Join(",", Enumerable.Range(0, 120).Select(i => "{\"entity_id\":\"sensor.s" + i.ToString("000") + "\",\"state\":\"1\",\"attributes\":{}}")) + "]";
        var registry = Registry(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(many) }));
        Assert.Equal(50, (await registry.SearchAsync("ha-tiles", "entities", "", CancellationToken.None)).Count);
    }

    [Fact]
    public void HomeChipStop_IsASearchSettingBackedByTheStopLookup()
    {
        var registry = Registry(Ok());
        Assert.True(registry.Has("home", "chipStopId"));
        var setting = new HomePageApp().GetSettings().First(s => s.Key == "chipStopId");
        Assert.Equal(AppSettingType.Search, setting.Type);
    }

    // ---- editor hint ------------------------------------------------------------------------------------------------------------

    private sealed class EditorApp : SettingsAppBase
    {
        public override string Id => "editor";
        public override string Name => "Editor";

        [Setting("Rows", Editor = "bins")]
        public string Rows { get; set; } = "";

        [Setting("Plain")]
        public string Plain { get; set; } = "";

        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private static List<JsonElement> Json(IConfigurableApp app) => JsonSerializer.SerializeToElement(
        new { settings = app.GetSettings().ToList() }, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("settings").EnumerateArray().ToList();

    [Fact]
    public void EditorHint_IsInSettingsJson_AndNullByDefault()
    {
        var settings = Json(new EditorApp());
        var rows = settings.First(s => s.GetProperty("key").GetString() == "rows");
        var plain = settings.First(s => s.GetProperty("key").GetString() == "plain");
        Assert.Equal("bins", rows.GetProperty("editor").GetString());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("editor").ValueKind);
        Assert.Equal((int)AppSettingType.String, rows.GetProperty("type").GetInt32());
    }

    [Fact]
    public void BuiltInApps_DeclareTheirStructuredEditors()
    {
        string? Editor(IConfigurableApp app, string key) => app.GetSettings().First(s => s.Key == key).Editor;
        Assert.Equal("ha_entities", Editor(new HomeAssistantTilesApp(new HttpClient()), "entities"));
        Assert.Equal("bins", Editor(new BinDayApp(), "bins"));
        Assert.Equal("reminders", Editor(new BinDayApp(), "reminders"));
        Assert.Null(Editor(new BinDayApp(), "calendarKeyword"));
    }
}
