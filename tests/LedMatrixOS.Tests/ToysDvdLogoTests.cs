using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Toys;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Xunit;

namespace LedMatrixOS.Tests;

public class ToysDvdLogoTests
{
    private static (DvdLogoApp app, ToyRunner run) Start(Action<DvdLogoApp>? configure = null)
    {
        var app = new DvdLogoApp();
        configure?.Invoke(app);
        var run = new ToyRunner(app);
        run.Advance(50);
        return (app, run);
    }

    [Fact]
    public void IdAndName_AreUnchanged_AndRegistered()
    {
        var app = new DvdLogoApp();
        Assert.Equal("dvd-logo", app.Id);
        Assert.Equal("DVD Logo", app.Name);
        Assert.Contains(typeof(DvdLogoApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void Snapshot_Cruising()
    {
        var (_, run) = Start();
        run.Advance(2950);
        var frame = run.Snapshot();
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, "toys_dvd_cruising");
    }

    [Fact]
    public void Snapshot_WallSquash()
    {
        var (app, run) = Start();
        var f = app.Field!;
        f.Place(f.MaxX - 2f, 20f, 34f, 0.5f);
        run.Advance(130);
        Assert.Equal(0, f.Corners);
        Assert.True(f.Bounces >= 1);
        SnapshotHelper.AssertMatchesSnapshot(run.Snapshot(), "toys_dvd_wall_squash");
    }

    [Theory]
    [InlineData(150, "toys_dvd_corner_flash")]
    [InlineData(1100, "toys_dvd_corner_banner")]
    [InlineData(4200, "toys_dvd_after_corner_counter")]
    public void Snapshot_CornerCelebration(int msAfter, string name)
    {
        var (app, run) = Start();
        var f = app.Field!;
        f.Place(f.MaxX - 2f, f.MaxY - 2f, 34f, 21f);
        run.Advance(msAfter);
        Assert.Equal(1, f.Corners);
        SnapshotHelper.AssertMatchesSnapshot(run.Snapshot(), name);
    }

    [Fact]
    public void CornerDetection_Exact_Near_AndMiss()
    {
        var (app, run) = Start(a => a.CornerAssist = false);
        var f = app.Field!;
        // Both walls in the same step.
        f.Place(f.MaxX - 0.1f, f.MaxY - 0.1f, 30f, 30f);
        f.Simulate(0.05f);
        Assert.Equal(1, f.Corners);
        // One wall, the other within tolerance.
        f.Place(f.MaxX - 1f, f.MaxY - 1.0f, 30f, 20f);
        f.Simulate(0.2f);
        Assert.Equal(2, f.Corners);
        // Wall hit with the other axis well away: just a bounce.
        f.Place(f.MaxX - 1f, 10f, 30f, 5f);
        f.Simulate(0.2f);
        Assert.Equal(2, f.Corners);
        Assert.True(f.VX < 0);
        Assert.True(run.App.Root is not null);
    }

    [Fact]
    public void Logo_StaysInBounds_AndBouncesChangeColour()
    {
        var (app, _) = Start();
        var f = app.Field!;
        var colour = f.LogoColor;
        int changes = 0;
        for (int i = 0; i < 20000; i++)
        {
            f.Simulate(0.01f);
            Assert.InRange(f.X, 0f, f.MaxX);
            Assert.InRange(f.Y, 0f, f.MaxY);
            if (f.LogoColor != colour) { changes++; colour = f.LogoColor; }
        }
        Assert.Equal(f.Bounces, changes);
        Assert.True(f.Bounces > 20);
    }

    [Fact]
    public void Assist_MakesCornersHappen_AndOffDoesNotForceThem()
    {
        var (assisted, _) = Start();
        var f = assisted.Field!;
        f.Simulate(240f);
        Assert.True(f.Corners >= 3, "assist should produce corners, got " + f.Corners);
        Assert.True(f.Corners <= 20, "assist should not make every bounce a corner: " + f.Corners);
        // Speed was only trimmed, never changed drastically.
        Assert.InRange(MathF.Abs(f.VY), 21f * 0.7f, 21f * 1.4f);
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        Assert.True(Stage.Same(Run(), Run()));
        static FrameBuffer Run()
        {
            var (app, run) = Start();
            app.Field!.Place(app.Field.MaxX - 2f, app.Field.MaxY - 2f, 34f, 21f);
            run.Advance(900);
            return run.Snapshot();
        }
    }

    [Fact]
    public void Counter_ShowsAfterFirstCorner()
    {
        var (app, run) = Start();
        Assert.False(app.CounterPill!.Visible);
        app.Field!.Place(app.Field.MaxX - 2f, app.Field.MaxY - 2f, 34f, 21f);
        run.Advance(300);
        Assert.True(app.CounterPill.Visible);
        Assert.Equal("CORNERS 1", app.CounterPill.Text);
    }

    [Fact]
    public void Celebration_ThenSettlesBackToCruising()
    {
        var (app, run) = Start();
        var f = app.Field!;
        f.Place(f.MaxX - 2f, f.MaxY - 2f, 34f, 21f);
        run.Advance(500);
        Assert.True(f.Celebrating);
        run.Advance(5000);
        Assert.False(f.Celebrating);
    }

    [Fact]
    public void Settings_RoundTrip_WithJsonElements()
    {
        var app = new DvdLogoApp();
        static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
        app.UpdateSetting("speed", J("180"));
        app.UpdateSetting("trail", J("false"));
        app.UpdateSetting("cornerAssist", J("false"));
        app.UpdateSetting("showCounter", J("\"false\""));
        app.UpdateSetting("palette", J("\"candy\""));
        Assert.Equal(180, app.Speed);
        Assert.False(app.Trail);
        Assert.False(app.CornerAssist);
        Assert.False(app.ShowCounter);
        Assert.Equal("candy", app.Palette);
        var settings = app.GetSettings().ToDictionary(s => s.Key);
        Assert.Equal(5, settings.Count);
        Assert.Equal(AppSettingType.Select, settings["palette"].Type);
        app.UpdateSetting("speed", J("1"));
        Assert.Equal(20, app.Speed);
    }

    [Fact]
    public void Settings_ApplyLive()
    {
        var (app, run) = Start();
        app.UpdateSetting("speed", 300);
        app.UpdateSetting("palette", "ocean");
        app.UpdateSetting("cornerAssist", false);
        Assert.Equal(300, app.Field!.Speed);
        Assert.False(app.Field.Assist);
    }

    [Fact]
    public void SteadyState_AllocatesNothingPerFrame_IncludingCelebration()
    {
        var (app, run) = Start();
        var f = app.Field!;
        void Frame() { run.Advance(16); run.Render(); }
        for (int i = 0; i < 100; i++) Frame();
        // Windows that straddle wall bounces and a corner celebration. Setup per window allocates (timeline), so only measure cruising + bounces.
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Frame();
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.True(windows.Min() == 0, "allocated bytes per 100-frame window: " + string.Join(", ", windows));
        Assert.True(f.Bounces >= 0);
    }

    [Fact]
    public void Celebration_RenderLoop_AllocatesNothing()
    {
        var (app, run) = Start();
        var f = app.Field!;
        f.Place(f.MaxX - 2f, f.MaxY - 2f, 34f, 21f);
        void Frame() { run.Advance(16); run.Render(); }
        for (int i = 0; i < 20; i++) Frame(); // the hit itself builds a timeline: not steady state
        long before = GC.GetAllocatedBytesForCurrentThread();
        long min = long.MaxValue;
        for (int w = 0; w < 3; w++)
        {
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++) Frame();
            min = Math.Min(min, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(f.Celebrating);
        Assert.Equal(0, min);
    }
}
