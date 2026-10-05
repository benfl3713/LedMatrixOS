using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;
using Xunit;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class TubeRouteOptionsTests
{
    private static (SettingOptionsRegistry Registry, TflStubHandler Handler) Registry(TflArrival[]? arrivals = null)
    {
        var handler = new TflStubHandler { Arrivals = JsonSerializer.Serialize(arrivals ?? BakerStreet()) };
        var registry = new SettingOptionsRegistry();
        BuiltInSettingOptions.Register(registry, new HttpClient(handler), new ConfigurationBuilder().Build());
        return (registry, handler);
    }

    private static Dictionary<string, string> Station(string id = "940GZZLUBST") => new() { ["stationId"] = id };

    [Fact]
    public async Task Routes_AreTheDistinctLineAndDirectionPairsOfTheStation()
    {
        var (registry, _) = Registry();

        var options = await registry.SearchAsync("tube-departures", "routes", "", Station(), default);

        Assert.Equal(
            new[]
            {
                "bakerloo|Southbound", "circle|Westbound", "hammersmith-city|Eastbound", "jubilee|Northbound", "jubilee|Southbound",
                "metropolitan|Eastbound", "metropolitan|Westbound",
            },
            options.Select(o => o.Value).ToArray());
        var met = options.Single(o => o.Value == "metropolitan|Eastbound");
        Assert.Equal("Metropolitan Eastbound", met.Label);
        Assert.Equal("Metropolitan line, platforms 5, 6", met.Subtitle);
        Assert.Equal("Hammersmith & City Eastbound", options.Single(o => o.Value == "hammersmith-city|Eastbound").Label);
        Assert.Equal("Jubilee line, platform 1", options.Single(o => o.Value == "jubilee|Southbound").Subtitle);
    }

    [Fact]
    public async Task Routes_FallBackToTheDestination_WhenThereIsNoDirection()
    {
        var (registry, _) = Registry([
            Arrival("1", "metropolitan", "Aldgate", 100, "Platform 1", "Metropolitan"),
            Arrival("2", "metropolitan", "Amersham", 200, "Platform 2", "Metropolitan"),
            Arrival("3", "metropolitan", "Aldgate", 300, "Platform 1", "Metropolitan"),
        ]);

        var options = await registry.SearchAsync("commute", "routes", null, Station(), default);

        Assert.Equal(new[] { "metropolitan|towards:Aldgate", "metropolitan|towards:Amersham" }, options.Select(o => o.Value).ToArray());
        Assert.Equal("Metropolitan towards Aldgate", options[0].Label);
    }

    [Fact]
    public async Task Routes_NeedAStation_AndTheQueryNarrowsTheList()
    {
        var (registry, handler) = Registry();

        Assert.Empty(await registry.SearchAsync("tube-departures", "routes", "", null, default));
        Assert.Empty(await registry.SearchAsync("tube-departures", "routes", "", Station(""), default));
        Assert.DoesNotContain(handler.Requests, r => r.Contains("/Arrivals"));

        var narrowed = await registry.SearchAsync("tube-departures", "routes", "jubilee", Station(), default);
        Assert.Equal(new[] { "jubilee|Northbound", "jubilee|Southbound" }, narrowed.Select(o => o.Value).ToArray());
    }

    [Fact]
    public async Task Routes_ReuseTheArrivalsBriefly_PerStation()
    {
        var (registry, handler) = Registry();

        await registry.SearchAsync("tube-departures", "routes", "", Station(), default);
        await registry.SearchAsync("tube-departures", "routes", "met", Station(), default);
        Assert.Single(handler.Requests, r => r.Contains("/Arrivals"));

        await registry.SearchAsync("tube-departures", "routes", "", Station("940GZZLUOXC"), default);
        Assert.Equal(2, handler.Requests.Count(r => r.Contains("/Arrivals")));
    }

    [Fact]
    public async Task Routes_LabelOfAStoredValue_NeedsNoLookup()
    {
        var (registry, handler) = Registry();

        Assert.Equal("Metropolitan Eastbound", await registry.LabelAsync("tube-departures", "routes", "metropolitan|Eastbound", default));
        Assert.Equal("Waterloo & City towards Bank", await registry.LabelAsync("tube-departures", "routes", "waterloo-city|towards:Bank", default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void RoutesSetting_IsMultiSearchBrowse_OnBothApps()
    {
        var tube = new TubeDeparturesApp(new HttpClient()).GetSettings().Single(s => s.Key == "routes");
        Assert.Equal(AppSettingType.MultiSearch, tube.Type);
        Assert.True(tube.Browse);
        Assert.Contains("Advanced", new TubeDeparturesApp(new HttpClient()).GetSettings().Single(s => s.Key == "platformFilter").Description);
    }

    // ---- the context contract ---------------------------------------------------------------------------------------

    private sealed class ContextProvider(bool browse) : ISettingOptionsProvider
    {
        public IReadOnlyDictionary<string, string>? Seen;
        public int Calls;
        public bool Browse => browse;

        public Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, IReadOnlyDictionary<string, string> context, CancellationToken ct)
        {
            Seen = context;
            Calls++;
            return Task.FromResult<IReadOnlyList<SettingOption>>([new SettingOption(context.GetValueOrDefault("x", "none"), query)]);
        }
    }

    [Fact]
    public async Task Registry_PassesTheContextToTheProvider_AndAllowsEmptyQueriesOnlyForBrowse()
    {
        var registry = new SettingOptionsRegistry();
        var browse = new ContextProvider(true);
        var search = new ContextProvider(false);
        registry.Register("a", "browse", browse);
        registry.Register("a", "search", search);

        var ctx = new Dictionary<string, string> { ["x"] = "1" };
        var found = await registry.SearchAsync("a", "browse", "", ctx, default);
        Assert.Equal("1", Assert.Single(found).Value);
        Assert.Equal("1", browse.Seen!["x"]);

        Assert.Empty(await registry.SearchAsync("a", "search", "", ctx, default));
        Assert.Equal(0, search.Calls);

        // Browse answers depend on the context, so the registry never serves them from its cache
        ctx["x"] = "2";
        Assert.Equal("2", Assert.Single(await registry.SearchAsync("a", "browse", "", ctx, default)).Value);
        Assert.Equal(2, browse.Calls);

        // Without a context the provider still gets an (empty) one
        await registry.SearchAsync("a", "browse", "", default);
        Assert.Empty(browse.Seen!);
    }
}
