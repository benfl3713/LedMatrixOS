using System.Net;
using System.Text;
using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.PlaneSpotter;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

/// <summary>The stateless Search/MultiSearch pickers: binder types and values, the registry, the built-in providers and old-key compatibility.</summary>
public sealed class SettingOptionsTests : IDisposable
{
    // ---- binder -----------------------------------------------------------------------------------------------------------------

    [LegacySettingKey("pickerSearch")]
    [LegacySettingKey("pickerSelect", "pick")]
    [LegacySettingKey("listSelect", "picks")]
    private sealed class PickerApp : SettingsAppBase
    {
        public override string Id => "picker";
        public override string Name => "Picker";

        [Setting("Pick", Search = true)]
        public string Pick { get; set; } = "";

        [Setting("Picks", MultiSearch = true, Max = 3)]
        public string Picks { get; set; } = "";

        [Setting("Plain")]
        public string Plain { get; set; } = "";

        public List<string> Changed { get; } = [];
        protected override void OnSettingChanged(string key) => Changed.Add(key);
        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Binder_ExposesSearchAndMultiSearchTypes_AppendedToTheEnum()
    {
        var settings = new PickerApp().GetSettings().ToDictionary(s => s.Key);
        Assert.Equal(AppSettingType.Search, settings["pick"].Type);
        Assert.Equal(AppSettingType.MultiSearch, settings["picks"].Type);
        Assert.Equal(AppSettingType.String, settings["plain"].Type);
        Assert.Equal(3, settings["picks"].MaxValue);

        // Existing integer values must not shift
        Assert.Equal(4, (int)AppSettingType.Select);
        Assert.Equal(5, (int)AppSettingType.Search);
        Assert.Equal(6, (int)AppSettingType.MultiSearch);
    }

    [Fact]
    public void Search_StoresACleanId()
    {
        var app = new PickerApp();
        app.UpdateSetting("pick", "  940GZZLUBST  ");
        Assert.Equal("940GZZLUBST", app.Pick);
        app.UpdateSetting("pick", Json("\"Paris|48.85,2.35\""));
        Assert.Equal("Paris|48.85,2.35", app.Pick);
    }

    [Fact]
    public void MultiSearch_NormalisesCommaStringsAndJsonLists_AndCapsToMax()
    {
        var app = new PickerApp();
        app.UpdateSetting("picks", " a, b ,A,, c ");
        Assert.Equal("a,b,c", app.Picks);

        app.UpdateSetting("picks", Json("""["x","y"]"""));
        Assert.Equal("x,y", app.Picks);

        app.UpdateSetting("picks", """["p","q","r","s"]""");
        Assert.Equal("p,q,r", app.Picks);

        app.UpdateSetting("picks", "");
        Assert.Equal("", app.Picks);
    }

    [Fact]
    public void LegacyKeys_AreAcceptedAndMapped()
    {
        var app = new PickerApp();
        app.UpdateSetting("pickerSearch", "ba");   // ignored
        app.UpdateSetting("pickerSelect", "");
        app.UpdateSetting("pickerSelect", "not a selection");
        Assert.Equal("", app.Pick);

        app.UpdateSetting("pickerSelect", "940GZZLUBST | Baker Street");
        Assert.Equal("940GZZLUBST", app.Pick);
        Assert.Contains("pick", app.Changed);

        foreach (var id in new[] { "1", "2", "3", "2", "4" }) app.UpdateSetting("listSelect", $"{id} | Somewhere");
        Assert.Equal("2,3,4", app.Picks);   // present ids are not added twice, the oldest makes room
        Assert.True(SettingsBinder.IsLegacyKey(app, "pickerSearch"));
        Assert.False(SettingsBinder.IsLegacyKey(app, "nope"));
    }

    // ---- registry ---------------------------------------------------------------------------------------------------------------

    private sealed class FakeProvider(Func<string, IReadOnlyList<SettingOption>>? search = null, Func<string, string?>? label = null) : ISettingOptionsProvider
    {
        public int SearchCalls, LabelCalls;

        public Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct)
        {
            Interlocked.Increment(ref SearchCalls);
            return Task.FromResult(search?.Invoke(query) ?? []);
        }

