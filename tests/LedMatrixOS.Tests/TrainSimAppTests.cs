using System.Net;
using System.Net.WebSockets;
using System.Text;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.TrainSim;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class TrainSimAppTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Clock = new(2026, 11, 10, 20, 44, 21, TimeSpan.Zero);

    private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tsw-telemetry.json"));

    // ---- snapshots used by the goldens -----------------------------------------------------------------------------------

    private static TrainSimSnapshot Live(double speedMs = 28.6, double limitMs = 33.528, double power = 46, double brake = 0) => new()
    {
        Reachable = true,
        Address = "192.168.1.20:31280",
        Status = BridgeStatus.Connected,
        GameName = "Demo route",
        LocoClass = "RVM_DEMO_Class805_C",
        FormationLength = 5,
        SpeedMs = speedMs,
        PowerHandle = power,
        TrainBrakeHandle = brake,
        SpeedLimitMs = limitMs,
        Gradient = 0.8,
        Signals = [new TsSignal("Approach_1", 202.7), new TsSignal("Stop", 1702.7)],
        Limits = [new TsLimit(17.88, 1290), new TsLimit(26.8, 4900)],
        Stops = [new TsStop("Ashby Road", "2", 502.7, true), new TsStop("Millbrook", "1", 2602.7, true)],
        ServiceName = "1D99",
        WorldTime = Clock,
    };

    private static TrainSimSnapshot Dwelling() => Live(0, 17.88, 0, 0) with
    {
        Signals = [new TsSignal("Clear", 640)],
        Limits = [],
        Stops = [new TsStop("Millbrook", "1", 18, true)],
        WaitRemainingSeconds = 42,
        ObjectiveKind = "Platform",
        Gradient = 0,
    };

    private static (TrainSimApp App, AppStage Stage, DemoTrainSimSource Source) Board(TrainSimSnapshot snapshot, Action<TrainSimApp>? configure = null)
    {
        Fonts.Load();
        var source = new DemoTrainSimSource(snapshot);
        var app = new TrainSimApp(source) { Time = new FakeTime { Now = Clock } };
        configure?.Invoke(app);
        app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None).GetAwaiter().GetResult();
        var stage = new AppStage(app);
        stage.Step(33, 20);
        return (app, stage, source);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- parser -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Fixture_ParsesToTheExpectedValues()
    {
        var s = TswTelemetryState.ParseTelemetry(Fixture(), "pc:31280");

        Assert.True(s.Reachable);
        Assert.Equal(BridgeStatus.Connected, s.Status);
        Assert.Equal("Demo route", s.GameName);
        Assert.Equal("RVM_DEMO_Class805_C", s.LocoClass);
        Assert.Equal(5, s.FormationLength);
        Assert.Equal(22.29, s.SpeedMs!.Value, 2);
        Assert.Equal(0.2307, s.AccelerationMs2!.Value, 3);
        Assert.Equal(46, s.PowerHandle);
        Assert.Equal(0, s.TrainBrakeHandle);
        Assert.Equal(33.528, s.SpeedLimitMs!.Value, 3);
        Assert.Equal(0.8, s.Gradient);
        Assert.Equal(4, s.Signals.Count);
        Assert.Equal("Approach_1", s.Signals[0].Aspect);
        Assert.Equal(202.67, s.Signals[0].DistanceMetres, 2);
        Assert.Equal(2, s.Limits.Count);
        Assert.Equal(13.4112, s.Limits[0].LimitMs, 4);
        Assert.Equal("1D99", s.ServiceName);
        Assert.Equal(4, s.Stops.Count);   // capped
        Assert.Equal(("Ashby Road", "2", true), (s.Stops[0].Name, s.Stops[0].Platform, s.Stops[0].Calling));
        Assert.Equal(0, s.GmtOffsetHours);
        Assert.Equal("20:44", TrainSimFormat.Clock(s.WorldTime));
        Assert.Null(s.WaitRemainingSeconds);
    }

    [Fact]
    public void Update_ReplacesOnlyTheSectionsItContains()
    {
        var state = new TswTelemetryState();
        var snapshot = $$"""{"type":"snapshot","seq":1,"t":1,"data":{{Fixture()}}}""";
        Assert.True(state.Apply(snapshot));
        Assert.True(state.Apply("""{"type":"update","seq":2,"t":2,"data":{"drive":{"speed":10.5,"powerHandle":-40},"objective":{"kind":"Platform","waitRemaining":31.2}}}"""));

        var s = state.Build("x", true, null);
        Assert.Equal(10.5, s.SpeedMs);
        Assert.Equal(-40, s.PowerHandle);
        Assert.Null(s.TrainBrakeHandle);                 // the drive section was replaced wholesale
        Assert.Equal("RVM_DEMO_Class805_C", s.LocoClass); // others untouched
        Assert.Equal(33.528, s.SpeedLimitMs!.Value, 3);
        Assert.Equal(31.2, s.WaitRemainingSeconds);
        Assert.Equal("Platform", s.ObjectiveKind);

        Assert.True(state.Apply("""{"type":"update","seq":3,"t":3,"data":{"objective":null}}"""));
        Assert.Null(state.Build("x", true, null).WaitRemainingSeconds);
    }

    [Fact]
    public void NewSnapshot_ReplacesEverything()
    {
        var state = new TswTelemetryState();
        state.Apply($$"""{"type":"snapshot","seq":1,"t":1,"data":{{Fixture()}}}""");
        state.Apply("""{"type":"snapshot","seq":2,"t":2,"data":{"connection":{"status":"waiting-for-game"}}}""");
        var s = state.Build("x", true, null);
        Assert.Equal(BridgeStatus.WaitingForGame, s.Status);
        Assert.Null(s.SpeedMs);
        Assert.Equal("", s.LocoClass);
    }

    [Theory]
    [InlineData("""{"type":"recorded","replace":true,"features":[]}""")]
    [InlineData("""{"type":"hello"}""")]
    [InlineData("""{"seq":1}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""{"type":"update","data":[1]}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void UnknownOrBrokenMessages_AreIgnored(string message)
    {
        var state = new TswTelemetryState();
        Assert.False(state.Apply(message));
        Assert.Equal(BridgeStatus.Unknown, state.Build("x", false, null).Status);
    }

    [Fact]
    public void NullsAndMissingSections_AreTolerated()
    {
        var state = new TswTelemetryState();
        Assert.True(state.Apply("""
            {"type":"snapshot","seq":1,"t":1,"data":{
              "connection":{"status":"connected"},"loco":null,"drive":{"speed":null,"powerHandle":"x"},
              "driverAid":{"speedLimit":null,"signals":[{"aspect":null,"distance":5},{"aspect":"Clear"},"junk",{"aspect":"Clear","distance":9}],"limits":[{"limit":null,"distance":1}],"gradient":null},
              "track":{"stops":[{"name":"A","distance":10},{"distance":3}]},"world":{"localTimeIso":"garbage"},"position":null}}
            """));
        var s = state.Build("x", true, null);

        Assert.Equal(BridgeStatus.Connected, s.Status);
        Assert.Equal("", s.LocoClass);
        Assert.Null(s.SpeedMs);
        Assert.Null(s.PowerHandle);
        Assert.Null(s.SpeedLimitMs);
        Assert.Single(s.Signals);
        Assert.Empty(s.Limits);
        var stop = Assert.Single(s.Stops);
        Assert.True(stop.Calling);
        Assert.Equal("", stop.Platform);
        Assert.Null(s.WorldTime);

        // and the app renders it without trouble
        var (_, stage, _) = Board(s);
        Assert.False(SnapshotHelper.IsBlank(stage.Snapshot()));
    }

    // ---- formatting -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(22.2904, true, 50)]
    [InlineData(22.2904, false, 80)]
    [InlineData(0, true, 0)]
    [InlineData(-1, true, 0)]
    [InlineData(33.528, true, 75)]
    public void Speed_ConvertsAndRounds(double ms, bool mph, int expected) => Assert.Equal(expected, TrainSimFormat.Speed(ms, mph));

    [Theory]
    [InlineData(202.7, true, "220y")]
    [InlineData(40, true, "45y")]
    [InlineData(1290, true, "0.8mi")]
    [InlineData(1900, true, "1.2mi")]
    [InlineData(850, false, "850m")]
    [InlineData(22, false, "20m")]
    [InlineData(1230, false, "1.2km")]
    [InlineData(-5, true, "0y")]
    public void Distance_UsesYardsMilesOrMetres(double metres, bool mph, string expected) => Assert.Equal(expected, TrainSimFormat.Distance(metres, mph));

    [Theory]
    [InlineData(42, "0:42")]
    [InlineData(41.2, "0:42")]
    [InlineData(125, "2:05")]
    [InlineData(-3, "0:00")]
    public void Countdown_FormatsMinutesAndSeconds(double seconds, string expected) => Assert.Equal(expected, TrainSimFormat.Countdown(seconds));

    [Theory]
    [InlineData("RVM_DEMO_Class805_C", "Class 805")]
    [InlineData("BP_Class_43_HST_C", "Class 43")]
    [InlineData("RVM_Class66_C", "Class 66")]
    [InlineData("RVM_Hst_C", "Hst")]
    [InlineData("RVM_BR_Pacer_C", "Pacer")]
    [InlineData("Intercity225", "Intercity225")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LocoName_IsPrettified(string? objectClass, string expected) => Assert.Equal(expected, TrainSimFormat.LocoName(objectClass));

    [Theory]
    [InlineData("Stop", SpeedAspect.Stop)]
    [InlineData("Danger", SpeedAspect.Stop)]
    [InlineData("Approach_1", SpeedAspect.Yellow)]
    [InlineData("Approach_2", SpeedAspect.DoubleYellow)]
    [InlineData("Clear", SpeedAspect.Green)]
    [InlineData("clear", SpeedAspect.Green)]
    [InlineData("Banana", SpeedAspect.Unknown)]
    [InlineData("", SpeedAspect.Unknown)]
    [InlineData(null, SpeedAspect.Unknown)]
    public void Aspect_MapsToTheLamp(string? aspect, SpeedAspect expected) => Assert.Equal(expected, TrainSimFormat.Aspect(aspect));

    [Fact]
    public void Classify_NormalThenAmberThenRed()
    {
        double limit = 33.528;   // 75 mph
        Assert.Equal(SpeedState.Normal, TrainSimFormat.Classify(limit - 5, limit));
        Assert.Equal(SpeedState.Normal, TrainSimFormat.Classify(limit, limit));
        Assert.Equal(SpeedState.Amber, TrainSimFormat.Classify(limit + 0.6, limit));
        Assert.Equal(SpeedState.Red, TrainSimFormat.Classify(limit + 1.5, limit));
        Assert.Equal(SpeedState.Normal, TrainSimFormat.Classify(80, null));
    }

    [Fact]
    public void Clock_ShowsTheRouteLocalWallClock()
    {
        // TSW's LocalTimeISO8601 is already route-local, e.g. "10:00:39.775+01:00" with GMTOffset 1.
        var json = """{"type":"snapshot","data":{"world":{"localTimeIso":"2026-11-10T10:00:39.775+01:00","gmtOffset":1}}}""";
        var state = new TswTelemetryState();
        state.Apply(json);
        var s = state.Build("x", true, null);
        Assert.Equal("10:00", TrainSimFormat.Clock(s.WorldTime));
        Assert.Equal("", TrainSimFormat.Clock(null));
    }

    [Fact]
    public void Model_PicksPowerBrakeAndTheNextStop()
    {
        var m = new TrainSimBoardModel();
        m.Refresh(Live(power: -38), true, true);
        Assert.Equal(0.38f, m.Brake, 3);
        Assert.Equal(0f, m.Power);

        m.Refresh(Live(power: 0, brake: 60), true, true);
        Assert.Equal(0.6f, m.Brake, 3);

        m.Refresh(Live(power: 80), true, true);
        Assert.Equal(0.8f, m.Power, 3);
        Assert.Equal("Ashby Road", m.StopName);
        Assert.Equal("P2", m.StopPlatform);
        Assert.Equal("550y", m.StopRight);
        Assert.Equal("40 in 0.8mi", m.NextLimitText);
        Assert.Equal(-1, m.NextLimitDirection);
        Assert.Equal("Class 805  1D99", m.LocoText);

        m.Refresh(Dwelling(), true, true);
        Assert.True(m.StopDwell);
        Assert.Equal("Wait 0:42", m.StopRight);
    }

    // ---- source ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("192.168.1.20:31280", "192.168.1.20:31280")]
    [InlineData("ws://pc:31280/ws", "pc:31280")]
    [InlineData("http://pc:31280/", "pc:31280")]
    [InlineData("  ", "localhost:31280")]
    public void NormaliseAddress_AcceptsCommonSpellings(string input, string expected) => Assert.Equal(expected, TswBridgeSource.NormaliseAddress(input));

    private static async Task<T> Eventually<T>(Func<T> read, Func<T, bool> done, int ms = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            var v = read();
            if (done(v)) return v;
            await Task.Delay(25);
        }
        return read();
    }

    private static async Task Send(WebSocket ws, string json) =>
        await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    public async Task BridgeSource_MergesMessagesAndReconnects()
    {
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        using var source = new TswBridgeSource();
        Assert.False(source.Current.Reachable);
        source.Start($"localhost:{port}");

        // first connection: snapshot, recorded noise, then an update
        var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("/ws", ctx.Request.Url!.AbsolutePath);
        var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
        await Send(ws, $$"""{"type":"snapshot","seq":1,"t":1,"data":{{Fixture()}}}""");
        await Send(ws, """{"type":"recorded","replace":true,"features":[]}""");
        var live = await Eventually(() => source.Current, s => s.Reachable);
        Assert.True(live.Reachable);
        Assert.Equal("Demo route", live.GameName);
        Assert.NotNull(live.LastMessageAt);

        await Send(ws, """{"type":"update","seq":2,"t":2,"data":{"drive":{"speed":12.5}}}""");
        var updated = await Eventually(() => source.Current, s => s.SpeedMs == 12.5);
        Assert.Equal(12.5, updated.SpeedMs);
        Assert.Equal("Demo route", updated.GameName);

        // the bridge goes away: offline, then back on the next accept
        ws.Abort();
        var offline = await Eventually(() => source.Current, s => !s.Reachable);
        Assert.False(offline.Reachable);
        Assert.Equal($"localhost:{port}", offline.Address);

        var ctx2 = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var ws2 = (await ctx2.AcceptWebSocketAsync(null)).WebSocket;
        await Send(ws2, """{"type":"snapshot","seq":1,"t":1,"data":{"connection":{"status":"waiting-for-game"}}}""");
        var again = await Eventually(() => source.Current, s => s.Reachable);
        Assert.Equal(BridgeStatus.WaitingForGame, again.Status);

        source.Stop();
        Assert.False(source.Current.Reachable);
        ws2.Abort();
    }

    [Fact]
    public async Task BridgeSource_UnreachableStaysOfflineWithItsAddress()
    {
        using var source = new TswBridgeSource();
        source.Start("localhost:1");
        await Task.Delay(300);
        Assert.False(source.Current.Reachable);
        Assert.Equal("localhost:1", source.Current.Address);
    }

    // ---- app lifecycle ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lifecycle_StartsOnActivationUsesConfigAndRestartsOnAddressChange()
    {
        var source = new DemoTrainSimSource(Live());
        var app = new TrainSimApp(source);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["TrainSim:BridgeAddress"] = "10.0.0.5:31280" }).Build();
        await app.OnActivatedAsync((64, 256), config, CancellationToken.None);
        Assert.Equal("10.0.0.5:31280", source.StartedAddress);

        app.UpdateSetting("bridgeAddress", "192.168.1.20:31280");
        Assert.Equal("192.168.1.20:31280", source.StartedAddress);
        Assert.Equal(2, source.Starts);

        await app.OnDeactivatedAsync(CancellationToken.None);
        Assert.Equal(1, source.Stops);
        app.UpdateSetting("bridgeAddress", "other:1");
        Assert.Equal(2, source.Starts);   // not while inactive
    }

    [Fact]
    public async Task Lifecycle_DefaultsToLocalhost()
    {
        var source = new DemoTrainSimSource(Live());
        var app = new TrainSimApp(source);
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);
        Assert.Equal("localhost:31280", source.StartedAddress);
    }

    [Fact]
    public void Settings_AreExposed()
    {
        var keys = new TrainSimApp(new DemoTrainSimSource(Live())).GetSettings().Select(s => s.Key).ToArray();
        Assert.Contains("bridgeAddress", keys);
        Assert.Contains("units", keys);
        Assert.Contains("showNextStop", keys);
    }

    // ---- goldens ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_DrivingNormally()
    {
        var (_, stage, _) = Board(Live());
        Golden(stage, "train_sim_driving");
    }

    [Fact]
    public void Golden_Overspeed()
    {
        var (_, stage, _) = Board(Live(speedMs: 37.5, power: -35) with { Signals = [new TsSignal("Stop", 1450)] });
        Golden(stage, "train_sim_overspeed");
    }

    [Fact]
    public void Golden_AtPlatformWithDwellCountdown()
    {
        var (_, stage, _) = Board(Dwelling());
        Golden(stage, "train_sim_dwell");
    }

    [Fact]
    public void Golden_KilometresPerHour()
    {
        var (_, stage, _) = Board(Live(speedMs: 31.9, power: 70) with { Signals = [new TsSignal("Approach_2", 480)] }, a => a.Units = "km/h");
        Golden(stage, "train_sim_kmh");
    }

    [Fact]
    public void Golden_BridgeOffline()
    {
        var (_, stage, _) = Board(TrainSimSnapshot.Offline("192.168.1.20:31280"));
        Golden(stage, "train_sim_offline");
    }

    [Fact]
    public void Golden_WaitingForGame()
    {
        var (_, stage, _) = Board(new TrainSimSnapshot { Reachable = true, Address = "192.168.1.20:31280", Status = BridgeStatus.WaitingForGame });
        Golden(stage, "train_sim_waiting");
    }

    [Theory]
    [InlineData(BridgeStatus.NoTrain)]
    [InlineData(BridgeStatus.NoKey)]
    [InlineData(BridgeStatus.Unknown)]
    public void StatusCards_RenderWithoutTrouble(BridgeStatus status)
    {
        var (_, stage, _) = Board(new TrainSimSnapshot { Reachable = true, Address = "pc:31280", Status = status, GameName = "Great Western" });
        Assert.False(SnapshotHelper.IsBlank(stage.Snapshot()));
    }

    [Fact]
    public void HideNextStop_RemovesTheStopRow()
    {
        var (_, withStop, _) = Board(Live());
        var (_, without, _) = Board(Live(), a => a.ShowNextStop = false);
        Assert.False(Stage.Same(withStop.Snapshot(), without.Snapshot()));
    }

    // ---- performance ------------------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage, _) = Board(Live());
        var m = stage.MeasureSteadyAllocation();
        output.WriteLine($"train sim: {m.MsPerFrame:F3} ms/frame, {m.Measured} steady windows");
        Assert.True(m.Measured >= 3);
        Assert.True(m.Least < 256, $"least allocation in a steady window: {m.Least} bytes");
    }

    [Fact]
    public void SteadyState_StatusCard_DoesNotAllocate()
    {
        var (_, stage, _) = Board(TrainSimSnapshot.Offline("pc:31280"));
        var m = stage.MeasureSteadyAllocation();
        Assert.True(m.Least < 256, $"allocated {m.Least} bytes");
    }
}
