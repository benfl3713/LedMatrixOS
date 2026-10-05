using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Toys;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Xunit;

namespace LedMatrixOS.Tests;

public class ToysBouncingBallsTests
{
    private static BouncingBallsApp App(Action<BouncingBallsApp>? configure = null)
    {
        var app = new BouncingBallsApp();
        configure?.Invoke(app);
        return app;
    }

    [Fact]
    public void IdAndName_AreUnchanged_AndRegistered()
    {
        var app = new BouncingBallsApp();
        Assert.Equal("bouncing-balls", app.Id);
        Assert.Equal("Bouncing Balls", app.Name);
        Assert.Contains(typeof(BouncingBallsApp), BuiltInApps.GetAll());
    }

    [Theory]
    [InlineData(2500, "toys_balls_t2500")]
    [InlineData(9500, "toys_balls_t9500_tilted")]
    public void Balls_Snapshot(int ms, string name)
    {
        var frame = ToyRunner.RunTo(App(), ms);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Balls_NoTrails_WeightlessSnapshot()
    {
        var frame = ToyRunner.RunTo(App(a => { a.Trails = false; a.Gravity = 0; a.Palette = "candy"; a.Count = 14; }), 4000);
        SnapshotHelper.AssertMatchesSnapshot(frame, "toys_balls_weightless_candy");
    }

    [Fact]
    public void Lava_Snapshot()
    {
        var frame = ToyRunner.RunTo(App(a => { a.Style = "lava"; a.Palette = "sunset"; }), 12000);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, "toys_balls_lava");
    }

    [Fact]
    public void IsDeterministic()
    {
        Assert.True(Stage.Same(ToyRunner.RunTo(App(), 7000), ToyRunner.RunTo(App(), 7000)));
        Assert.True(Stage.Same(ToyRunner.RunTo(App(a => a.Style = "lava"), 5000), ToyRunner.RunTo(App(a => a.Style = "lava"), 5000)));
    }

    [Fact]
    public void Animates()
    {
        var r = new ToyRunner(App());
        r.Advance(2000);
        var a = r.Snapshot();
        r.Advance(300);
        Assert.False(Stage.Same(a, r.Snapshot()));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(50)]
    public void Physics_KeepsBallsInBounds_AndEnergySane(int stepMs)
    {
        var app = App(a => a.Count = 24);
        var r = new ToyRunner(app, stepMs);
        r.Advance(100); // build
        var field = app.Field!;
        float minMean = float.MaxValue;
        r.Advance(40000, () =>
        {
            Assert.True(field.AllInBounds(), "ball left the arena");
            Assert.True(field.MaxSpeed() < 900f, "speed blew up: " + field.MaxSpeed());
        });
        r.Advance(2000, () => minMean = Math.Min(minMean, field.MeanSpeed()));
        Assert.True(field.Collisions > 20, "balls never collided: " + field.Collisions);
        Assert.True(field.WallHits > 20);
        Assert.True(field.MeanSpeed() > 0f);
    }

    [Fact]
    public void Gravity_DoesNotLetTheSceneDieOut()
    {
        var app = App();
        var r = new ToyRunner(app);
        var field = app.Field;
        r.Advance(100);
        field = app.Field!;
        int lowSamples = 0;
        r.Advance(60000, () => { if (r.Time.TotalSeconds > 5 && field.MeanSpeed() < 15f) lowSamples++; });
        // Quiet moments are fine, but the floor thump keeps pulling it back to life.
        Assert.True(lowSamples < 60000 / 16 / 3, "scene was dead for too long: " + lowSamples);
    }

    [Fact]
    public void Lava_DiffersFromBalls_AndStaysLit()
    {
        var balls = ToyRunner.RunTo(App(), 3000);
        var lava = ToyRunner.RunTo(App(a => a.Style = "lava"), 3000);
        Assert.False(Stage.Same(balls, lava));
        int lit = 0;
        foreach (var p in lava.GetPixelsSpan()) if (p.R + p.G + p.B > 150) lit++;
        Assert.InRange(lit, 300, 12000);
    }

    [Fact]
    public void Migration_OldLowercaseOptionsAreAcceptedAndCanonicalised()
    {
        var app = App();
        app.UpdateSetting("style", "balls");
        app.UpdateSetting("palette", "sunset");
        Assert.Equal("Balls", app.Style);
        Assert.Equal("Sunset", app.Palette);
        app.UpdateSetting("palette", "Mono"); // new shared palettes
        app.UpdateSetting("style", "LAVA");
        Assert.Equal("Mono", app.Palette);
        Assert.Equal("Lava", app.Style);
        var r = new ToyRunner(app);
        r.Advance(500);
        Assert.Equal("lava", app.Field!.Style);
        Assert.Equal("Mono", app.Field.PaletteName);
        var keys = app.GetSettings().Select(s => s.Key).ToHashSet();
        Assert.Contains("style", keys);
        Assert.Contains("palette", keys);
    }

    [Fact]
    public void Settings_RoundTrip_WithJsonElements()
    {
        var app = App();
        static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
        app.UpdateSetting("style", J("\"lava\""));
        app.UpdateSetting("count", J("18"));
        app.UpdateSetting("gravity", J("\"25\""));
        app.UpdateSetting("trails", J("false"));
        app.UpdateSetting("palette", J("\"ocean\""));
        Assert.Equal("Lava", app.Style); // an old lowercase value selects the Title Case option
        Assert.Equal(18, app.Count);
        Assert.Equal(25, app.Gravity);
        Assert.False(app.Trails);
        Assert.Equal("Ocean", app.Palette);

        var settings = app.GetSettings().ToDictionary(s => s.Key);
        Assert.Equal(5, settings.Count);
        Assert.Equal(AppSettingType.Select, settings["style"].Type);
        Assert.Equal(AppSettingType.Boolean, settings["trails"].Type);

        app.UpdateSetting("style", J("\"nonsense\"")); // not an option: ignored
        Assert.Equal("Lava", app.Style);
        app.UpdateSetting("count", J("500"));
        Assert.Equal(BouncingBallsField.MaxBalls, app.Count);
    }

    [Fact]
    public void SettingsApplyLive()
    {
        var app = App();
        var r = new ToyRunner(app);
        r.Advance(2500);
        var before = r.Snapshot();
        app.UpdateSetting("style", "lava");
        r.Advance(500);
        Assert.False(Stage.Same(before, r.Snapshot()));
        app.UpdateSetting("style", "balls");
        app.UpdateSetting("count", 6);
        r.Advance(2000);
        Assert.Equal(6, app.Field!.ActiveBalls);
    }

    [Theory]
    [InlineData("balls")]
    [InlineData("lava")]
    public void SteadyState_AllocatesNothingPerFrame(string style)
    {
        var app = App(a => a.Style = style);
        var r = new ToyRunner(app);
        r.Advance(3000);
        void Frame() { r.Advance(16); r.Render(); }
        for (int i = 0; i < 100; i++) Frame();
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Frame();
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.True(windows.Min() == 0, "allocated bytes per 100-frame window: " + string.Join(", ", windows));
    }
}
