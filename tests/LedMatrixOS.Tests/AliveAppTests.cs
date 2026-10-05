using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class AliveAppTests(ITestOutputHelper output)
{
    private static (AliveApp App, AppStage Stage) Make(string sim, string palette = "Neon", string variant = "Auto", int seed = 7, Action<AliveApp>? configure = null)
    {
        var app = new AliveApp { Simulation = sim, Palette = palette, Variant = variant, Seed = seed };
        configure?.Invoke(app);
        var stage = new AppStage(app);
        stage.Step(33, 1);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        Assert.False(SnapshotHelper.IsBlank(frame), $"{name} is blank");
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    private static bool Same(FrameBuffer a, FrameBuffer b) => a.GetPixelsSpan().SequenceEqual(b.GetPixelsSpan());

    private static int Lit(FrameBuffer f)
    {
        int n = 0;
        foreach (var p in f.GetPixelsSpan()) if (p.R + p.G + p.B > 24) n++;
        return n;
    }

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new AliveApp();
        Assert.Equal("alive", app.Id);
        Assert.Equal(
            new[] { "simulation", "cycleSeconds", "palette", "speed", "population", "trail", "reseedMinutes", "predator", "variant" },
            app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Contains(typeof(AliveApp), BuiltInApps.GetAll());
    }

    // ---- goldens --------------------------------------------------------------

    [Theory]
    [InlineData("Neon", 150, "alive_boids_neon")]
    [InlineData("Fire", 240, "alive_boids_fire")]
    public void Golden_Boids(string palette, int frames, string name)
    {
        var (_, stage) = Make(AliveApp.BoidsName, palette);
        stage.Step(33, frames);
        Golden(stage, name);
    }

    [Theory]
    [InlineData("Coral", "Neon", "alive_rd_coral")]
    [InlineData("Spots", "Ocean", "alive_rd_spots")]
    [InlineData("Worms", "Forest", "alive_rd_worms")]
    [InlineData("Mitosis", "Rainbow", "alive_rd_mitosis")]
    public void Golden_ReactionDiffusion(string variant, string palette, string name)
    {
        var (_, stage) = Make(AliveApp.ReactionName, palette, variant);
        stage.Step(33, 260);
        Golden(stage, name);
    }

    [Theory]
    [InlineData("Rain", "Neon", "alive_sand_rain", 420)]
    [InlineData("Hourglass", "Forest", "alive_sand_hourglass", 150)]
    [InlineData("Garden", "Ocean", "alive_sand_garden", 600)]
    [InlineData("Cascade", "Rainbow", "alive_sand_cascade", 420)]
    public void Golden_FallingSand(string scene, string palette, string name, int frames)
    {
        var (_, stage) = Make(AliveApp.SandName, palette, scene);
        stage.Step(33, frames);
        Golden(stage, name);
    }

    [Theory]
    [InlineData("Neon", "alive_lenia_neon")]
    [InlineData("Ocean", "alive_lenia_ocean")]
    public void Golden_Lenia(string palette, string name)
    {
        var (_, stage) = Make(AliveApp.LeniaName, palette);
        stage.Step(33, 260);
        Golden(stage, name);
    }

    [Fact]
    public void Golden_Mono_FireAndForest_Palettes_AreNotBlank()
    {
        foreach (var palette in new[] { "Mono", "Fire", "Forest", "Ocean", "Rainbow", "Neon" })
            foreach (var sim in new[] { AliveApp.BoidsName, AliveApp.ReactionName, AliveApp.SandName, AliveApp.LeniaName })
            {
                var (_, stage) = Make(sim, palette);
                stage.Step(33, 120);
                Assert.True(Lit(stage.Snapshot()) > 40, $"{sim}/{palette} nearly blank");
            }
    }

    // ---- behaviour --------------------------------------------------------------

    [Theory]
    [InlineData(AliveApp.BoidsName)]
    [InlineData(AliveApp.ReactionName)]
    [InlineData(AliveApp.SandName)]
    [InlineData(AliveApp.LeniaName)]
    public void SameSeed_IsDeterministic(string sim)
    {
        var (_, a) = Make(sim, seed: 3);
        var (_, b) = Make(sim, seed: 3);
        var (_, other) = Make(sim, seed: 4);
        a.Step(33, 90); b.Step(33, 90); other.Step(33, 90);
        Assert.True(Same(a.Snapshot(), b.Snapshot()));
        Assert.False(Same(a.Snapshot(), other.Snapshot()));
    }

    [Theory]
    [InlineData(AliveApp.BoidsName)]
    [InlineData(AliveApp.ReactionName)]
    [InlineData(AliveApp.SandName)]
    [InlineData(AliveApp.LeniaName)]
    public void EmptyWorld_ReseedsItself(string sim)
    {
        var (app, stage) = Make(sim);
        stage.Step(33, 30);
        Assert.Equal(0, app.Reseeds);
        app.Wipe();
        for (int i = 0; i < 400 && app.Reseeds == 0; i++) stage.Step(33);
        Assert.True(app.Reseeds >= 1, "never reseeded");
        stage.Step(33, 20);
        Assert.True(Lit(stage.Snapshot()) > 40, "still dead after reseed");
    }

    [Theory]
    [InlineData(AliveApp.BoidsName)]
    [InlineData(AliveApp.ReactionName)]
    [InlineData(AliveApp.SandName)]
    [InlineData(AliveApp.LeniaName)]
    public void ReseedTimer_StartsAFreshRun(string sim)
    {
        var (app, stage) = Make(sim, configure: a => a.ReseedMinutes = 1);
        stage.Step(33, 1700);    // 56 s, then a bit more
        stage.Step(33, 200);
        Assert.True(app.Reseeds >= 1);
    }

    [Fact]
    public void ReseedMinutesZero_DisablesTheTimer()
    {
        var (app, stage) = Make(AliveApp.BoidsName, configure: a => a.ReseedMinutes = 0);
        stage.Step(50, 2400);    // 2 minutes
        Assert.Equal(0, app.Reseeds);
    }

    [Fact]
    public void ChangingSettings_ReseedsWithoutCounting()
    {
        var (app, stage) = Make(AliveApp.BoidsName);
        stage.Step(33, 10);
        app.UpdateSetting("simulation", AliveApp.LeniaName);
        stage.Step(33, 2);
        Assert.Equal(AliveApp.LeniaName, app.CurrentSimulation);
        Assert.Equal(0, app.Reseeds);
        Assert.True(app.Ticks < 10);
    }

    [Fact]
    public void AutoCycle_VisitsEverySimulation()
    {
        var (app, stage) = Make(AliveApp.AutoCycle, configure: a => a.CycleSeconds = 5);
        var seen = new HashSet<string>();
        for (int i = 0; i < 900; i++)
        {
            stage.Step(33);
            seen.Add(app.CurrentSimulation);
        }
        Assert.Equal(4, seen.Count);
        Assert.Contains(AliveApp.ReactionName, seen);
        Assert.False(SnapshotHelper.IsBlank(stage.Snapshot()));
    }

    [Fact]
    public void FallingSand_ScenesRunAndDifferByScene()
    {
        var frames = new List<FrameBuffer>();
        foreach (var scene in new[] { "Rain", "Hourglass", "Garden", "Cascade" })
        {
            var (_, stage) = Make(AliveApp.SandName, variant: scene);
            stage.Step(33, 200);
            frames.Add(stage.Snapshot());
        }
        for (int i = 0; i < frames.Count; i++)
            for (int j = i + 1; j < frames.Count; j++) Assert.False(Same(frames[i], frames[j]));
    }

    [Fact]
    public void Boids_FlockMoves_AndPredatorToggleChangesPicture()
    {
        var (_, withHawk) = Make(AliveApp.BoidsName, seed: 11);
        var (_, noHawk) = Make(AliveApp.BoidsName, seed: 11, configure: a => a.Predator = false);
        withHawk.Step(33, 120); noHawk.Step(33, 120);
        Assert.False(Same(withHawk.Snapshot(), noHawk.Snapshot()));
    }

    // ---- cost -------------------------------------------------------------------

    [Theory]
    [InlineData(AliveApp.BoidsName, "Auto")]
    [InlineData(AliveApp.ReactionName, "Coral")]
    [InlineData(AliveApp.ReactionName, "Worms")]
    [InlineData(AliveApp.SandName, "Rain")]
    [InlineData(AliveApp.SandName, "Garden")]
    [InlineData(AliveApp.SandName, "Cascade")]
    [InlineData(AliveApp.LeniaName, "Auto")]
    [InlineData(AliveApp.AutoCycle, "Auto")]
    public void SteadyState_DoesNotAllocate(string sim, string variant)
    {
        var (_, stage) = Make(sim, variant: variant, configure: a => { a.Speed = 10; a.Population = 10; a.CycleSeconds = 5; });
        var run = stage.MeasureSteadyAllocation(windows: 6, warmFrames: 200);
        output.WriteLine($"alive {sim}/{variant}: {run.MsPerFrame:F3} ms/frame, least {run.Least} bytes");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
        Assert.True(run.MsPerFrame < 12, $"{run.MsPerFrame} ms/frame");
    }
}
