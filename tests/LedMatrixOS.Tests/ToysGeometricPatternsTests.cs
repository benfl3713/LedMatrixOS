using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Toys;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Xunit;

namespace LedMatrixOS.Tests;

public class ToysGeometricPatternsTests
{
    public static IEnumerable<object[]> Patterns() => GeometricField.PatternNames.Select(n => new object[] { n });

    private static GeometricPatternsApp App(string pattern = "auto", Action<GeometricPatternsApp>? configure = null)
    {
        var app = new GeometricPatternsApp { Pattern = pattern };
        configure?.Invoke(app);
        return app;
    }

    private static int Lit(FrameBuffer f)
    {
        int n = 0;
        foreach (var p in f.GetPixelsSpan()) if (p.R + p.G + p.B > 120) n++;
        return n;
    }

    [Fact]
    public void IdAndName_AreUnchanged_AndRegistered()
    {
        var app = new GeometricPatternsApp();
        Assert.Equal("geometric-patterns", app.Id);
        Assert.Equal("Geometric Patterns", app.Name);
        Assert.Contains(typeof(GeometricPatternsApp), BuiltInApps.GetAll());
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Pattern_Snapshot_NonBlank_Deterministic(string pattern)
    {
        var frame = ToyRunner.RunTo(App(pattern), 3300);
        Assert.False(SnapshotHelper.IsBlank(frame));
        Assert.InRange(Lit(frame), 150, 14000);
        Assert.True(Stage.Same(frame, ToyRunner.RunTo(App(pattern), 3300)));
        SnapshotHelper.AssertMatchesSnapshot(frame, "toys_geo_" + pattern);
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Pattern_Animates(string pattern)
    {
        var r = new ToyRunner(App(pattern));
        r.Advance(2000);
        var a = r.Snapshot();
        r.Advance(400);
        Assert.False(Stage.Same(a, r.Snapshot()));
    }

    [Fact]
    public void Speed_ScalesAnimationTime()
    {
        var slow = new ToyRunner(App("spirograph", a => a.Speed = 50));
        var fast = new ToyRunner(App("spirograph", a => a.Speed = 200));
        slow.Advance(4000);
        fast.Advance(4000);
        Assert.Equal(2f, ((GeometricPatternsApp)slow.App).Field!.AnimationTime, 0.1f);
        Assert.Equal(8f, ((GeometricPatternsApp)fast.App).Field!.AnimationTime, 0.1f);
    }

    [Fact]
    public void Auto_CyclesThroughPatterns_WithATransition()
    {
        var app = App("auto", a => a.Interval = 5);
        var r = new ToyRunner(app);
        r.Advance(200);
        var field = app.Field!;
        Assert.Equal("spirograph", field.CurrentPattern);
        r.Advance(5000);
        Assert.True(field.IsTransitioning, "no transition started after the interval");
        r.Advance(2000);
        Assert.False(field.IsTransitioning);
        Assert.Equal("polygons", field.CurrentPattern);
        var seen = new HashSet<string> { "spirograph", "polygons" };
        r.Advance(5200 * 4, () => seen.Add(field.CurrentPattern));
        Assert.Equal(GeometricField.PatternNames.Length, seen.Count);
    }

    [Fact]
    public void Transition_MidSnapshot()
    {
        var app = App("auto", a => a.Interval = 5);
        var r = new ToyRunner(app);
        r.Advance(5000 + 200 + 550);
        Assert.True(app.Field!.IsTransitioning);
        var mid = r.Snapshot();
        Assert.False(SnapshotHelper.IsBlank(mid));
        SnapshotHelper.AssertMatchesSnapshot(mid, "toys_geo_transition_mid");
    }

    [Fact]
    public void ChoosingAPattern_TransitionsToIt()
    {
        var app = App("spirograph");
        var r = new ToyRunner(app);
        r.Advance(1000);
        app.UpdateSetting("pattern", "kaleidoscope");
        r.Advance(100);
        Assert.True(app.Field!.IsTransitioning);
        r.Advance(2000);
        Assert.Equal("kaleidoscope", app.Field.CurrentPattern);
    }

    [Fact]
    public void Settings_RoundTrip_WithJsonElements()
    {
        var app = App();
        static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
        app.UpdateSetting("pattern", J("\"lissajous\""));
        app.UpdateSetting("palette", J("\"aurora\""));
        app.UpdateSetting("speed", J("250"));
        app.UpdateSetting("interval", J("\"20\""));
        Assert.Equal("lissajous", app.Pattern);
        Assert.Equal("aurora", app.Palette);
        Assert.Equal(250, app.Speed);
        Assert.Equal(20, app.Interval);
        var settings = app.GetSettings().ToDictionary(s => s.Key);
        Assert.Equal(4, settings.Count);
        Assert.Equal(AppSettingType.Select, settings["pattern"].Type);
        Assert.Equal(AppSettingType.Integer, settings["speed"].Type);
        app.UpdateSetting("speed", J("9999"));
        Assert.Equal(400, app.Speed);
        app.UpdateSetting("pattern", J("\"bogus\""));
        Assert.Equal("lissajous", app.Pattern);
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void SteadyState_AllocatesNothingPerFrame(string pattern)
    {
        var app = App(pattern);
        var r = new ToyRunner(app);
        r.Advance(1000);
        // Warm up and include a transition (the pattern change is requested mid-run).
        void Frame() { r.Advance(16); r.Render(); }
        for (int i = 0; i < 60; i++) Frame();
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Frame();
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.True(windows.Min() == 0, "allocated bytes per 50-frame window: " + string.Join(", ", windows));
    }

    [Fact]
    public void SteadyState_AllocatesNothing_DuringAutoTransitions()
    {
        var app = App("auto", a => a.Interval = 3);
        var r = new ToyRunner(app);
        r.Advance(500);
        void Frame() { r.Advance(16); r.Render(); }
        for (int i = 0; i < 700; i++) Frame(); // first full cycle: builds every scratch buffer and table
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
