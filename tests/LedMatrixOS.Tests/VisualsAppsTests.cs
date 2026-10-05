using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

/// <summary>Fire, Matrix Rain, Rainbow Spiral and Equalizer on the widget platform: determinism, content, settings, goldens, allocations.</summary>
public class VisualsAppsTests
{
    private readonly ITestOutputHelper _output;

    public VisualsAppsTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> AppNames() => [["fire"], ["rain"], ["spiral"], ["eq"]];

    private static IMatrixApp Make(string name, int seed = 11) => name switch
    {
        "fire" => new FireApp { Seed = seed },
        "rain" => new MatrixRainApp { Seed = seed },
        "spiral" => new RainbowSpiralApp { Seed = seed },
        _ => new EqualizerApp { Seed = seed },
    };

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    // ------------------------------------------------------------ basics

    [Theory, MemberData(nameof(AppNames))]
    public void IdsAndNamesAreUnchanged(string name)
    {
        var app = Make(name);
        var (id, display) = name switch
        {
            "fire" => ("fire", "Fire"),
            "rain" => ("matrix-rain", "Matrix Rain"),
            "spiral" => ("rainbow-spiral", "Rainbow Spiral"),
            _ => ("equalizer", "Equalizer Visualizer"),
        };
        Assert.Equal(id, app.Id);
        Assert.Equal(display, app.Name);
    }

    [Theory, MemberData(nameof(AppNames))]
    public void RendersSomethingAndAnimates(string name)
    {
        var d = new VisualDriver(Make(name));
        var a = d.RunTo(Ms(2000));
        Assert.False(SnapshotHelper.IsBlank(a));
        Assert.True(VisualDriver.Lit(a) > 400, "lit pixels: " + VisualDriver.Lit(a));
        var b = d.RunTo(Ms(2400));
        Assert.False(VisualDriver.Same(a, b));
    }

    [Theory, MemberData(nameof(AppNames))]
    public void SameSeedAndTimeGiveIdenticalFrames(string name)
    {
        var a = new VisualDriver(Make(name, 5)).RunTo(Ms(3000));
        var b = new VisualDriver(Make(name, 5)).RunTo(Ms(3000));
        Assert.True(VisualDriver.Same(a, b));
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("rain")]
    [InlineData("spiral")]
    public void DifferentSeedsDiffer(string name)
    {
        var a = new VisualDriver(Make(name, 1)).RunTo(Ms(2000));
        var b = new VisualDriver(Make(name, 2)).RunTo(Ms(2000));
        Assert.False(VisualDriver.Same(a, b));
    }

    // ------------------------------------------------------------ settings

    [Theory, MemberData(nameof(AppNames))]
    public void SettingsRoundTripPersistedJsonElements(string name)
    {
        var app = (IConfigurableApp)Make(name);
        foreach (var setting in app.GetSettings().ToList())
        {
            // Mimic app-settings.json: persisted values come back as JsonElement.
            object expected;
            string raw;
            switch (setting.CurrentValue)
            {
                case bool b:
                    expected = !b;
                    raw = b ? "false" : "true";
                    break;
                case int n:
                    int max = (int)(setting.MaxValue ?? 10), min = (int)(setting.MinValue ?? 1);
                    int next = n == max ? min : max;
                    expected = next;
                    raw = next.ToString();
                    break;
                case string s when setting.Options is { Length: > 1 } opts:
                    var other = opts.First(o => o != s);
                    expected = other;
                    raw = System.Text.Json.JsonSerializer.Serialize(other);
                    break;
                default:
                    continue;
            }

            app.UpdateSetting(setting.Key, VisualDriver.Json(raw));
            Assert.Equal(expected, app.GetSettings().Single(s => s.Key == setting.Key).CurrentValue);
        }

        // And the app still renders with everything flipped.
        var d = new VisualDriver((IMatrixApp)app);
        Assert.False(SnapshotHelper.IsBlank(d.RunTo(Ms(1500))));
    }

    [Fact]
    public void LegacySettingKeysAreStillThere()
    {
        var keys = ((IConfigurableApp)new EqualizerApp()).GetSettings().Select(s => s.Key).ToHashSet();
        foreach (var k in new[] { "barCount", "smoothness", "colorMode", "autoGenerate", "audioSource" }) Assert.Contains(k, keys);
        Assert.Contains("style", keys);
    }