        public Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct)
        {
            Interlocked.Increment(ref LabelCalls);
            return Task.FromResult(label?.Invoke(value));
        }
    }

    [Fact]
    public async Task Registry_ShortQueriesAndUnknownKeysGiveEmptyListsWithoutCallingTheProvider()
    {
        var provider = new FakeProvider(q => [new SettingOption("1", q)]);
        var registry = new SettingOptionsRegistry();
        registry.Register("picker", "pick", provider);

        Assert.Empty(await registry.SearchAsync("picker", "pick", "a", default));
        Assert.Empty(await registry.SearchAsync("picker", "pick", " a ", default));
        Assert.Empty(await registry.SearchAsync("picker", "pick", null, default));
        Assert.Empty(await registry.SearchAsync("picker", "other", "abc", default));
        Assert.Equal(0, provider.SearchCalls);

        var found = await registry.SearchAsync("PICKER", "Pick", " abc ", default);   // app id and key are case-insensitive
        Assert.Equal("abc", Assert.Single(found).Label);
        Assert.True(registry.Has("picker", "pick"));
        Assert.False(registry.Has("picker", "other"));
    }

    [Fact]
    public async Task Registry_CachesOptionsBriefly_AndSwallowsProviderFailures()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var provider = new FakeProvider(q => [new SettingOption("1", q)]);
        var registry = new SettingOptionsRegistry(() => now);
        registry.Register("picker", "pick", provider);

        await registry.SearchAsync("picker", "pick", "bak", default);
        await registry.SearchAsync("picker", "pick", "BAK", default);
        Assert.Equal(1, provider.SearchCalls);
        now += TimeSpan.FromMinutes(3);
        await registry.SearchAsync("picker", "pick", "bak", default);
        Assert.Equal(2, provider.SearchCalls);

        registry.Register("picker", "boom", new ThrowingProvider());
        Assert.Empty(await registry.SearchAsync("picker", "boom", "bak", default));
        Assert.Null(await registry.LabelAsync("picker", "boom", "x", default));
    }

    private sealed class ThrowingProvider : ISettingOptionsProvider
    {
        public Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct) => throw new HttpRequestException("down");
        public Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct) => throw new HttpRequestException("down");
    }

    [Fact]
    public async Task Registry_ResolvesCurrentLabels_CachedAndNeverThrowing()
    {
        var provider = new FakeProvider(label: v => v == "bad" ? null : v.ToUpperInvariant() + "!");
        var registry = new SettingOptionsRegistry();
        registry.Register("picker", "pick", provider);
        registry.Register("picker", "picks", provider);
        registry.Register("picker", "boom", new ThrowingProvider());

        var app = new PickerApp { Pick = "abc", Picks = "x,bad,y" };
        var settings = app.GetSettings().ToList();
        settings.Add(new AppSetting("boom", "Boom", "", AppSettingType.Search, "zzz", "zzz"));

        var labelled = (await registry.WithLabelsAsync("picker", settings, default)).ToDictionary(s => s.Key);
        Assert.Equal("ABC!", labelled["pick"].CurrentLabel);
        Assert.Equal(["X!", "bad", "Y!"], labelled["picks"].CurrentLabels);   // an unresolved id shows as itself
        Assert.Equal("zzz", labelled["boom"].CurrentLabel);
        Assert.Null(labelled["plain"].CurrentLabel);
        Assert.Null(labelled["plain"].CurrentLabels);

        var calls = provider.LabelCalls;
        await registry.WithLabelsAsync("picker", settings, default);
        Assert.Equal(calls + 1, provider.LabelCalls);   // only the unresolved id is asked again

        var empty = (await registry.WithLabelsAsync("picker", new PickerApp().GetSettings().ToList(), default)).ToDictionary(s => s.Key);
        Assert.Equal("", empty["pick"].CurrentLabel);
        Assert.Empty(empty["picks"].CurrentLabels!);
    }

    [Fact]
    public async Task SettingsJson_ShapeForSearchSettings()
    {
        var registry = new SettingOptionsRegistry();
        registry.Register("picker", "pick", new FakeProvider(label: _ => "Baker Street"));
        var app = new PickerApp { Pick = "940GZZLUBST" };

        var settings = await registry.WithLabelsAsync("picker", app.GetSettings().ToList(), default);
        var json = JsonSerializer.SerializeToElement(new { appId = "picker", settings }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var pick = json.GetProperty("settings").EnumerateArray().First(s => s.GetProperty("key").GetString() == "pick");

        Assert.Equal(5, pick.GetProperty("type").GetInt32());
        Assert.Equal("940GZZLUBST", pick.GetProperty("currentValue").GetString());
        Assert.Equal("Baker Street", pick.GetProperty("currentLabel").GetString());
    }

    // ---- built-in providers -----------------------------------------------------------------------------------------------------

    private sealed class RouteHandler(Func<string, string> body) : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Enqueue(url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body(url), Encoding.UTF8, "application/json") });
        }
    }

    private const string GeocodeJson = """
        {"results":[
          {"name":"London","latitude":51.5085,"longitude":-0.1257,"admin1":"England","country":"United Kingdom"},
          {"name":"London","latitude":42.9834,"longitude":-81.233,"admin1":"Ontario","country":"Canada"}]}
        """;

    private static (SettingOptionsRegistry Registry, RouteHandler Handler) BuiltIn(string? tflKey = null)
    {
        var handler = new RouteHandler(url => url.Contains("geocoding-api") ? GeocodeJson : new TflStubHandlerBody().For(url));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["TFL:AppKey"] = tflKey }).Build();
        var registry = new SettingOptionsRegistry();
        BuiltInSettingOptions.Register(registry, new HttpClient(handler), config);
        return (registry, handler);
    }

    // Canned TfL answers for the three kinds of place
    private sealed class TflStubHandlerBody
    {
        public string For(string url) =>
            url.Contains("/BikePoint/Search") ? """[{"id":"BikePoints_42","commonName":"Hyde Park Corner , Hyde Park"},{"id":"BikePoints_7","commonName":"Park Lane, Mayfair"}]"""
            : url.Contains("/BikePoint/") ? """{"id":"BikePoints_42","commonName":"Hyde Park Corner , Hyde Park","additionalProperties":[]}"""
            : url.Contains("modes=bus") ? """{"matches":[{"id":"490000001A","name":"Victoria Station","modes":["bus"]}]}"""
            : url.Contains("/Search/") ? """{"matches":[{"id":"940GZZLUBST","name":"Baker Street Underground Station","modes":["tube","circle"]},{"id":"bus1","name":"Baker Street Bus","modes":["bus"]}]}"""
            : url.Contains("490000001A") ? """{"commonName":"Victoria Station","indicator":"Stop B"}"""
            : """{"commonName":"Baker Street Underground Station"}""";
    }

    [Fact]
    public async Task BuiltIn_StationOptionsAndLabel_NeedNoApp()
    {
        var (registry, handler) = BuiltIn("k");

        var options = await registry.SearchAsync("tube-departures", "stationId", "baker", default);
        var station = Assert.Single(options);
        Assert.Equal("940GZZLUBST", station.Value);
        Assert.Equal("Baker Street", station.Label);
        Assert.Equal("tube, circle", station.Subtitle);
        Assert.Contains(handler.Requests, r => r.Contains("/StopPoint/Search/baker") && r.Contains("app_key=k"));

        Assert.Equal("Baker Street", await registry.LabelAsync("commute", "stationId", "940GZZLUBST", default));
        Assert.True(registry.Has("morning-briefing", "stationId"));
    }

    [Fact]
    public async Task BuiltIn_BusStopsAndDocks()
    {
        var (registry, _) = BuiltIn();

        var stop = Assert.Single(await registry.SearchAsync("bus-arrivals", "stopIds", "victoria", default));
        Assert.Equal(("490000001A", "Victoria Station"), (stop.Value, stop.Label));
        Assert.Equal("Victoria (Stop B)", await registry.LabelAsync("bus-arrivals", "stopIds", "490000001A", default));

        var docks = await registry.SearchAsync("cycle-hub", "dockIds", "park", default);
        Assert.Equal(["BikePoints_42", "BikePoints_7"], docks.Select(d => d.Value).ToArray());
        Assert.Equal("Hyde Park Corner, Hyde Park", await registry.LabelAsync("cycle-hub", "dockIds", "BikePoints_42", default));
    }

    [Fact]
    public async Task BuiltIn_PlacesAreStoredWithTheirCoordinates()
    {
        var (registry, _) = BuiltIn();

        var places = await registry.SearchAsync("weather", "location", "london", default);
        Assert.Equal(["London|51.5085,-0.1257", "London|42.9834,-81.233"], places.Select(p => p.Value).ToArray());
        Assert.Equal(["London, England, United Kingdom", "London, Ontario, Canada"], places.Select(p => p.Label).ToArray());

        Assert.Equal("London", await registry.LabelAsync("weather", "location", "London|51.5085,-0.1257", default));
        Assert.Equal("London, England, United Kingdom", await registry.LabelAsync("weather", "location", "London", default));   // an older plain name is looked up
        Assert.Equal("51.5,-0.1", await registry.LabelAsync("plane-spotter", "location", "51.5,-0.1", default));
        Assert.True(registry.Has("commute", "location"));
    }

    [Fact]
    public async Task BuiltIn_JourneyOffersPostcodeStationsAndPlaces()
    {
        var (registry, _) = BuiltIn();

        var options = await registry.SearchAsync("journey", "to", "SW1A 1AA", default);
        Assert.Equal(new SettingOption("SW1A 1AA", "SW1A 1AA", "Postcode"), options[0]);
        Assert.Contains(options, o => o.Value == "940GZZLUBST" && o.Subtitle == "Station");
        Assert.Contains(options, o => o.Value.StartsWith("London|"));

        Assert.Equal("Baker Street", await registry.LabelAsync("journey", "from", "940GZZLUBST", default));
        Assert.Equal("SW1A 1AA", await registry.LabelAsync("journey", "from", "sw1a 1aa", default));
        Assert.Equal("Work", await registry.LabelAsync("journey", "from", "Work|51.5,-0.1", default));
    }

    [Fact]
    public void PlaceEncoding_RoundTrips_AndPlannerGetsCoordinates()
    {
        var value = PlaceGeocoder.Encode("King's Cross", 51.53, -0.1234567);
        Assert.Equal("King's Cross|51.53,-0.1235", value);
        Assert.True(PlaceGeocoder.TryParseEncoded(value, out var name, out var c));
        Assert.Equal(("King's Cross", 51.53, -0.1235), (name, c.Lat, c.Lon));
        Assert.Equal("King's Cross", PlaceGeocoder.DisplayName(value));
        Assert.Equal("London", PlaceGeocoder.DisplayName("London"));
        Assert.False(PlaceGeocoder.TryParseEncoded("A|B", out _, out _));

        Assert.Equal("51.53,-0.1235", JourneyApp.PlannerPoint(value));
        Assert.Equal("SW1A 1AA", JourneyApp.PlannerPoint(" SW1A 1AA "));
        Assert.Equal("940GZZLUBST", JourneyApp.PlannerPoint("940GZZLUBST"));
    }

    // ---- inactive apps, aliases and old persisted keys -------------------------------------------------------------------------

    private sealed class HttpServices(HttpMessageHandler handler) : IServiceProvider
    {
        // A fresh client per app: the apps set its Timeout, which is not allowed once a client has sent a request
        public object? GetService(Type serviceType) => serviceType == typeof(HttpClient) ? new HttpClient(handler) : null;
    }

    private readonly string _file = Path.Combine(Path.GetTempPath(), "app-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    private AppManager Manager(AppSettingsStorage? storage = null)
    {
        var handler = new TflStubHandler();
        var services = new HttpServices(handler);
        var mgr = new AppManager(services, new ConfigurationBuilder().Build(), 64, 256, storage ?? new AppSettingsStorage(_file));
        foreach (var app in new[] { typeof(TubeDeparturesApp), typeof(BusArrivalsApp), typeof(CycleHubApp), typeof(CommuteApp), typeof(ClockApp) }) mgr.Register(app);
        foreach (var (alias, target, preset) in BuiltInApps.Aliases()) mgr.RegisterAlias(alias, target, preset);
        return mgr;
    }

    [Fact]
    public async Task OptionsLookup_WorksWithoutConstructingOrActivatingTheApp()
    {
        var mgr = Manager();
        var (registry, _) = BuiltIn();

        Assert.Equal("tube-departures", mgr.ResolveAppId("TUBE-DEPARTURES"));
        Assert.Null(mgr.ResolveAppId("nope"));
        Assert.Equal("clock", mgr.ResolveAppId("flip-clock"));   // an alias resolves to its target

        // Nothing is active, and the lookup does not need the app anyway
        Assert.Null(mgr.ActiveAppId);
        Assert.Single(await registry.SearchAsync(mgr.ResolveAppId("tube-departures")!, "stationId", "baker", default));

        // Unknown key or app: nothing to search (the endpoint answers 404 for these)
        Assert.False(registry.Has(mgr.ResolveAppId("tube-departures")!, "platformFilter"));
        Assert.False(registry.Has("clock", "style"));
    }

    [Fact]
    public async Task OldPersistedKeysStillLoad_AndNewSettingsReadBackAsSearchTypes()
    {
        var storage = new AppSettingsStorage(_file);
        storage.SaveAppSettings("tube-departures", new Dictionary<string, object>
        {
            ["stationSearch"] = "baker", ["stationSelect"] = "", ["stationId"] = "940GZZLUBST", ["platformFilter"] = "East", ["boardStyle"] = "Hero",
        });
        storage.SaveAppSettings("bus-arrivals", new Dictionary<string, object> { ["stopSearch"] = "vic", ["stopSelect"] = "", ["stopIds"] = "490000001A, 490000002B" });
        storage.SaveAppSettings("cycle-hub", new Dictionary<string, object> { ["dockSearch"] = "", ["dockSelect"] = "", ["dockIds"] = "BikePoints_42,BikePoints_7" });
        storage.SaveAppSettings("commute", new Dictionary<string, object> { ["stationSearch"] = "", ["stationSelect"] = "", ["stationId"] = "940GZZLUOXC", ["location"] = "Leeds" });

        var mgr = Manager(new AppSettingsStorage(_file));   // a fresh process reading the old file

        var tube = mgr.GetSettings("tube-departures").Settings.ToDictionary(s => s.Key);
        Assert.Equal("940GZZLUBST", tube["stationId"].CurrentValue);
        Assert.Equal(AppSettingType.Search, tube["stationId"].Type);
        Assert.Equal("East", tube["platformFilter"].CurrentValue);
        Assert.Equal("Hero", tube["boardStyle"].CurrentValue);
        Assert.DoesNotContain("stationSearch", tube.Keys);
        Assert.DoesNotContain("stationSelect", tube.Keys);

        var bus = mgr.GetSettings("bus-arrivals").Settings.ToDictionary(s => s.Key);
        Assert.Equal("490000001A,490000002B", bus["stopIds"].CurrentValue);
        Assert.Equal(AppSettingType.MultiSearch, bus["stopIds"].Type);

        Assert.Equal("BikePoints_42,BikePoints_7", mgr.GetSettings("cycle-hub").Settings.Single(s => s.Key == "dockIds").CurrentValue);

        var commute = mgr.GetSettings("commute").Settings.ToDictionary(s => s.Key);
        Assert.Equal("940GZZLUOXC", commute["stationId"].CurrentValue);
        Assert.Equal("Leeds", commute["location"].CurrentValue);
        Assert.Equal(AppSettingType.Search, commute["location"].Type);

        // Activating restores the same values into the running app
        await mgr.ActivateAsync("tube-departures", CancellationToken.None);
        Assert.Equal("940GZZLUBST", ((Apps.TubeDeparturesApp)mgr.ActiveApp!).StationId);
        await mgr.ActiveApp!.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public void RetiredKeysPostedToAnInactiveApp_AreAcceptedAndMapped()
    {
        var mgr = Manager();

        var result = mgr.UpdateSettings("tube-departures", new Dictionary<string, object>
        {
            ["stationSearch"] = "baker",
            ["stationSelect"] = "940GZZLUOXC | Oxford Circus",
            ["boardStyle"] = "Platform",
        });

        Assert.Equal(SettingsStatus.Ok, result.Status);
        Assert.Empty(result.RejectedKeys);
        Assert.Equal("940GZZLUOXC", mgr.GetSettings("tube-departures").Settings.Single(s => s.Key == "stationId").CurrentValue);

        var unknown = mgr.UpdateSettings("tube-departures", new Dictionary<string, object> { ["bogus"] = "x" });
        Assert.Equal(["bogus"], unknown.RejectedKeys);

        mgr.UpdateSettings("bus-arrivals", new Dictionary<string, object> { ["stopSelect"] = "490000001A | Victoria" });
        mgr.UpdateSettings("bus-arrivals", new Dictionary<string, object> { ["stopIds"] = Json("""["a","b"]""") });
        Assert.Equal("a,b", mgr.GetSettings("bus-arrivals").Settings.Single(s => s.Key == "stopIds").CurrentValue);
    }

    [Fact]
    public async Task PlaneSpotter_LocationSettingWinsOverConfig_AndFallsBackToIt()
    {
        var app = new PlaneSpotterApp(new FakePlaneSource());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaneSpotter:Latitude"] = "10", ["PlaneSpotter:Longitude"] = "20",
        }).Build();
        await app.OnActivatedAsync((64, 256), config, CancellationToken.None);
        Assert.Equal((10d, 20d), app.Home);

        app.UpdateSetting("location", "Paris|48.8534,2.3488");
        Assert.Equal((48.8534, 2.3488), app.Home);
        Assert.True(app.HasLocation);

        app.UpdateSetting("location", "");
        Assert.Equal((10d, 20d), app.Home);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }
}
