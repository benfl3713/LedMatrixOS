using LedMatrixOS.Apps;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class LifeAppTests(ITestOutputHelper output)
{
    private static (LifeApp App, AppStage Stage) Make(string rule = LifeApp.Conway, string palette = "Neon", int cell = 2, int seed = 7)
    {
        Fonts.Load();
        var app = new LifeApp { Rule = rule, Palette = palette, CellSize = cell, Seed = seed };
        var stage = new AppStage(app);
        stage.Step(33, 1);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    private static bool Same(LedMatrixOS.Core.FrameBuffer a, LedMatrixOS.Core.FrameBuffer b) => a.GetPixelsSpan().SequenceEqual(b.GetPixelsSpan());

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new LifeApp();
        Assert.Equal("life", app.Id);
        Assert.Equal(new[] { "rule", "palette", "speed", "cellSize" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Contains(typeof(LifeApp), BuiltInApps.GetAll());
    }

    [Theory]
    [InlineData(LifeApp.Conway, "life_conway")]
    [InlineData(LifeApp.HighLife, "life_highlife")]
    [InlineData(LifeApp.LangtonsAnt, "life_ant")]
    [InlineData(LifeApp.Wireworld, "life_wireworld")]
    public void Golden_Rules(string rule, string name)
    {
        var (_, stage) = Make(rule);
        stage.Step(33, 90);
        Golden(stage, name);
    }

    [Fact]
    public void Golden_FirePaletteSmallCells()
    {
        var (_, stage) = Make(palette: "Fire", cell: 1);
        stage.Step(33, 60);
        Golden(stage, "life_fire_cell1");
    }

    [Fact]
    public void SameSeed_IsDeterministic()
    {
        var (_, a) = Make(seed: 3);
        var (_, b) = Make(seed: 3);
        a.Step(33, 50); b.Step(33, 50);
        Assert.True(Same(a.Snapshot(), b.Snapshot()));
    }

    [Fact]
    public void Blinker_Oscillates()
    {
        var (app, stage) = Make(cell: 1);
        app.SetPattern("###");
        var before = stage.Snapshot();
        stage.Step(40, 1);   // exactly one generation at speed 5 (18 gens/s -> not quite) so step until it changes
        for (int i = 0; i < 10 && Same(before, stage.Snapshot()); i++) stage.Step(40);
        Assert.False(Same(before, stage.Snapshot()));
        Assert.Equal(3, app.Population);
        Assert.Equal(0, app.Reseeds);
    }

    [Fact]
    public void StillLife_ReseedsWhenStagnant()
    {
        var (app, stage) = Make(cell: 1);
        app.SetPattern("##", "##");
        stage.Step(33, 120);
        Assert.True(app.Reseeds >= 1);
        Assert.True(app.Population > 100);
    }

    [Fact]
    public void EmptyGrid_Reseeds()
    {
        var (app, stage) = Make(cell: 1);
        app.SetPattern();
        stage.Step(33, 10);
        Assert.True(app.Reseeds >= 1);
        Assert.True(app.Population > 100);
    }

    [Fact]
    public void ChangingRule_ReseedsWithoutCounting()
    {
        var (app, stage) = Make();
        app.UpdateSetting("rule", LifeApp.Wireworld);
        stage.Step(33, 2);
        Assert.Equal(0, app.Reseeds);
        Assert.True(app.Generation < 5);
    }

    [Fact]
    public void Wireworld_ElectronCirculatesAroundALoop()
    {
        var (app, stage) = Make(LifeApp.Wireworld, cell: 1);
        app.SetPattern("#xo###", "#....#", "#....#", "#....#", "######");
        Assert.Equal(1, app.Population);
        stage.Step(33, 8);
        Assert.Equal(1, app.Population);
        Assert.Equal(0, app.Reseeds);
    }

    [Theory]
    [InlineData(LifeApp.Conway)]
    [InlineData(LifeApp.LangtonsAnt)]
    [InlineData(LifeApp.Wireworld)]
    public void SteadyState_DoesNotAllocate(string rule)
    {
        var (_, stage) = Make(rule);
        var run = stage.MeasureSteadyAllocation(windows: 6, warmFrames: 200);
        output.WriteLine($"life {rule}: {run.MsPerFrame:F3} ms/frame, least {run.Least} bytes");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }
}

public class PomodoroAppTests(ITestOutputHelper output)
{
    private sealed class FakeOverlays : IOverlayService
    {
        public List<IOverlay> Added { get; } = new();
        public int Width => 256;
        public int Height => 64;
        public void Add(IOverlay overlay) => Added.Add(overlay);
        public bool Remove(string id) => true;
    }

    private static (PomodoroApp App, AppStage Stage, FakeTime Clock, FakeOverlays Overlays) Make(Action<PomodoroApp>? configure = null)
    {
        Fonts.Load();
        var clock = new FakeTime();
        var overlays = new FakeOverlays();
        var app = new PomodoroApp { Time = clock, OverlayService = overlays };
        configure?.Invoke(app);
        var stage = new AppStage(app);
        stage.Step(33, 2);
        return (app, stage, clock, overlays);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new PomodoroApp();
        Assert.Equal("pomodoro", app.Id);
        Assert.Equal(new[] { "focusMinutes", "shortBreakMinutes", "longBreakMinutes", "sessionsBeforeLongBreak", "notify" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Contains(typeof(PomodoroApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void StartsInFocus_AndCountsDown()
    {
        var (app, stage, clock, overlays) = Make();
        Assert.Equal(PomodoroPhase.Focus, app.Phase);
        Assert.Equal(25 * 60, app.RemainingSeconds, 1);
        clock.Now += TimeSpan.FromMinutes(5);
        stage.Step(33, 2);
        Assert.Equal(20 * 60, app.RemainingSeconds, 1);
        Assert.Empty(overlays.Added);
    }

    [Fact]
    public void FullCycle_WithOneToastPerPhase()
    {
        var (app, stage, clock, overlays) = Make(a => { a.FocusMinutes = 1; a.ShortBreakMinutes = 1; a.LongBreakMinutes = 2; a.SessionsBeforeLongBreak = 2; });
        var seen = new List<PomodoroPhase>();
        for (int i = 0; i < 6; i++)
        {
            clock.Now += TimeSpan.FromSeconds(61 + (seen.Count == 3 ? 60 : 0));
            stage.Step(33, 2);
            seen.Add(app.Phase);
        }
        Assert.Equal(new[] { PomodoroPhase.ShortBreak, PomodoroPhase.Focus, PomodoroPhase.LongBreak, PomodoroPhase.Focus, PomodoroPhase.ShortBreak, PomodoroPhase.Focus }, seen);
        Assert.Equal(6, overlays.Added.Count);
        Assert.Equal("Short break", ((OverlayBase)overlays.Added[0]).Text);
        Assert.Equal("Long break", ((OverlayBase)overlays.Added[2]).Text);
    }

    [Fact]
    public void Notify_Alert_RaisesAlert_AndOffIsSilent()
    {
        var (_, stage, clock, overlays) = Make(a => { a.FocusMinutes = 1; a.Notify = "Alert"; });
        clock.Now += TimeSpan.FromSeconds(61);
        stage.Step(33, 2);
        var alert = Assert.Single(overlays.Added);
        Assert.NotEqual("toast", ((OverlayBase)alert).Kind);

        var (_, stage2, clock2, quiet) = Make(a => { a.FocusMinutes = 1; a.Notify = "Off"; });
        clock2.Now += TimeSpan.FromSeconds(61);
        stage2.Step(33, 2);
        Assert.Empty(quiet.Added);
    }

    [Fact]
    public void LongSuspend_DoesNotSkipPhases()
    {
        var (app, stage, clock, _) = Make();
        clock.Now += TimeSpan.FromHours(5);
        stage.Step(33, 3);
        Assert.Equal(PomodoroPhase.ShortBreak, app.Phase);
    }

    [Fact]
    public void Golden_Focus()
    {
        var (_, stage, clock, _) = Make();
        clock.Now += TimeSpan.FromSeconds(7 * 60 + 12);
        stage.Step(33, 2);
        Golden(stage, "pomodoro_focus");
    }

    [Fact]
    public void Golden_ShortBreak_AndLongBreak()
    {
        var (app, stage, clock, _) = Make();
        clock.Now += TimeSpan.FromMinutes(25.5);
        stage.Step(33, 2);
        Assert.Equal(PomodoroPhase.ShortBreak, app.Phase);
        Golden(stage, "pomodoro_short_break");

        var (app2, stage2, clock2, _) = Make(a => { a.SessionsBeforeLongBreak = 2; a.FocusMinutes = 1; a.ShortBreakMinutes = 1; });
        clock2.Now += TimeSpan.FromSeconds(61);
        stage2.Step(33, 2);
        clock2.Now += TimeSpan.FromSeconds(60);
        stage2.Step(33, 2);
        clock2.Now += TimeSpan.FromSeconds(61);
        stage2.Step(33, 2);
        Assert.Equal(PomodoroPhase.LongBreak, app2.Phase);
        Golden(stage2, "pomodoro_long_break");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage, _, _) = Make();
        var run = stage.MeasureSteadyAllocation(windows: 8);
        output.WriteLine($"pomodoro: {run.MsPerFrame:F3} ms/frame, least {run.Least} bytes");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }
}