    [Fact]
    public void EqualizerBarCountAndColorModeApply()
    {
        var app = new EqualizerApp { Seed = 1 };
        var d = new VisualDriver(app);
        d.RunTo(Ms(1000));
        app.UpdateSetting("barCount", VisualDriver.Json("16"));
        app.UpdateSetting("colorMode", VisualDriver.Json("\"Heat\""));
        var f = d.RunTo(Ms(2500));
        Assert.Equal(16, app.BarCount);
        Assert.False(SnapshotHelper.IsBlank(f));
    }

    // ------------------------------------------------------------ palettes, mirror

    [Theory]
    [InlineData("Neon")]
    [InlineData("Candy")]
    [InlineData("Rainbow")]
    [InlineData("Mono")]
    public void Fire_SharedPalettes_RenderAndDiffer(string palette)
    {
        var fire = new VisualDriver(new FireApp { Seed = 3, Palette = palette }).RunTo(Ms(2000));
        var classic = new VisualDriver(new FireApp { Seed = 3 }).RunTo(Ms(2000));
        Assert.False(SnapshotHelper.IsBlank(fire));
        Assert.False(VisualDriver.Same(fire, classic));
    }

    [Theory]
    [InlineData("Candy")]
    [InlineData("Mono")]
    public void Spiral_SharedPalettes_RenderAndDiffer(string palette)
    {
        var spiral = new VisualDriver(new RainbowSpiralApp { Seed = 3, Palette = palette }).RunTo(Ms(2000));
        var rainbow = new VisualDriver(new RainbowSpiralApp { Seed = 3 }).RunTo(Ms(2000));
        Assert.False(SnapshotHelper.IsBlank(spiral));
        Assert.False(VisualDriver.Same(spiral, rainbow));
    }

    [Fact]
    public void FireAndSpiral_OfferTheSharedPaletteNames()
    {
        foreach (var name in new[] { "Neon", "Sunset", "Ocean", "Candy", "Aurora", "Rainbow", "Mono" })
        {
            Assert.Contains(name, ((IConfigurableApp)new FireApp()).GetSettings().Single(s => s.Key == "palette").Options!);
            Assert.Contains(name, ((IConfigurableApp)new RainbowSpiralApp()).GetSettings().Single(s => s.Key == "palette").Options!);
        }

        // Existing persisted values (and any casing) keep working.
        var fire = new FireApp();
        fire.UpdateSetting("palette", "purple");
        Assert.Equal("Purple", fire.Palette);
    }

    [Fact]
    public void Equalizer_MirrorSettingExists_AndChangesRadial()
    {
        var setting = ((IConfigurableApp)new EqualizerApp()).GetSettings().Single(s => s.Key == "mirror");
        Assert.Equal(true, setting.CurrentValue);
        var svc = new AudioDataService();
        var mirrored = new VisualDriver(LiveApp(svc, "Radial")).RunTo(Ms(1500), () => svc.AddAudioSamples(Samples(0.9f)));
        var wrapped = LiveApp(svc, "Radial");
        wrapped.Mirror = false;
        var wrappedFrame = new VisualDriver(wrapped).RunTo(Ms(1500), () => svc.AddAudioSamples(Samples(0.9f)));
        Assert.False(VisualDriver.Same(mirrored, wrappedFrame));
        SnapshotHelper.AssertMatchesSnapshot(wrappedFrame, "visuals_eq_radial_unmirrored_loud");
    }

    [Fact]
    public void Equalizer_RadialSpansTheWholeWidth()
    {
        var svc = new AudioDataService();
        var f = new VisualDriver(LiveApp(svc, "Radial")).RunTo(Ms(1500), () => svc.AddAudioSamples(Samples(0.9f)));
        int minX = f.Width, maxX = -1;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                var p = f.GetPixel(x, y);
                if (p.R + p.G + p.B < 60) continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }

