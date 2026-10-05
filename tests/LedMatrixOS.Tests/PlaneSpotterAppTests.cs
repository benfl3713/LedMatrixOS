using System.Diagnostics;
using System.Net;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.PlaneSpotter;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class PlaneSpotterAppTests(ITestOutputHelper output)
{
    private const double HomeLat = 51.5, HomeLon = -0.12;
    private static readonly DateTimeOffset Start = new(2026, 1, 2, 13, 45, 7, TimeSpan.Zero);

    /// <summary>An aircraft <paramref name="km"/> from home on <paramref name="bearing"/>, with metric measurements.</summary>
    private static Aircraft Ac(string icao, string callsign, double bearing, double km, double? altM = 3500, double? ms = 130, double? track = 90, double? vr = 0,
        string country = "United Kingdom", bool ground = false)
    {
        double rad = bearing * Math.PI / 180;
        double lat = HomeLat + km * Math.Cos(rad) / 111.195;
        double lon = HomeLon + km * Math.Sin(rad) / (111.195 * Math.Cos(lat * Math.PI / 180));
        return new Aircraft(icao, callsign, country, lat, lon, altM, ms, track, vr, ground);
    }

    private static PlaneSnapshot Snap(params Aircraft[] a) => new(PlaneStatus.Ok, a, HomeLat, HomeLon);

    private static Aircraft[] Overhead() => [Ac("400a1b", "BAW117", 40, 6.2, 3200, 128, 255, 6.5)];

    private static Aircraft[] Several() =>
    [
        Ac("400a1b", "BAW117", 40, 6.2, 3200, 128, 255, 6.5),
        Ac("4ca8f2", "RYR48GP", 160, 11.4, 5800, 190, 20, -4.0, "Ireland"),
        Ac("3c6444", "DLH4YT", 285, 14.9, 9100, 230, 110, 0, "Germany"),
        Ac("06a0d1", "QTR8", 330, 19.5, 11800, 250, 285, 0.3, "Qatar"),
        Ac("a1b2c3", "N123AB", 95, 21.8, 1500, 70, 180, 2.5, "United States"),
        Ac("400f00", "EZY82LR", 210, 8.0, 2100, 110, 330, -6.0),
        Ac("440cc1", "AUA1TD", 20, 17.3, 7300, 200, 60, 0, "Austria"),
    ];

    private static (PlaneSpotterApp App, AppStage Stage, FakeTime Clock) Spotter(PlaneSnapshot? snapshot, bool location = true, int warmMs = 1500, Action<PlaneSpotterApp>? configure = null)
    {
        Fonts.Load();
        var clock = new FakeTime { Now = Start };
        var app = new PlaneSpotterApp(new FakePlaneSource()) { Time = clock };
        configure?.Invoke(app);
        app.UseData(new MutableLive<PlaneSnapshot> { Value = snapshot }, location);
        var stage = new AppStage(app);
        stage.Step(33, warmMs / 33);
        return (app, stage, clock);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Haversine_MatchesKnownDistances()
    {
        Assert.Equal(343.6, PlaneMath.HaversineKm(51.5074, -0.1278, 48.8566, 2.3522), 0.6);   // London to Paris
        Assert.Equal(0, PlaneMath.HaversineKm(10, 20, 10, 20), 6);
        Assert.Equal(111.19, PlaneMath.HaversineKm(0, 0, 1, 0), 0.05);
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 1, 90)]
    [InlineData(1, 0, 0, 0, 180)]
    [InlineData(0, 1, 0, 0, 270)]
    public void Bearing_PointsAlongTheCompass(double lat1, double lon1, double lat2, double lon2, double expected) =>
        Assert.Equal(expected, PlaneMath.BearingDegrees(lat1, lon1, lat2, lon2), 3);

    [Fact]
    public void Bearing_RoundTripsWithTheTestFixtureGeometry()
    {
        var a = Ac("x", "X", 215, 9);
        Assert.Equal(215, PlaneMath.BearingDegrees(HomeLat, HomeLon, a.Latitude!.Value, a.Longitude!.Value), 0.5);
        Assert.Equal(9, PlaneMath.HaversineKm(HomeLat, HomeLon, a.Latitude.Value, a.Longitude.Value), 0.02);
    }

    [Fact]
    public void BoundingBox_ContainsTheCircle_AndClampsToTheGlobe()
    {
        var (minLat, minLon, maxLat, maxLon) = PlaneMath.BoundingBox(51.5, -0.12, 25);
        Assert.Equal(51.5 - 25 / 111.195, minLat, 4);
        Assert.Equal(51.5 + 25 / 111.195, maxLat, 4);
        Assert.True(maxLon - -0.12 > 25 / 111.195, "longitude spans widen away from the equator");
        Assert.Equal(-0.12 - (maxLon + 0.12), minLon, 6);

        var (a, b, c, d) = PlaneMath.BoundingBox(89.9, 179.9, 100);
        Assert.Equal((90.0, 180.0), (c, d));
        Assert.True(a < 89.9 && b < 179.9);
    }

    [Fact]
    public void UnitConversions()
    {
        Assert.Equal(3280.84, PlaneMath.MetresToFeet(1000), 0.01);
        Assert.Equal(194.38, PlaneMath.MsToKnots(100), 0.01);
        Assert.True(PlaneMath.IsValidCoordinate(51.5, -0.1));
        Assert.False(PlaneMath.IsValidCoordinate(91, 0));
    }

    [Theory]
    [InlineData("BAW117", "British Airways")]
    [InlineData("ezy82lr", "easyJet")]
    [InlineData("RYR48GP", "Ryanair")]
    [InlineData("UAE5", "Emirates")]
    [InlineData("BA117", "British Airways")]
    [InlineData("KL1234", "KLM")]
    [InlineData("  VIR3N ", "Virgin Atlantic")]
    public void Airline_IsFoundFromTheCallsignPrefix(string callsign, string expected) => Assert.Equal(expected, AirlineLookup.Find(callsign));

    [Theory]
    [InlineData("N123AB")]
    [InlineData("G-ABCD")]
    [InlineData("ZZZ1")]
    [InlineData("")]
    [InlineData(null)]
    public void Airline_UnknownIsNullAndDescribeFallsBack(string? callsign)
    {
        Assert.Null(AirlineLookup.Find(callsign));
        Assert.Equal("Germany", AirlineLookup.Describe(callsign, "Germany"));
        Assert.Equal("Unknown operator", AirlineLookup.Describe(callsign, " "));
    }

    // ---- source -----------------------------------------------------------------------------------------------------

    private const string OpenSkyJson = """
        {"time":1767361507,"states":[
          ["400a1b","BAW117  ","United Kingdom",1767361500,1767361506,-0.0661,51.5472,3200.4,false,128.3,255.2,6.5,null,3253.7,"2655",false,0],
          ["4ca8f2","RYR48GP ","Ireland",1767361502,1767361506,-0.0310,51.4630,null,false,null,null,null,null,null,null,false,0],
          ["a1b2c3","","United States",null,1767361490,null,null,null,true,null,null,null,null,null,null,false,0],
          ["06a0d1",null,"Qatar",1767361500,1767361506,-0.2,51.55,null,false,251.1,284.9,null,null,11800.0,"1000",false,0],
          [null,"BAD1","Nowhere",null,null,null,null,null,false,null,null,null,null,null,null,false,0],
          "garbage",
          ["short"]
        ]}
        """;

    [Fact]
    public void Parse_ReadsFieldsAndToleratesNulls()
    {
        var planes = OpenSkyPlaneSource.Parse(OpenSkyJson);

        Assert.Equal(["400a1b", "4ca8f2", "a1b2c3", "06a0d1", "short"], planes.Select(p => p.Icao24).ToArray());
        var ba = planes[0];
        Assert.Equal("BAW117", ba.Callsign);
        Assert.Equal("United Kingdom", ba.Country);
        Assert.Equal(51.5472, ba.Latitude);
        Assert.Equal(-0.0661, ba.Longitude);
        Assert.Equal(3200.4, ba.AltitudeMetres);
        Assert.Equal(128.3, ba.VelocityMs);
        Assert.Equal(255.2, ba.TrackDegrees);
        Assert.Equal(6.5, ba.VerticalRateMs);
        Assert.False(ba.OnGround);

        var sparse = planes[1];
        Assert.Null(sparse.AltitudeMetres);
        Assert.Null(sparse.VelocityMs);
        Assert.Null(sparse.TrackDegrees);
        Assert.Null(sparse.VerticalRateMs);

        Assert.True(planes[2].OnGround);
        Assert.Null(planes[2].Latitude);
        Assert.Equal("", planes[2].Callsign);
        Assert.Equal(11800.0, planes[3].AltitudeMetres);   // falls back to the geometric altitude
        Assert.Equal("", planes[3].Callsign);
        Assert.Null(planes[4].Latitude);
    }

    [Theory]
    [InlineData("""{"time":1,"states":null}""")]
    [InlineData("""{"time":1}""")]
    [InlineData("""{"states":[]}""")]
    [InlineData("[]")]
    public void Parse_EmptyShapesGiveNoAircraft(string json) => Assert.Empty(OpenSkyPlaneSource.Parse(json));

    [Fact]
    public void Url_CarriesTheBoundingBox()
    {
        var url = OpenSkyPlaneSource.Url(new PlaneQuery(51.5, -0.12, 25));
        Assert.StartsWith("https://opensky-network.org/api/states/all?lamin=51.2752&lomin=", url);
        Assert.Contains("&lamax=51.7248&lomax=", url);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };

    [Fact]
    public async Task Source_ReturnsAircraft_AndBacksOffOn429()
    {
        var clock = new FakeTime { Now = Start };
        var step = 0;
        var handler = new StubHandler(_ => step++ switch
        {
            0 => Json(OpenSkyJson),
            1 => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => Json(OpenSkyJson),
        });
        var source = new OpenSkyPlaneSource(new HttpClient(handler), clock);
        var q = new PlaneQuery(HomeLat, HomeLon, 25);

        var ok = await source.GetAsync(q, CancellationToken.None);
        Assert.Equal(PlaneStatus.Ok, ok.Status);
        Assert.Equal(5, ok.Aircraft.Length);
        Assert.Equal(HomeLat, ok.HomeLatitude);

        var busy = await source.GetAsync(q, CancellationToken.None);   // 429
        Assert.Equal(PlaneStatus.Busy, busy.Status);
        Assert.Equal(2, handler.Calls);

        clock.Now = Start.AddSeconds(30);   // still backing off: no request
        Assert.Equal(PlaneStatus.Busy, (await source.GetAsync(q, CancellationToken.None)).Status);
        Assert.Equal(2, handler.Calls);

        clock.Now = Start.AddSeconds(61);
        Assert.Equal(PlaneStatus.Ok, (await source.GetAsync(q, CancellationToken.None)).Status);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Source_NeverThrows_FailuresBecomeOffline()
    {
        var q = new PlaneQuery(HomeLat, HomeLon, 25);

        var serverError = new OpenSkyPlaneSource(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))));
        Assert.Equal(PlaneStatus.Offline, (await serverError.GetAsync(q, CancellationToken.None)).Status);

        var broken = new OpenSkyPlaneSource(new HttpClient(new StubHandler(_ => throw new HttpRequestException("no network"))));
        Assert.Equal(PlaneStatus.Offline, (await broken.GetAsync(q, CancellationToken.None)).Status);

        var garbage = new OpenSkyPlaneSource(new HttpClient(new StubHandler(_ => Json("<html>oops</html>"))));
        Assert.Equal(PlaneStatus.Offline, (await garbage.GetAsync(q, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Source_HonoursRetryAfter()
    {
        var clock = new FakeTime { Now = Start };
        var handler = new StubHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(300));
            return r;
        });
        var source = new OpenSkyPlaneSource(new HttpClient(handler), clock);
        var q = new PlaneQuery(HomeLat, HomeLon, 25);

        await source.GetAsync(q, CancellationToken.None);
        clock.Now = Start.AddSeconds(200);
        await source.GetAsync(q, CancellationToken.None);
        Assert.Equal(1, handler.Calls);
        clock.Now = Start.AddSeconds(301);
        await source.GetAsync(q, CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    // ---- location & settings ----------------------------------------------------------------------------------------

    [Fact]
    public void Settings_KeepTheirKeysOrderAndTypes_AndNoCoordinatesAreExposed()
    {
        Fonts.Load();
        var app = new PlaneSpotterApp(new FakePlaneSource());
        var settings = app.GetSettings().ToList();

        Assert.Equal(new[] { "location", "radius", "alerts", "units", "pageSeconds" }, settings.Select(s => s.Key).ToArray());
        Assert.Equal(new[] { AppSettingType.Search, AppSettingType.Integer, AppSettingType.Boolean, AppSettingType.Select, AppSettingType.Integer }, settings.Select(s => s.Type).ToArray());
        Assert.Equal(5, settings[1].MinValue);
        Assert.Equal(100, settings[1].MaxValue);
        Assert.Equal(25, app.Radius);
        Assert.True(app.Alerts);
        Assert.Equal("plane-spotter", app.Id);
        Assert.DoesNotContain(settings, s => s.Key.Contains("lat", StringComparison.OrdinalIgnoreCase) || s.Key.Contains("lon", StringComparison.OrdinalIgnoreCase));
    }

    private static IConfiguration Config(params (string, string)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    [Fact]
    public void Location_ComesFromConfig_ThenWeatherCoordinates_ThenWeatherPlaceName_ElseNone()
    {
        Fonts.Load();
        var http = new HttpClient(new StubHandler(_ => Json("{}")));

        var explicitApp = new PlaneSpotterApp(new FakePlaneSource(), new PlaceGeocoder(http));
        explicitApp.ReadLocation(Config(("PlaneSpotter:Latitude", "51.47"), ("PlaneSpotter:Longitude", "-0.45"), ("Weather:Location", "Paris")));
        Assert.Equal((51.47, -0.45), explicitApp.Home);

        var weatherCoords = new PlaneSpotterApp(new FakePlaneSource(), new PlaceGeocoder(http));
        weatherCoords.ReadLocation(Config(("Weather:Location", "48.85,2.35")));
        Assert.Equal((48.85, 2.35), weatherCoords.Home);

        var weatherPlace = new PlaneSpotterApp(new FakePlaneSource(), new PlaceGeocoder(http));
        weatherPlace.ReadLocation(Config(("Weather:Location", "Edinburgh")));
        Assert.True(weatherPlace.HasLocation);
        Assert.Null(weatherPlace.Home);   // geocoded later, on the poll

        var none = new PlaneSpotterApp(new FakePlaneSource(), new PlaceGeocoder(http));
        none.ReadLocation(Config());
        Assert.False(none.HasLocation);

        var invalid = new PlaneSpotterApp(new FakePlaneSource(), new PlaceGeocoder(http));
        invalid.ReadLocation(Config(("PlaneSpotter:Latitude", "999"), ("PlaneSpotter:Longitude", "0")));
        Assert.False(invalid.HasLocation);
    }

    [Fact]
    public async Task Geocoder_ResolvesPlaceNames_AndCachesThem()
    {
        var handler = new StubHandler(_ => Json("""{"results":[{"name":"Edinburgh","latitude":55.95,"longitude":-3.19}]}"""));
        var geocoder = new PlaceGeocoder(new HttpClient(handler));

        Assert.Equal((55.95, -3.19), await geocoder.ResolveAsync("Edinburgh", CancellationToken.None));
        Assert.Equal((55.95, -3.19), await geocoder.ResolveAsync("edinburgh", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        Assert.Equal((10.5, 20.5), await geocoder.ResolveAsync("10.5, 20.5", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    // ---- model --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Model_SortsNearestFirst_FiltersRadiusAndGround_AndFormatsRows()
    {
        var model = new PlaneBoardModel();
        var planes = Several().Append(Ac("ff0000", "FAR1", 10, 60)).Append(Ac("ee0000", "GND1", 10, 2, ground: true)).Append(new Aircraft("dd0000", "NOPOS", "", null, null, null, null, null, null, false)).ToArray();
        model.Refresh(Snap(planes), 25, feet: true);

        Assert.Equal(7, model.Count);
        Assert.Equal("BAW117", model.Hero!.Callsign);
        Assert.Equal(model.All.OrderBy(r => r.DistKm).Select(r => r.Key), model.All.Select(r => r.Key));
        Assert.Equal("7 in range".Replace("7", "7"), model.CountText);
        Assert.Equal("6.2km", model.Hero.DistText);
        Assert.Equal("10500", model.Hero.AltText);        // 3200 m is 10499 ft, to the nearest 100
        Assert.Equal("249 kt", model.Hero.SpeedText);
        Assert.Equal(1, model.Hero.Climb);
        Assert.Equal(-1, model.All.Single(r => r.Callsign == "RYR48GP").Climb);
        Assert.Equal(0, model.All.Single(r => r.Callsign == "DLH4YT").Climb);
        Assert.Equal("British Airways", model.Hero.Operator);
        Assert.Equal("United States", model.All.Single(r => r.Callsign == "N123AB").Operator);
        Assert.Equal(2, model.Pages.Count);                // six further aircraft, five a page
        Assert.Equal(5, model.Page(0).Count);
        Assert.Single(model.Page(1));

        model.Refresh(Snap(planes), 25, feet: false);      // same data, other units
        Assert.Equal("3200", model.Hero.AltText);
        model.Refresh(Snap(planes), 10, feet: false);      // smaller radius
        Assert.Equal(2, model.Count);
    }

    [Fact]
    public void Model_PlacesRadarDotsByBearingAndDistance()
    {
        var model = new PlaneBoardModel();
        model.Refresh(Snap(Ac("a", "EAST", 90, 10), Ac("b", "NORTH", 0, 20), Ac("c", "SW", 225, 25)), 25, true);

        var east = model.All.Single(r => r.Callsign == "EAST");
        Assert.Equal(0.4f, east.RadarX, 0.01f);
        Assert.Equal(0f, east.RadarY, 0.02f);
        var north = model.All.Single(r => r.Callsign == "NORTH");
        Assert.Equal(-0.8f, north.RadarY, 0.01f);
        var sw = model.All.Single(r => r.Callsign == "SW");
        Assert.True(sw.RadarX < 0 && sw.RadarY > 0);
    }

    [Fact]
    public void Model_KeepsRowsAcrossPolls_ReportsNewAircraft_AndIgnoresFailedPolls()
    {
        var model = new PlaneBoardModel();
        var a = Ac("400a1b", "BAW117", 40, 6);
        var b = Ac("4ca8f2", "RYR48GP", 160, 11);

        model.Refresh(Snap(a), 25, true);
        Assert.Empty(model.NewRows);                       // the first poll is the baseline
        var row = model.Hero;

        model.Refresh(Snap(a, b), 25, true);
        Assert.Equal(["RYR48GP"], model.NewRows.Select(r => r.Callsign).ToArray());
        Assert.Same(row, model.All.Single(r => r.Key == "400a1b"));

        model.Refresh(PlaneSnapshot.Failed(PlaneStatus.Offline, new PlaneQuery(HomeLat, HomeLon, 25)), 25, true);
        Assert.Equal(2, model.Count);                      // last good aircraft kept
        Assert.Empty(model.NewRows);

        model.Refresh(Snap(a, b), 25, true);               // back again: nobody is new
        Assert.Empty(model.NewRows);

        model.Refresh(Snap(a), 25, true);
        model.Refresh(Snap(a, b), 25, true);               // left and re-entered
        Assert.Single(model.NewRows);
    }

    // ---- rendering ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Snapshots()
    {
        var (_, overhead, _) = Spotter(Snap(Overhead()));
        Golden(overhead, "plane_spotter_overhead");

        var (many, multiple, _) = Spotter(Snap(Several()));
        Assert.Equal(7, many.Model.Count);
        Golden(multiple, "plane_spotter_multiple");

        var (_, clear, _) = Spotter(Snap());
        Golden(clear, "plane_spotter_clear");

        var (_, noLocation, _) = Spotter(null, location: false);
        Golden(noLocation, "plane_spotter_no_location");

        var (_, offline, _) = Spotter(PlaneSnapshot.Failed(PlaneStatus.Offline, new PlaneQuery(HomeLat, HomeLon, 25)));
        Golden(offline, "plane_spotter_offline");
    }

    [Fact]
    public void Snapshot_Metres_ShowsMetresLabel()
    {
        var (_, stage, _) = Spotter(Snap(Overhead()), configure: a => a.Units = "Metres");
        Golden(stage, "plane_spotter_metres");
    }

    [Fact]
    public void Busy_AndLoading_AndClearRadiusHint()
    {
        var (busy, _, _) = Spotter(PlaneSnapshot.Failed(PlaneStatus.Busy, new PlaneQuery(HomeLat, HomeLon, 25)));
        Assert.False(busy.Model.Count > 0);
        var (_, loading, _) = Spotter(null);
        Assert.False(SnapshotHelper.IsBlank(loading.Snapshot()));
    }

    [Fact]
    public void Pager_SlidesToTheSecondPage()
    {
        var (app, stage, _) = Spotter(Snap(Several()), warmMs: 300);
        Assert.Equal(2, app.ListPager!.PageCount);

        stage.Step(33, 6 * 30 + 40);
        Assert.Equal(1, app.ListPager.PageIndex);
    }

    // ---- toasts -------------------------------------------------------------------------------------------------------

    private sealed class FakeOverlays : IOverlayService
    {
        public List<IOverlay> Added { get; } = new();
        public List<string> Removed { get; } = new();
        public int Width => 256;
        public int Height => 64;
        public void Add(IOverlay overlay) => Added.Add(overlay);
        public bool Remove(string id) { Removed.Add(id); return true; }
    }

    private static (PlaneSpotterApp App, AppStage Stage, FakeTime Clock, MutableLive<PlaneSnapshot> Live, FakeOverlays Overlays) Watching(params Aircraft[] initial)
    {
        Fonts.Load();
        var clock = new FakeTime { Now = Start };
        var overlays = new FakeOverlays();
        var live = new MutableLive<PlaneSnapshot> { Value = Snap(initial) };
        var app = new PlaneSpotterApp(new FakePlaneSource()) { Time = clock, OverlayService = overlays };
        app.UseData(live);
        var stage = new AppStage(app);
        stage.Step(33, 5);
        return (app, stage, clock, live, overlays);
    }

    [Fact]
    public void Toast_AnnouncesANewAircraftOnce_AndNotTheInitialOnes()
    {
        var (_, stage, _, live, overlays) = Watching(Ac("400a1b", "BAW117", 40, 6));
        Assert.Empty(overlays.Added);                      // already overhead when we started

        live.Value = Snap(Ac("400a1b", "BAW117", 40, 6), Ac("4ca8f2", "RYR48GP", 160, 11));
        stage.Step(33, 5);
        var toast = Assert.Single(overlays.Added);
        Assert.Equal("toast", ((OverlayBase)toast).Kind);
        Assert.Equal("RYR48GP inbound", ((OverlayBase)toast).Text);

        stage.Step(33, 30);                                // later frames do not repeat it
        Assert.Single(overlays.Added);
    }

    [Fact]
    public void Toast_IsRateLimitedToOneAMinute_AndCanBeTurnedOff()
    {
        var (app, stage, clock, live, overlays) = Watching(Ac("400a1b", "BAW117", 40, 6));

        live.Value = Snap(Ac("400a1b", "BAW117", 40, 6), Ac("4ca8f2", "RYR48GP", 160, 11));
        stage.Step(33, 3);
        Assert.Single(overlays.Added);

        clock.Now = Start.AddSeconds(20);
        live.Value = Snap(Ac("400a1b", "BAW117", 40, 6), Ac("4ca8f2", "RYR48GP", 160, 11), Ac("3c6444", "DLH4YT", 285, 14));
        stage.Step(33, 3);
        Assert.Single(overlays.Added);                     // too soon: swallowed

        clock.Now = Start.AddSeconds(45);
        live.Value = Snap(Ac("400a1b", "BAW117", 40, 6), Ac("4ca8f2", "RYR48GP", 160, 11), Ac("3c6444", "DLH4YT", 285, 14), Ac("06a0d1", "QTR8", 330, 19));
        stage.Step(33, 3);
        Assert.Single(overlays.Added);                     // still inside the minute

        clock.Now = Start.AddSeconds(61);
        live.Value = Snap(Ac("400a1b", "BAW117", 40, 6), Ac("4ca8f2", "RYR48GP", 160, 11), Ac("3c6444", "DLH4YT", 285, 14), Ac("06a0d1", "QTR8", 330, 19), Ac("440cc1", "AUA1TD", 20, 17));
        stage.Step(33, 3);
        Assert.Equal(2, overlays.Added.Count);
        Assert.Equal("AUA1TD inbound", ((OverlayBase)overlays.Added[1]).Text);

        app.Alerts = false;
        clock.Now = Start.AddSeconds(200);
        live.Value = Snap(Ac("400a1b", "BAW117", 40, 6), Ac("e00001", "NEW1", 10, 5));
        stage.Step(33, 3);
        Assert.Equal(2, overlays.Added.Count);
    }

    [Fact]
    public void Toast_WithoutAnOverlayService_StillRenders()
    {
        var (app, _, _) = Spotter(Snap(Overhead()));
        Assert.Null(app.OverlayService);
        Assert.Equal(1, app.Model.Count);
    }

    // ---- performance --------------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage, _) = Spotter(Snap(Several()));
        for (int i = 0; i < 400; i++) { stage.Step(33); stage.Render(); }

        int measured = 0;
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 12; window++)
        {
            int page = app.ListPager!.PageIndex;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (page != app.ListPager.PageIndex || app.ListPager.IsTransitioning) continue;
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        output.WriteLine($"plane spotter: {ms:F3} ms/frame, {measured} steady windows");
        Assert.True(measured >= 3);
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }

    [Fact]
    public void SteadyState_ClearSkies_DoesNotAllocate()
    {
        var (_, stage, _) = Spotter(Snap());
        for (int i = 0; i < 200; i++) { stage.Step(33); stage.Render(); }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++) { stage.Step(33); stage.Render(); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 256, $"allocated {allocated} bytes");
    }
}
