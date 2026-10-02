using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

/// <summary>Frame cost of the ambient apps (update + render). Run with -c Release and --logger "console;verbosity=detailed" to see numbers; the asserts are loose so Debug builds pass.</summary>
public class AmbientPerfTests(ITestOutputHelper output)
{
    private static WidgetApp Make(string key) => key switch
    {
        "home-aurora" => new HomePageApp { DisplayMode = "Aurora" },
        "home-starfield" => new HomePageApp { DisplayMode = "Starfield" },
        "home-embers" => new HomePageApp { DisplayMode = "Embers" },
        "home-waves" => new HomePageApp { DisplayMode = "Waves" },
        "home-daysky" => new HomePageApp { DisplayMode = "Day Night Sky" },
        "home-minimal" => new HomePageApp { DisplayMode = "Minimal" },
        "countdown-final" => new CountdownTimerApp { DurationMinutes = 1 },
        "ticker-huge-rainbow-wave" => new ScrollingTextApp { FontSize = 48, TextEffect = "Rainbow Wave", Decor = "Chase Lights" },
        "ticker-default" => new ScrollingTextApp(),
        "solid-aurora" => new SolidColorApp { Mode = "Aurora" },
        "solid-sparkle" => new SolidColorApp { Mode = "Sparkle" },
        _ => throw new ArgumentException(key),
    };

    [Theory]
    [InlineData("home-aurora")]
    [InlineData("home-starfield")]
    [InlineData("home-embers")]
    [InlineData("home-waves")]
    [InlineData("home-daysky")]
    [InlineData("home-minimal")]
    [InlineData("countdown-final")]
    [InlineData("ticker-huge-rainbow-wave")]
    [InlineData("ticker-default")]
    [InlineData("solid-aurora")]
    [InlineData("solid-sparkle")]
    public void FrameCost(string key)
    {
        var rig = new AmbientRig(Make(key));
        if (key == "countdown-final") rig.Advance(52_000);
        for (int i = 0; i < 300; i++) { rig.Advance(16, 16); rig.Draw(); }
        const int frames = 600;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++) { rig.Advance(16, 16); rig.Draw(); }
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        output.WriteLine($"PERF {key}: {ms:F3} ms/frame");
        Assert.True(ms < 25, $"{key} took {ms:F2} ms per frame");
    }
}