        Assert.True(maxX - minX > f.Width * 0.8, $"radial spans {minX}..{maxX}");
    }

    // ------------------------------------------------------------ equalizer audio behaviour

    private static float[] Samples(float level)
    {
        var s = new float[640];
        for (int j = 0; j < 64; j++)
        {
            float amp = level * (1f - j / 64f * 0.75f) * (0.75f + 0.25f * MathF.Sin(j * 0.9f));
            for (int k = 0; k < 10; k++) s[j * 10 + k] = amp;
        }
        return s;
    }

    private static EqualizerApp LiveApp(AudioDataService svc, string style = "Bars") => new(svc)
    {
        Seed = 2, AudioSource = "Microphone", AutoGenerate = false, Style = style,
    };

    [Fact]
    public void Equalizer_LiveAudioDrivesBars_AndSilenceFallsBackToIdle()
    {
        var svc = new AudioDataService();
        var app = LiveApp(svc);
        var d = new VisualDriver(app);

        var idle = d.RunTo(Ms(1500));
        Assert.False(app.IsShowingLiveAudio);
        int idleLit = VisualDriver.Lit(idle);
        Assert.True(idleLit > 100, "idle breathing must not be blank");

        var loud = d.RunTo(Ms(2500), () => svc.AddAudioSamples(Samples(0.9f)));
        Assert.True(app.IsShowingLiveAudio);
        Assert.True(VisualDriver.Lit(loud) > idleLit * 2, $"loud {VisualDriver.Lit(loud)} vs idle {idleLit}");

        svc.Clear();
        d.Step(120);
        Assert.False(app.IsShowingLiveAudio);
    }

    [Fact]
    public void Equalizer_PeakCapsHoldThenFall()
    {
        var svc = new AudioDataService();
        var app = LiveApp(svc);
        var d = new VisualDriver(app);
        d.RunTo(Ms(1000), () => svc.AddAudioSamples(Samples(0.9f)));
        svc.Clear();
        d.Step(10); // bars start falling, caps are held
        var held = d.Render();
        d.Step(120); // ~2 s of silence: caps have fallen under gravity
        var fallen = d.Render();

        static int Top(FrameBuffer f)
        {
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < 40; x++)
                {
                    var p = f.GetPixel(x, y);
                    if (p.R + p.G + p.B > 150) return y;
                }
            return f.Height;
        }

        Assert.True(Top(fallen) > Top(held) + 8, $"cap top {Top(held)} -> {Top(fallen)}");
    }

    [Fact]
    public void Equalizer_PeakCapsCanBeDisabled()
    {
        var svc = new AudioDataService();
        var on = LiveApp(svc);
        var off = LiveApp(svc);
        off.PeakCaps = false;
        var a = new VisualDriver(on).RunTo(Ms(1200), () => svc.AddAudioSamples(Samples(0.9f)));
        var b = new VisualDriver(off).RunTo(Ms(1200), () => svc.AddAudioSamples(Samples(0.9f)));
        Assert.False(VisualDriver.Same(a, b));
    }

    [Fact]
    public void Equalizer_DemoGrooveIsLivelyWithoutAudio()
    {
        var app = new EqualizerApp { Seed = 1 }; // Auto Generate on, no service
        var d = new VisualDriver(app);
        var f1 = d.RunTo(Ms(3000));
        var f2 = d.RunTo(Ms(3160));
        Assert.True(VisualDriver.Lit(f1) > 800);
        Assert.False(VisualDriver.Same(f1, f2));
    }

    // ------------------------------------------------------------ goldens

    [Theory]
    [InlineData(1500, "visuals_fire_t1500")]
    [InlineData(5000, "visuals_fire_t5000")]
    public void Fire_Snapshot(int ms, string name) => Golden(new FireApp { Seed = 7 }, ms, name);

    [Fact]
    public void Fire_BluePalette_Snapshot() => Golden(new FireApp { Seed = 7, Palette = "Blue", Intensity = 8 }, 3000, "visuals_fire_blue");

    [Theory]
    [InlineData(1000, "visuals_rain_t1000")]
    [InlineData(7000, "visuals_rain_t7000_word_scan")]
    [InlineData(8500, "visuals_rain_t8500_word")]
    public void Rain_Snapshot(int ms, string name) => Golden(new MatrixRainApp { Seed = 7 }, ms, name);

    [Fact]
    public void Rain_Gold_Snapshot() => Golden(new MatrixRainApp { Seed = 7, Colour = "Gold", Words = false }, 3000, "visuals_rain_gold");

    [Theory]
    [InlineData("Twin Vortex", 3000, "visuals_spiral_twin")]
    [InlineData("Vortex", 3000, "visuals_spiral_vortex")]
    [InlineData("Plasma", 3000, "visuals_spiral_plasma")]
    [InlineData("Tunnel", 3000, "visuals_spiral_tunnel")]
    public void Spiral_Snapshot(string style, int ms, string name) => Golden(new RainbowSpiralApp { Seed = 7, Style = style }, ms, name);

    [Fact]
    public void Spiral_SunsetPalette_Snapshot() => Golden(new RainbowSpiralApp { Seed = 7, Palette = "Sunset", Arms = 5 }, 4000, "visuals_spiral_sunset");

    [Fact]
    public void Equalizer_Silent_Snapshot() =>
        Golden(new EqualizerApp(new AudioDataService()) { Seed = 2, AudioSource = "Microphone", AutoGenerate = false }, 2500, "visuals_eq_bars_silent");

    [Theory]
    [InlineData("Bars")]
    [InlineData("Mirrored")]
    [InlineData("Wave")]
    [InlineData("Dots")]
    [InlineData("Radial")]
    public void Equalizer_Loud_Snapshot(string style)
    {
        var svc = new AudioDataService();
        var d = new VisualDriver(LiveApp(svc, style));
        var f = d.RunTo(Ms(1500), () => svc.AddAudioSamples(Samples(0.9f)));
        Assert.False(SnapshotHelper.IsBlank(f));
        SnapshotHelper.AssertMatchesSnapshot(f, "visuals_eq_" + style.ToLowerInvariant() + "_loud");
    }

    [Fact]
    public void Equalizer_PeakDecay_Snapshot()
    {
        var svc = new AudioDataService();
        var d = new VisualDriver(LiveApp(svc));
        d.RunTo(Ms(1200), () => svc.AddAudioSamples(Samples(0.95f)));
        svc.Clear();
        d.Step(30); // 0.5 s later: bars have dropped, caps still hanging above them
        SnapshotHelper.AssertMatchesSnapshot(d.Render(), "visuals_eq_bars_peak_decay");
    }

    [Fact]
    public void Equalizer_DemoGroove_Snapshot() => Golden(new EqualizerApp { Seed = 3 }, 4000, "visuals_eq_demo_kick");

    private static void Golden(IMatrixApp app, int ms, string name)
    {
        var f = new VisualDriver(app).RunTo(Ms(ms));
        Assert.False(SnapshotHelper.IsBlank(f));
        SnapshotHelper.AssertMatchesSnapshot(f, name);
    }

    // ------------------------------------------------------------ allocations and speed

    [Theory]
    [InlineData("fire")]
    [InlineData("rain")]
    [InlineData("spiral")]
    [InlineData("eq")]
    [InlineData("eq-idle")]
    public void SteadyState_AllocatesNothingPerFrame(string name)
    {
        IMatrixApp app = name == "eq-idle"
            ? new EqualizerApp { Seed = 1, AutoGenerate = false, Style = "Wave" }
            : Make(name);
        var d = new VisualDriver(app);
        // Warm past the rain's first word reveal and glitch burst (about 10 s) so every lazy buffer exists.
        d.Step(750);
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

        Assert.True(windows.Min() == 0, name + " allocated bytes per 100-frame window: " + string.Join(", ", windows));
    }

    [Theory, MemberData(nameof(AppNames))]
    public void FrameCost_IsReported(string name)
    {
        var app = Make(name);
        var d = new VisualDriver(app);
        d.Step(200);
        for (int i = 0; i < 30; i++) { d.Step(); app.Render(d.Frame, CancellationToken.None); }
        var sw = Stopwatch.StartNew();
        const int frames = 200;
        for (int i = 0; i < frames; i++)
        {
            d.Step();
            app.Render(d.Frame, CancellationToken.None);
        }
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        _output.WriteLine($"{name}: {ms:F2} ms/frame (update+render)");
        Assert.True(ms < 60, $"{name} took {ms:F1} ms/frame");
    }
}
