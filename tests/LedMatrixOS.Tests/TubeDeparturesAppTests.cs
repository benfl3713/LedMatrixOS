using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

public class TubeDeparturesAppTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Enqueue(url);
            var body =
                url.Contains("/Arrivals") ? """
                    [
                      {"destinationName":"Stanmore Underground Station","towards":"","timeToStation":30,"platformName":"Southbound - Platform 1","lineName":"Jubilee","lineId":"jubilee"},
                      {"destinationName":"Aldgate Underground Station","towards":"Aldgate via Baker St","timeToStation":300,"platformName":"Eastbound - Platform 3","lineName":"Circle","lineId":"circle"}
                    ]
                    """
                : url.Contains("/Line/") ? """
                    [
                      {"id":"jubilee","name":"Jubilee","lineStatuses":[{"statusSeverity":10,"statusSeverityDescription":"Good Service"}]},
                      {"id":"circle","name":"Circle","lineStatuses":[{"statusSeverity":6,"statusSeverityDescription":"Severe Delays"}]}
                    ]
                    """
                : url.Contains("/Search/") ? """
                    {"matches":[{"id":"940GZZLUBST","name":"Baker Street Underground Station","modes":["tube"]},{"id":"bus1","name":"Baker Street Bus","modes":["bus"]}]}
                    """
                : """{"commonName":"Baker Street Underground Station"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    public TubeDeparturesAppTests()
    {
        Fonts.Load();
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time");
            await Task.Delay(10);
        }
    }

    private static bool HasPixelsIn(FrameBuffer f, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (f.GetPixel(x, y) != Pixel.Black) return true;
        return false;
    }

    private static async Task<TubeDeparturesApp> ActivatedApp(StubHandler handler)
    {
        var app = new TubeDeparturesApp(new HttpClient(handler));
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);
        return app;
    }

    [Fact]
    public void Settings_KeepTheirKeysOrderAndTypes()
    {
        var app = new TubeDeparturesApp(new HttpClient(new StubHandler()));
        var settings = app.GetSettings().ToList();

        Assert.Equal(
            new[] { "stationSearch", "stationSelect", "stationId", "platformFilter", "maxDepartures", "colorDeparturesByLine" },
            settings.Select(s => s.Key).ToArray());
        Assert.Equal(
            new[] { AppSettingType.String, AppSettingType.Select, AppSettingType.String, AppSettingType.String, AppSettingType.Integer, AppSettingType.Boolean },
            settings.Select(s => s.Type).ToArray());

        var max = settings.Single(s => s.Key == "maxDepartures");
        Assert.Equal(3, max.CurrentValue);
        Assert.Equal(1, max.MinValue);
        Assert.Equal(12, max.MaxValue);
        Assert.Equal(false, settings.Single(s => s.Key == "colorDeparturesByLine").CurrentValue);
        Assert.Equal(new[] { "Type at least 2 chars" }, settings.Single(s => s.Key == "stationSelect").Options);
    }

    [Fact]
    public void UpdateSetting_AcceptsPersistedValueShapes()
    {
        var app = new TubeDeparturesApp(new HttpClient(new StubHandler()));

        // Values as AppSettingsStorage would hand them back (plain CLR values) and as the API receives them (JsonElement)
        app.UpdateSetting("maxDepartures", 99);
        Assert.Equal(12, app.MaxDepartures);
        app.UpdateSetting("maxDepartures", Json("0"));
        Assert.Equal(1, app.MaxDepartures);
        app.UpdateSetting("maxDepartures", Json("\"6\""));
        Assert.Equal(6, app.MaxDepartures);

        app.UpdateSetting("colorDeparturesByLine", true);
        Assert.True(app.ColorDeparturesByLine);
        app.UpdateSetting("colorDeparturesByLine", Json("false"));
        Assert.False(app.ColorDeparturesByLine);

        app.UpdateSetting("platformFilter", Json("\"East\""));
        Assert.Equal("East", app.PlatformFilter);

        // The empty stationSelect that gets persisted must not clear the station
        app.UpdateSetting("stationId", "940GZZLUBST");
        app.UpdateSetting("stationSelect", "");
        app.UpdateSetting("stationSelect", "not a selection");
        Assert.Equal("940GZZLUBST", app.StationId);

        app.UpdateSetting("stationSelect", "940GZZLUOXC | Oxford Circus");
        Assert.Equal("940GZZLUOXC", app.StationId);
    }

    [Fact]
    public async Task Renders_NoStationMessage_ThenDataOnceFetched()
    {
        var handler = new StubHandler();
        var app = await ActivatedApp(handler);

        var frame = new FrameBuffer(256, 64);
        app.Render(frame, CancellationToken.None);
        Assert.False(SnapshotHelper.IsBlank(frame));
        Assert.Empty(handler.Requests);

        app.UpdateSetting("stationId", "940GZZLUBST");
        // The line status tiles (bottom left) only appear once arrivals and line statuses have both loaded (the corner pixel is never text)
        await WaitFor(() =>
        {
            var f = new FrameBuffer(256, 64);
            app.Render(f, CancellationToken.None);
            return f.GetPixel(1, 49) != Pixel.Black;
        });

        Assert.Contains(handler.Requests, r => r.Contains("/StopPoint/940GZZLUBST/Arrivals"));
        Assert.Contains(handler.Requests, r => r.Contains("/Line/circle,jubilee/Status"));
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlatformFilter_AppliesImmediatelyToFetchedArrivals()
    {
        var handler = new StubHandler();
        var app = await ActivatedApp(handler);
        app.UpdateSetting("stationId", "940GZZLUBST");
        await WaitFor(() => handler.Requests.Any(r => r.Contains("/Arrivals")));

        FrameBuffer Rendered()
        {
            var f = new FrameBuffer(256, 64);
            app.Render(f, CancellationToken.None);
            return f;
        }

        // Wait for arrivals to land: the departures area (rows 0-46) shows more than the "Loading..." text
        await WaitFor(() => HasPixelsIn(Rendered(), 200, 20, 256, 46));

        app.UpdateSetting("platformFilter", "nonexistent platform");
        var none = Rendered();
        Assert.False(HasPixelsIn(none, 200, 20, 256, 46)); // only the "No departures" message on the first row
        Assert.True(HasPixelsIn(none, 0, 0, 120, 16));

        app.UpdateSetting("platformFilter", "Eastbound");
        Assert.True(HasPixelsIn(Rendered(), 200, 0, 256, 16)); // the one matching departure
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StationSearch_PublishesRailStopsAsIdPipeName()
    {
        var handler = new StubHandler();
        var app = await ActivatedApp(handler);

        app.UpdateSetting("stationSearch", "b");
        Assert.Equal(new[] { "Type at least 2 chars" }, app.GetSettings().Single(s => s.Key == "stationSelect").Options);

        app.UpdateSetting("stationSearch", "baker");
        await WaitFor(() => app.GetSettings().Single(s => s.Key == "stationSelect").Options!.Contains("940GZZLUBST | Baker Street"));
        Assert.Equal(new[] { "940GZZLUBST | Baker Street" }, app.GetSettings().Single(s => s.Key == "stationSelect").Options);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }
}
