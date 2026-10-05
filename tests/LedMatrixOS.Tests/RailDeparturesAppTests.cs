using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Rail;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class RailDeparturesAppTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Start = new(2026, 1, 2, 13, 45, 7, TimeSpan.Zero);   // FakeTime's clock

    private static RailService Svc(DateTimeOffset now, int minutes, string dest, string platform, RailStatus status = RailStatus.OnTime, int delay = 0, bool last = false,
        string calling = "Clapham Junction, East Croydon, London Bridge") =>
        new($"{minutes}{dest}", Minute(now).AddMinutes(minutes), status == RailStatus.Delayed ? Minute(now).AddMinutes(minutes + delay) : null,
            status, dest, platform, "Southern", calling, 8, last);

    private static DateTimeOffset Minute(DateTimeOffset t) => t.AddSeconds(-t.Second);

    private static RailService[] Normal(DateTimeOffset now) =>
    [
        Svc(now, 7, "London Bridge", "2"),
        Svc(now, 19, "Brighton", "1"),
        Svc(now, 33, "Ashford International", "3"),
        Svc(now, 46, "Gatwick Airport", "1"),
        Svc(now, 54, "London Victoria", "2"),
    ];

    private static RailService[] Disrupted(DateTimeOffset now) =>
    [
        Svc(now, 7, "London Bridge", "2", RailStatus.Delayed, 6),
        Svc(now, 19, "Brighton", "1", RailStatus.Cancelled),
        Svc(now, 33, "Ashford International", "3"),
        Svc(now, 46, "Gatwick Airport", "1", RailStatus.Delayed, 5),
    ];

    private static (RailDeparturesApp App, AppStage Stage, FakeTime Clock) Board(RailService[]? services, DateTimeOffset? at = null, int warmMs = 1500)
    {
        Fonts.Load();
        var clock = new FakeTime { Now = at ?? Start };
        var app = new RailDeparturesApp { Time = clock };
        app.UseData(new MutableLive<RailService[]> { Value = services }, "Havenbridge");
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

    // ---- settings -----------------------------------------------------------------------------------------------

    [Fact]
    public void Settings_KeepTheirKeysOrderAndTypes()
    {
        Fonts.Load();
        var app = new RailDeparturesApp();
        var settings = app.GetSettings().ToList();

        Assert.Equal(new[] { "station", "maxServices", "platformFilter", "pageSeconds", "arrivalFormat", "showDestination", "dueThreshold", "showCallingPoints", "showPlatform" }, settings.Select(s => s.Key).ToArray());
        Assert.Equal(new[] { AppSettingType.String, AppSettingType.Integer, AppSettingType.String, AppSettingType.Integer, AppSettingType.Select, AppSettingType.Boolean, AppSettingType.Integer, AppSettingType.Boolean, AppSettingType.Boolean }, settings.Select(s => s.Type).ToArray());
        Assert.Equal(1, settings.Single(s => s.Key == "maxServices").MinValue);
        Assert.Equal(5, settings.Single(s => s.Key == "maxServices").MaxValue);
        Assert.Equal("rail-departures", app.Id);
        Assert.Equal("HVB", app.Station);
    }

    // ---- source -------------------------------------------------------------------------------------------------

    [Fact]
    public void HardcodedSource_GeneratesTimesRelativeToTheClock()
    {
        var a = HardcodedRailSource.Generate(Start);
        var later = HardcodedRailSource.Generate(Start.AddMinutes(30));

        Assert.NotEmpty(a);
        Assert.All(a, s => Assert.InRange(s.DepartsAt, Start.AddMinutes(-2), Start.AddHours(3)));
        Assert.Equal(a.OrderBy(s => s.DepartsAt).Select(s => s.Key), a.Select(s => s.Key));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 13, 52, 0, TimeSpan.Zero), a.First(s => s.DepartsAt > Start).DepartsAt);
        Assert.DoesNotContain(later, s => s.DepartsAt < Start.AddMinutes(28));   // departed trains drop out as time passes
        Assert.Contains(later, s => s.DepartsAt > Start.AddHours(3));            // and new ones appear
    }

    [Fact]
    public void HardcodedSource_HasDelayedAndCancelledServices_WithConsistentFields()
    {
        var day = Enumerable.Range(0, 24).SelectMany(h => HardcodedRailSource.Generate(new DateTimeOffset(2026, 1, 2, h, 0, 0, TimeSpan.Zero))).DistinctBy(s => s.Key).ToList();

        var delayed = day.Where(s => s.Status == RailStatus.Delayed).ToList();
        Assert.NotEmpty(delayed);
        Assert.All(delayed, s => { Assert.NotNull(s.ExpectedTime); Assert.True(s.ExpectedTime > s.ScheduledTime); Assert.Equal(s.ExpectedTime, s.DepartsAt); });
        Assert.NotEmpty(day.Where(s => s.Status == RailStatus.Cancelled));
        Assert.All(day.Where(s => s.Status == RailStatus.OnTime), s => Assert.Equal(s.ScheduledTime, s.DepartsAt));

        // The first board shows both kinds at the default test time.
        var board = HardcodedRailSource.Generate(Start).Where(s => s.DepartsAt > Start).Take(5).ToList();
        Assert.Contains(board, s => s.Status == RailStatus.Delayed);
        Assert.Contains(board, s => s.Status == RailStatus.Cancelled);
    }

    [Fact]
    public void HardcodedSource_FlagsExactlyOneLastTrainPerDay_NeverCancelled()
    {
        var last = HardcodedRailSource.Generate(new DateTimeOffset(2026, 1, 2, 23, 0, 0, TimeSpan.Zero)).Where(s => s.IsLastTrain).ToList();

        var only = Assert.Single(last);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 23, 52, 0, TimeSpan.Zero), only.ScheduledTime);
        Assert.Equal(RailStatus.OnTime, only.Status);
        Assert.Empty(HardcodedRailSource.Generate(Start.AddHours(-6)).Where(s => s.IsLastTrain));
    }

    [Fact]
    public async Task HardcodedSource_ReadsItsClockOnEachCall_AndNamesTheSampleStation()
    {
        var now = Start;
        IRailDepartureSource source = new HardcodedRailSource(() => now);

        var first = await source.GetDeparturesAsync("HVB", CancellationToken.None);
        now = now.AddHours(1);
        var second = await source.GetDeparturesAsync("HVB", CancellationToken.None);

        Assert.NotEqual(first[0].Key, second[0].Key);
        Assert.Equal("Havenbridge", source.StationName("hvb"));
        Assert.Null(source.StationName("XXX"));
    }

    // ---- model --------------------------------------------------------------------------------------------------

    [Fact]
    public void Model_MapsStatusesToText_AndDropsDepartedTrains()
    {
        var model = new RailBoardModel();
        var services = Disrupted(Start);
        model.Refresh(Start, services, "", 5);

        Assert.Equal(["Exp 13:58", "Cancelled", "On time", "Exp 14:36"], model.Visible.Select(r => r.Status).ToArray());
        Assert.Equal("13:52", model.Hero[0].Time);   // the scheduled time stays, the expected time goes in the status
        Assert.Contains("Delayed", model.Hero[0].Info);
        Assert.Contains("cancelled", model.Visible[1].Info);

        model.Refresh(Start.AddMinutes(14), services, "", 5);   // the delayed 13:58 has gone
        Assert.Equal(3, model.Visible.Count);
        Assert.Equal("Brighton", model.Hero[0].Destination);
    }

    [Fact]
    public void Model_AppliesPlatformFilterAndMaximum_AndPages()
    {
        var model = new RailBoardModel();
        var services = Normal(Start);

        model.Refresh(Start, services, "1", 5);
        Assert.Equal(["Brighton", "Gatwick Airport"], model.Visible.Select(r => r.Destination).ToArray());

        model.Refresh(Start, services, " 2 , 3", 5);
        Assert.Equal(3, model.Visible.Count);

        model.Refresh(Start, services, "", 2);
        Assert.Equal(2, model.Visible.Count);
        Assert.Single(model.Pages);

        model.Refresh(Start, services, "", 5);
        Assert.Equal(5, model.Visible.Count);
        Assert.Equal(2, model.Pages.Count);   // hero, then three, then one
        Assert.Equal(3, model.Page(0).Count);
        Assert.Single(model.Page(1));
    }

    [Fact]
    public void Model_KeepsRowsAcrossPolls_AndFlagsTheLastTrain()
    {
        var model = new RailBoardModel();
        var last = Svc(Start, 20, "London Victoria", "2", last: true);
        model.Refresh(Start, [Svc(Start, 7, "London Bridge", "2"), last], "", 5);
        var row = model.Visible[0];
        int version = row.Version;

        model.Refresh(Start, [Svc(Start, 7, "London Bridge", "2"), last], "", 5);   // an identical poll
        Assert.Same(row, model.Visible[0]);
        Assert.Equal(version, row.Version);

        Assert.Equal(20 - 0.1167, model.LastTrainMinutes(Start)!.Value, 2);
        Assert.Null(model.LastTrainMinutes(Start.AddMinutes(21)));
        model.Refresh(Start, [Svc(Start, 7, "London Bridge", "2"), last with { Status = RailStatus.Cancelled }], "", 5);
        Assert.Null(model.LastTrainMinutes(Start));
    }

    // ---- rendering ----------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_MinutesBothAndHiddenColumns()
    {
        var (minutes, minutesStage, _) = Board(HardcodedRailSource.Generate(Start));
        minutes.ArrivalFormat = "Minutes";
        minutesStage.Step(33, 6);
        Golden(minutesStage, "rail_departures_minutes");

        var (both, bothStage, _) = Board(HardcodedRailSource.Generate(Start));
        both.ArrivalFormat = "Both";
        both.ShowPlatform = false;
        both.ShowCallingPoints = false;
        bothStage.Step(33, 6);
        Golden(bothStage, "rail_departures_both_no_platform_no_calling");

        var (nodest, nodestStage, _) = Board(HardcodedRailSource.Generate(Start));
        nodest.ShowDestination = false;
        nodestStage.Step(33, 6);
        Golden(nodestStage, "rail_departures_no_destination");
    }

    [Fact]
    public void Snapshots()
    {
        var (normal, normalStage, _) = Board(HardcodedRailSource.Generate(Start));
        Assert.Equal(5, normal.Model.Visible.Count);
        Golden(normalStage, "rail_departures_board");

        var (_, disrupted, _) = Board(Disrupted(Start));
        Golden(disrupted, "rail_departures_delayed_cancelled");

        var (_, none, _) = Board([]);
        Golden(none, "rail_departures_empty");
    }

    [Fact]
    public void LastTrain_PulsesWithinThirtyMinutes()
    {
        var evening = new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.Zero);
        var services = new[]
        {
            Svc(evening, 4, "London Bridge", "2"),
            Svc(evening, 12, "Brighton", "1"),
            Svc(evening, 22, "London Victoria", "2", last: true),
        };

        var (app, stage, clock) = Board(services, evening);
        Assert.True(app.LastTrainPill!.Visible);
        Golden(stage, "rail_departures_last_train");

        clock.Now = evening.AddMinutes(-20);   // 42 minutes away: no pill yet
        stage.Step(33, 3);
        Assert.False(app.LastTrainPill.Visible);
    }

    private sealed class FakeOverlays : LedMatrixOS.Core.Overlays.IOverlayService
    {
        public List<LedMatrixOS.Core.Overlays.IOverlay> Added { get; } = new();
        public List<string> Removed { get; } = new();
        public int Width => 256;
        public int Height => 64;
        public void Add(LedMatrixOS.Core.Overlays.IOverlay overlay) => Added.Add(overlay);
        public bool Remove(string id) { Removed.Add(id); return true; }
    }

    [Fact]
    public void LastTrain_RaisesOneToastWithinFifteenMinutes_AndRearmsOnlyForANewService()
    {
        Fonts.Load();
        var evening = new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.Zero);
        var clock = new FakeTime { Now = evening };
        var overlays = new FakeOverlays();
        var live = new MutableLive<RailService[]> { Value = [Svc(evening, 4, "London Bridge", "2"), Svc(evening, 22, "London Victoria", "2", last: true)] };
        var app = new RailDeparturesApp { Time = clock, OverlayService = overlays };
        app.UseData(live, "Havenbridge");
        var stage = new AppStage(app);

        stage.Step(33, 10);                       // 22 minutes away: nothing yet
        Assert.Empty(overlays.Added);

        clock.Now = evening.AddMinutes(8);        // 14 minutes away
        stage.Step(33, 5);
        var toast = Assert.Single(overlays.Added);
        Assert.Equal("toast", ((LedMatrixOS.Core.Overlays.OverlayBase)toast).Kind);
        Assert.Contains("London Victoria", ((LedMatrixOS.Core.Overlays.OverlayBase)toast).Text);

        clock.Now = evening.AddMinutes(10);       // still the same service: no repeat
        stage.Step(33, 5);
        Assert.Single(overlays.Added);

        live.Value = [Svc(evening, 22, "London Victoria", "2", last: true) with { IsLastTrain = false }, Svc(evening, 24, "Brighton", "1", last: true)];
        stage.Step(33, 5);                        // a different flagged service re-arms
        Assert.Equal(2, overlays.Added.Count);
    }

    [Fact]
    public void LastTrain_WithoutAnOverlayService_StillRenders()
    {
        var evening = new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.Zero);
        var (app, _, _) = Board([Svc(evening, 10, "London Victoria", "2", last: true)], evening);
        Assert.Null(app.OverlayService);
        Assert.True(app.LastTrainPill!.Visible);
    }

    [Fact]
    public async Task Deactivation_RemovesTheAppsOwnOverlays()
    {
        Fonts.Load();
        var evening = new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.Zero);
        var overlays = new FakeOverlays();
        var app = new RailDeparturesApp { Time = new FakeTime { Now = evening }, OverlayService = overlays };
        app.UseData(new MutableLive<RailService[]> { Value = [Svc(evening, 10, "London Victoria", "2", last: true)] }, "Havenbridge");
        new AppStage(app).Step(33, 5);
        var id = Assert.Single(overlays.Added).Id;

        await app.OnDeactivatedAsync(CancellationToken.None);
        Assert.Equal(new[] { id }, overlays.Removed);
    }

    [Fact]
    public void Pager_SlidesToTheSecondPage()
    {
        var (app, stage, _) = Board(Normal(Start), warmMs: 300);
        Assert.Equal(2, app.ServicePager!.PageCount);

        stage.Step(33, 6 * 30 + 40);
        Assert.Equal(1, app.ServicePager.PageIndex);
    }

    // ---- performance --------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage, _) = Board(HardcodedRailSource.Generate(Start));
        var run = stage.MeasureSteadyAllocation(windows: 12, warmFrames: 400, beginWindow: () => { int page = app.ServicePager!.PageIndex; return () => page == app.ServicePager.PageIndex && !app.ServicePager.IsTransitioning; });

        output.WriteLine($"rail departures: {run.MsPerFrame:F3} ms/frame, {run.Measured} steady windows");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");    }
}
