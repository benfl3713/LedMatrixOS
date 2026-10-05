using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.ISS;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class IssTrackerAppTests(ITestOutputHelper output)
{
    private static readonly (double Lat, double Lon) London = (51.5, -0.12);

    private static IssSnapshot Snap(double lat, double lon, string visibility = "visible", double solarLat = 20, double solarLon = 10) =>
        new(IssStatus.Ok, FakeIssSource.At(lat, lon, visibility, solarLat, solarLon));

    /// <summary>Two samples a poll apart (so the heading is known and the ground track is drawn), then a warm-up of animation time.</summary>
    private static (IssTrackerApp App, AppStage Stage, MutableLive<IssSnapshot> Live) Tracker(
        IssSnapshot first, IssSnapshot? second, bool noHome = false, int warmMs = 1500, Action<IssTrackerApp>? configure = null)
    {
        Fonts.Load();
        var app = new IssTrackerApp(new FakeIssSource()) { Time = new FakeTime() };
        configure?.Invoke(app);
        var live = new MutableLive<IssSnapshot> { Value = first };
        app.UseData(live, noHome ? null : London);
        var stage = new AppStage(app);
        stage.Step(33, 3);
        if (second is not null)
        {
            live.Value = second;
            stage.Step(33, 3);
        }
        stage.Step(33, warmMs / 33);
        return (app, stage, live);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- data --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsThePosition()
    {
        const string json = """
            {"name":"iss","id":25544,"latitude":-12.5,"longitude":143.25,"altitude":419.4,"velocity":27580.1,"visibility":"daylight",
             "footprint":4500.2,"timestamp":1760000000,"daynum":2460000.5,"solar_lat":-7.1,"solar_lon":96.4,"units":"kilometers"}
            """;
        var snap = WhereTheIssSource.Parse(json);
        Assert.Equal(IssStatus.Ok, snap.Status);
        Assert.Equal(-12.5, snap.Position!.Latitude);
        Assert.Equal(143.25, snap.Position.Longitude);
        Assert.Equal(419.4, snap.Position.AltitudeKm);
        Assert.Equal("daylight", snap.Position.Visibility);
        Assert.Equal(96.4, snap.Position.SolarLon);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"latitude":123,"longitude":4}""")]
    [InlineData("""{"message":"rate limited"}""")]
    public void Parse_AnythingUnusable_IsOffline(string json)
    {
        var snap = WhereTheIssSource.Parse(json);
        Assert.Equal(IssStatus.Offline, snap.Status);
        Assert.Null(snap.Position);
    }

    [Theory]
    [InlineData(51.5, -0.12, "United Kingdom")]
    [InlineData(40, -100, "United States")]
    [InlineData(-25, 135, "Australia")]
    [InlineData(-20, -140, "South Pacific")]
    [InlineData(25, -40, "North Atlantic")]
    [InlineData(-10, 80, "Indian Ocean")]
    [InlineData(33, 15, "Mediterranean Sea")]
    [InlineData(-10, -55, "Brazil")]
    public void Regions_NameTheCoarsePlace(double lat, double lon, string expected) =>
        Assert.Equal(expected, IssRegions.Describe(lat, lon));

    [Fact]
    public void Orbit_StaysWithinTheInclinationAndMovesEast()
    {
        double maxLat = 0;
        for (int t = -3000; t <= 3000; t += 60)
            maxLat = Math.Max(maxLat, Math.Abs(IssOrbit.Project(0, 0, true, t).Lat));
        Assert.InRange(maxLat, 51.4, 51.7);

        var (lat, lon) = IssOrbit.Project(0, 0, true, 120);
        Assert.True(lat > 0);                // ascending crosses the equator heading north
        Assert.InRange(lon, 3.5, 5);         // about 2 degrees a minute eastward at the equator
        Assert.Equal(0, IssOrbit.Project(10, 20, false, 0).Lon - 20, 6);
    }

    [Fact]
    public void Model_EntersRangeOnce_AndHasHysteresis()
    {
        var model = new IssModel();
        var home = new HomePoint(London.Lat, London.Lon, "London");

        Assert.False(model.Refresh(Snap(0, -150), home, false));
        Assert.False(model.InRange);
        Assert.True(model.Refresh(Snap(52, 3), home, false));
        Assert.True(model.Overhead);
        Assert.Equal("OVERHEAD NOW", model.Headline);
        Assert.False(model.Refresh(Snap(52.2, 3.3), home, false));       // still in range: not "entered" again

        // 1500 km is ~13.5 degrees: just outside the entry range but inside the exit range stays lit
        Assert.False(model.Refresh(Snap(64.5, -0.12), home, false));
        Assert.True(model.InRange);
        Assert.False(model.Refresh(Snap(72, -0.12), home, false));
        Assert.False(model.InRange);
    }

    [Fact]
    public void Model_GroundTrack_NeedsAHeading()
    {
        var model = new IssModel();
        model.Refresh(Snap(10, 20), null, false);
        Assert.Equal(0, model.PastCount);
        model.Refresh(Snap(10.4, 21.5), null, false);
        Assert.Equal(IssModel.PastPoints, model.PastCount);
        Assert.Equal("Set a location", model.DistText);
    }

    [Fact]
    public async Task Source_ReportsOffline_WhenTheRequestFails()
    {
        var source = new WhereTheIssSource(new HttpClient(new ThrowingHandler()));
        var snap = await source.GetAsync(CancellationToken.None);
        Assert.Equal(IssStatus.Offline, snap.Status);
        Assert.Null(snap.Position);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("down");
    }

    private sealed class FakeOverlays : IOverlayService
    {
        public List<IOverlay> Added { get; } = new();
        public int Width => 256;
        public int Height => 64;
        public void Add(IOverlay overlay) => Added.Add(overlay);
        public bool Remove(string id) => true;
    }

    // ---- app ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new IssTrackerApp(new FakeIssSource());
        Assert.Equal("iss-tracker", app.Id);
        Assert.Equal(new[] { "location", "units", "showMap", "alertDistance", "alerts" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Equal(AppSettingType.Search, app.GetSettings().Single(s => s.Key == "location").Type);
    }

    [Fact]
    public void App_ShowsTheStationFromTheLatestPoll()
    {
        var (app, _, _) = Tracker(Snap(19.6, -150), Snap(20, -149), noHome: true);
        Assert.Equal("over North Pacific", app.Model.RegionText);
        Assert.Equal("20.0N 149.0W", app.Model.LatLonText);
        Assert.Equal("ALT 421 km", app.Model.AltText);
        Assert.Equal(27579, app.Model.Speed);
    }

    [Fact]
    public void App_InRange_RaisesOneToast_AndAlertsCanBeTurnedOff()
    {
        var overlays = new FakeOverlays();
        var (_, stage, live) = Tracker(Snap(0, -150), null, configure: a => a.OverlayService = overlays);
        Assert.Empty(overlays.Added);

        live.Value = Snap(52, 3);
        stage.Step(33, 5);
        var toast = Assert.Single(overlays.Added);
        Assert.Equal("ISS overhead now", ((OverlayBase)toast).Text);
        stage.Step(33, 30);
        Assert.Single(overlays.Added);

        var quiet = new FakeOverlays();
        var (_, stage2, live2) = Tracker(Snap(0, -150), null, configure: a => { a.OverlayService = quiet; a.Alerts = false; });
        live2.Value = Snap(52, 3);
        stage2.Step(33, 5);
        Assert.Empty(quiet.Added);
    }

    // ---- goldens -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_FarAway_OverThePacific()
    {
        var (_, stage, _) = Tracker(Snap(19.4, -153.5, "daylight", 12, -140), Snap(20, -152, "daylight", 12, -140));
        Golden(stage, "iss_tracker_pacific");
    }

    [Fact]
    public void Golden_OverheadNow_HighlightsTheBand()
    {
        var (_, stage, _) = Tracker(Snap(50.9, -3.2, "eclipsed", -8, 150), Snap(51.4, -1.8, "eclipsed", -8, 150));
        Golden(stage, "iss_tracker_overhead");
    }

    [Fact]
    public void Golden_VisibleTonight_InMiles()
    {
        var (_, stage, _) = Tracker(Snap(52.1, -17.9, "visible", 5, 80), Snap(51.6, -16.5, "visible", 5, 80), configure: a => a.Units = "Miles");
        Golden(stage, "iss_tracker_visible_tonight");
    }

    [Fact]
    public void Golden_MapHidden_GivesTheStatsTheFullWidth()
    {
        var (_, stage, _) = Tracker(Snap(50.9, -3.2, "eclipsed", -8, 150), Snap(51.4, -1.8, "eclipsed", -8, 150), configure: a => a.ShowMap = false);
        Golden(stage, "iss_tracker_no_map");
    }

    [Fact]
    public void Golden_MapHidden_FarAway()
    {
        var (_, stage, _) = Tracker(Snap(19.4, -153.5, "daylight", 12, -140), Snap(20, -152, "daylight", 12, -140), configure: a => a.ShowMap = false);
        Golden(stage, "iss_tracker_no_map_far");
    }

    [Fact]
    public void Model_AlertDistance_MovesTheRange()
    {
        var home = new HomePoint(London.Lat, London.Lon, "London");
        var model = new IssModel();
        // ~1100 km north-west of London: outside a 800 km alert, inside the default 1500 km one
        Assert.False(model.Refresh(Snap(60, -10), home, false, 800));
        Assert.False(model.InRange);
        Assert.True(model.Refresh(Snap(60.1, -10), home, false, 1500));
        Assert.True(model.InRange);
        Assert.False(model.Overhead);
    }

    [Fact]
    public void Golden_Offline_BeforeAnyData()
    {
        var (_, stage, _) = Tracker(IssSnapshot.Offline, null);
        Golden(stage, "iss_tracker_offline");
    }

    [Fact]
    public void Golden_Loading()
    {
        Fonts.Load();
        var app = new IssTrackerApp(new FakeIssSource()) { Time = new FakeTime() };
        app.UseData(new MutableLive<IssSnapshot> { Value = null }, London);
        var stage = new AppStage(app);
        stage.Step(33, 45);
        Golden(stage, "iss_tracker_loading");
    }

    [Fact]
    public void Golden_SignalLost_KeepsTheLastPosition()
    {
        var (_, stage, live) = Tracker(Snap(-30.6, 40), Snap(-30, 41.5));
        live.Value = IssSnapshot.Offline;
        stage.Step(33, 30);
        Golden(stage, "iss_tracker_signal_lost");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage, _) = Tracker(Snap(50.9, -3.2), Snap(51.4, -1.8));
        var run = stage.MeasureSteadyAllocation(windows: 6);

        output.WriteLine($"iss tracker: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");    }
}
