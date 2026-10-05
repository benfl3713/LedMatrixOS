using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

/// <summary>Demoscene effects: determinism, content, settings, auto-cycle, goldens, allocations and frame cost.</summary>
public class DemosceneAppTests(ITestOutputHelper output)
{
    private static readonly string[] Effects = ["Plasma", "Tunnel", "Metaballs", "Starfield Warp", "Lava Lamp", "Cube", "Mandelbrot Zoom"];

    public static IEnumerable<object[]> EffectNames() => Effects.Select(e => new object[] { e });

    private static DemosceneApp Make(string effect, int seed = 11) => new() { Effect = effect, Seed = seed };

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    private static string Slug(string effect) => effect.ToLowerInvariant().Replace(' ', '_');

    [Fact]
    public void Identity_AndRegistered()
    {
        var app = new DemosceneApp();
        Assert.Equal("demoscene", app.Id);
        Assert.Equal("Demoscene", app.Name);
        Assert.Contains(typeof(DemosceneApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void Settings_AreExposedWithOptionsAndRanges()
    {
        var s = ((IConfigurableApp)new DemosceneApp()).GetSettings().ToDictionary(x => x.Key);
        Assert.Equal(["effect", "secondsPerEffect", "palette", "speed", "scale", "mirror", "intensity"], s.Keys.ToArray());
        Assert.Equal(8, s["effect"].Options!.Length);
        Assert.Equal("Auto-cycle", s["effect"].CurrentValue);
        Assert.Equal(["Neon", "Fire", "Ocean", "Forest", "Mono", "Rainbow", "Sunset"], s["palette"].Options);
        Assert.Equal(["Off", "Horizontal", "Quad"], s["mirror"].Options);
        Assert.Equal(5, s["secondsPerEffect"].MinValue);
    }

    [Fact]
    public void Settings_RoundTripPersistedJsonElements_AndStillRender()
    {
        var app = new DemosceneApp { Seed = 1 };
        var cfg = (IConfigurableApp)app;
        foreach (var setting in cfg.GetSettings().ToList())
        {
            object expected;
            string raw;
            switch (setting.CurrentValue)
            {
                case int n:
                    int max = (int)(setting.MaxValue ?? 10), min = (int)(setting.MinValue ?? 1);
                    int next = n == max ? min : max;
                    expected = next; raw = next.ToString();
                    break;
                case string s when setting.Options is { Length: > 1 } opts:
                    var other = opts.First(o => o != s);
                    expected = other; raw = System.Text.Json.JsonSerializer.Serialize(other);
                    break;
                default:
                    continue;
            }

            cfg.UpdateSetting(setting.Key, VisualDriver.Json(raw));
            Assert.Equal(expected, cfg.GetSettings().Single(x => x.Key == setting.Key).CurrentValue);
        }

        Assert.False(SnapshotHelper.IsBlank(new VisualDriver(app).RunTo(Ms(1500))));
    }

    [Theory, MemberData(nameof(EffectNames))]
    public void RendersSomethingBrightAndAnimates(string effect)
    {
        var d = new VisualDriver(Make(effect));
        var a = d.RunTo(Ms(2000));
        Assert.True(VisualDriver.Coverage(a) > 0.12, $"{effect} coverage {VisualDriver.Coverage(a):F2}");
        var b = d.RunTo(Ms(2400));
        Assert.False(VisualDriver.Same(a, b));
    }

    [Theory, MemberData(nameof(EffectNames))]
    public void SameSeedAndTimeGiveIdenticalFrames(string effect)
    {
        var a = new VisualDriver(Make(effect, 5)).RunTo(Ms(3000));
        var b = new VisualDriver(Make(effect, 5)).RunTo(Ms(3000));
        Assert.True(VisualDriver.Same(a, b));
    }

    [Theory]
    [InlineData("Metaballs")]
    [InlineData("Starfield Warp")]
    [InlineData("Lava Lamp")]
    public void DifferentSeedsDiffer(string effect)
    {
        var a = new VisualDriver(Make(effect, 1)).RunTo(Ms(2000));
        var b = new VisualDriver(Make(effect, 2)).RunTo(Ms(2000));
        Assert.False(VisualDriver.Same(a, b));
    }

    [Theory]
    [InlineData("Horizontal")]
    [InlineData("Quad")]
    public void Mirror_ProducesSymmetricFrames(string mirror)
    {
        var f = new VisualDriver(new DemosceneApp { Effect = "Plasma", Mirror = mirror, Seed = 1 }).RunTo(Ms(1500));
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width / 2; x++)
            {
                Assert.Equal(f.GetPixel(x, y), f.GetPixel(f.Width - 1 - x, y));
                if (mirror == "Quad") Assert.Equal(f.GetPixel(x, y), f.GetPixel(x, f.Height - 1 - y));
            }
    }

    [Fact]
    public void Palette_ChangesTheColours()
    {
        var a = new VisualDriver(new DemosceneApp { Effect = "Plasma", Palette = "Neon", Seed = 1 }).RunTo(Ms(1000));
        var b = new VisualDriver(new DemosceneApp { Effect = "Plasma", Palette = "Forest", Seed = 1 }).RunTo(Ms(1000));
        Assert.False(VisualDriver.Same(a, b));
    }

    [Fact]
    public void Intensity_ScalesBrightness()
    {
        static long Sum(FrameBuffer f) { long s = 0; foreach (var p in f.GetPixelsSpan()) s += p.R + p.G + p.B; return s; }
        var dim = new VisualDriver(new DemosceneApp { Effect = "Plasma", Intensity = 2, Seed = 1 }).RunTo(Ms(1000));
        var bright = new VisualDriver(new DemosceneApp { Effect = "Plasma", Intensity = 10, Seed = 1 }).RunTo(Ms(1000));
        Assert.True(Sum(bright) > Sum(dim) * 3);
    }

    [Fact]
    public void AutoCycle_AdvancesAfterTheInterval_WithACrossfade()
    {
        var app = new DemosceneApp { Effect = "Auto-cycle", SecondsPerEffect = 5, Seed = 1 };
        var d = new VisualDriver(app);
        d.RunTo(Ms(4000));
        Assert.Equal(0, app.CurrentEffect);
        Assert.False(app.Fading);
        d.RunTo(Ms(5800));
        Assert.True(app.Fading);
        var mid = d.Render();
        Assert.False(SnapshotHelper.IsBlank(mid));
        d.RunTo(Ms(7000));
        Assert.False(app.Fading);
        Assert.Equal(1, app.CurrentEffect);
        d.RunTo(Ms(12100));
        d.RunTo(Ms(13500));
        Assert.Equal(2, app.CurrentEffect);
    }

    [Fact]
    public void ChoosingAnEffect_CrossfadesToIt()
    {
        var app = new DemosceneApp { Effect = "Plasma", Seed = 1 };
        var d = new VisualDriver(app);
        d.RunTo(Ms(1000));
        app.Effect = "Cube";
        d.RunTo(Ms(1500));
        Assert.True(app.Fading);
        d.RunTo(Ms(3500));
        Assert.Equal(5, app.CurrentEffect);
    }

    // ------------------------------------------------------------ goldens

    [Theory, MemberData(nameof(EffectNames))]
    public void Effect_Snapshot(string effect)
    {
        var f = new VisualDriver(Make(effect, 7)).RunTo(Ms(4000));
        Assert.False(SnapshotHelper.IsBlank(f));
        VisualDriver.DumpSheet("demoscene_" + Slug(effect), f);
        Preview(f, "demoscene_" + Slug(effect));
        SnapshotHelper.AssertMatchesSnapshot(f, "demoscene_" + Slug(effect));
    }

    [Theory]
    [InlineData("Plasma", "Fire", "Quad", "demoscene_plasma_fire_quad")]
    [InlineData("Tunnel", "Rainbow", "Off", "demoscene_tunnel_rainbow")]
    [InlineData("Starfield Warp", "Ocean", "Off", "demoscene_starfield_ocean")]
    [InlineData("Cube", "Sunset", "Off", "demoscene_cube_sunset")]
    [InlineData("Mandelbrot Zoom", "Fire", "Horizontal", "demoscene_mandelbrot_fire_horizontal")]
    [InlineData("Lava Lamp", "Forest", "Off", "demoscene_lava_forest")]
    [InlineData("Metaballs", "Mono", "Off", "demoscene_metaballs_mono")]
    public void Variant_Snapshot(string effect, string palette, string mirror, string name)
    {
        var f = new VisualDriver(new DemosceneApp { Effect = effect, Palette = palette, Mirror = mirror, Seed = 7 }).RunTo(Ms(effect == "Cube" ? 6100 : 9000));
        Assert.False(SnapshotHelper.IsBlank(f));
        Preview(f, name);
        SnapshotHelper.AssertMatchesSnapshot(f, name);
    }

    [Fact]
    public void Crossfade_Snapshot()
    {
        var app = new DemosceneApp { Effect = "Auto-cycle", SecondsPerEffect = 5, Seed = 7 };
        var f = new VisualDriver(app).RunTo(Ms(5750));
        Assert.True(app.Fading);
        Preview(f, "demoscene_crossfade");
        SnapshotHelper.AssertMatchesSnapshot(f, "demoscene_crossfade");
    }

    private static void Preview(FrameBuffer f, string name)
    {
        var dir = Environment.GetEnvironmentVariable("LED_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var img = SnapshotHelper.ToImage(f);
        using var big = SixLabors.ImageSharp.Processing.ProcessingExtensions.Clone(img, c =>
            SixLabors.ImageSharp.Processing.ResizeExtensions.Resize(c, f.Width * 4, f.Height * 4, SixLabors.ImageSharp.Processing.KnownResamplers.NearestNeighbor));
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(big, Path.Combine(dir, name + ".png"));
    }

    // ------------------------------------------------------------ allocations and speed

    [Theory, MemberData(nameof(EffectNames))]
    public void SteadyState_AllocatesNothingPerFrame(string effect)
    {
        var app = Make(effect);
        var d = new VisualDriver(app);
        d.Step(120);
        d.Render();

        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                d.Step();
                d.Frame.Clear(Pixel.Black);
                app.Render(d.Frame, CancellationToken.None);
            }
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.True(windows.Min() == 0, effect + " allocated bytes per 100-frame window: " + string.Join(", ", windows));
    }

    [Fact]
    public void AutoCycleAndMirror_AllocateNothingPerFrame()
    {
        var app = new DemosceneApp { Effect = "Auto-cycle", SecondsPerEffect = 5, Mirror = "Quad", Seed = 3 };
        var d = new VisualDriver(app);
        d.Step(120);
        d.Render();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 700; i++) // crosses several crossfades
        {
            d.Step();
            app.Render(d.Frame, CancellationToken.None);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory, MemberData(nameof(EffectNames))]
    public void FrameCost_StaysLow(string effect)
    {
        var app = Make(effect);
        var d = new VisualDriver(app);
        d.Step(200);
        for (int i = 0; i < 30; i++) { d.Step(); app.Render(d.Frame, CancellationToken.None); }
        var sw = Stopwatch.StartNew();
        const int frames = 300;
        for (int i = 0; i < frames; i++)
        {
            d.Step();
            app.Render(d.Frame, CancellationToken.None);
        }
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        output.WriteLine($"{effect}: {ms:F2} ms/frame (update+render)");
        Assert.True(ms < 6, $"{effect} took {ms:F2} ms/frame");
    }
}
