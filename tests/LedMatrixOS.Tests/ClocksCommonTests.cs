using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

/// <summary>Behaviour shared by the three clock apps: determinism, non-blank output, settings, allocation and speed.</summary>
public class ClocksCommonTests
{
    private readonly ITestOutputHelper _out;
    public ClocksCommonTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Apps =>
    [
        [(Func<WidgetApp>)(() => new ClockApp())],
        [(Func<WidgetApp>)(() => new AnimatedClockApp())],
        [(Func<WidgetApp>)(() => new FlipClockApp())],
    ];

    private static byte[] Bytes(FrameBuffer frame)
    {
        var bytes = new List<byte>();
        foreach (var p in frame.GetPixelsSpan()) { bytes.Add(p.R); bytes.Add(p.G); bytes.Add(p.B); }
        return bytes.ToArray();
    }

    private static byte[] Run(WidgetApp app, int frames, DateTimeOffset? start = null)
    {
        var h = new ClocksHarness(app, start ?? ClocksHarness.At(10, 20, 30, 250));
        h.Step(16, frames);
        h.Render();
        return Bytes(h.Frame);
    }

    [Theory, MemberData(nameof(Apps))]
    public void Renders_NonBlank_AndDeterministic(Func<WidgetApp> make)
    {
        var a = Run(make(), 200);
        var b = Run(make(), 200);
        Assert.Contains(a, x => x != 0);
        Assert.Equal(a, b);
    }

    [Theory, MemberData(nameof(Apps))]
    public void KeepsIdAndName(Func<WidgetApp> make)
    {
        var app = make();
        Assert.Contains((app.Id, app.Name), new[] { ("clock", "Clock"), ("animated-clock", "Animated Clock"), ("flip-clock", "Flip Clock") });
    }

    [Theory, MemberData(nameof(Apps))]
    public void Settings_RoundTrip_AndStaySafeWhileRunning(Func<WidgetApp> make)
    {
        var app = make();
        var h = new ClocksHarness(app, ClocksHarness.At(10, 20, 30));
        h.Step(16, 30);
        foreach (var s in app.GetSettings().ToList())
        {
            object[] values = s.Type == AppSettingType.Boolean ? [false, true] : s.Options?.Cast<object>().ToArray() ?? [s.CurrentValue];
            foreach (var v in values)
            {
                app.UpdateSetting(s.Key, v);
                Assert.Equal(v.ToString(), app.GetSettings().First(x => x.Key == s.Key).CurrentValue.ToString(), ignoreCase: true);
                h.Step(16, 20);
                h.Render();
            }
        }
    }

    [Fact]
    public void LegacySettingKeysAreKept()
    {
        var keys = new ClockApp().GetSettings().Select(s => s.Key).ToHashSet();
        Assert.Superset(new HashSet<string> { "showSeconds", "show24Hour", "timeColor" }, keys);
        keys = new FlipClockApp().GetSettings().Select(s => s.Key).ToHashSet();
        Assert.Superset(new HashSet<string> { "showSeconds", "show24Hour", "textColor", "backgroundColor" }, keys);
    }

    [Fact]
    public void ClockApp_HonoursLegacyTimeColor()
    {
        var app = new ClockApp();
        app.UpdateSetting("timeColor", "Red");
        var h = new ClocksHarness(app, ClocksHarness.At(10, 20, 30));
        h.Step(16, 200);
        h.Render();
        int strongBlue = 0;
        foreach (var p in h.Frame.GetPixelsSpan()) if (p.B > 150 && p.B > p.R + 30) strongBlue++;
        Assert.True(strongBlue < 20, $"{strongBlue} strongly blue pixels");
    }

    [Fact]
    public void ClockApp_ShowsTimeFromTimeProvider()
    {
        var a = Run(new ClockApp(), 200);
        var b = Run(new ClockApp(), 200, ClocksHarness.At(11, 20, 30, 250));
        Assert.NotEqual(a, b);
    }

    [Theory, MemberData(nameof(Apps))]
    public void SteadyState_AllocatesNothingPerFrame(Func<WidgetApp> make)
    {
        var h = new ClocksHarness(make(), ClocksHarness.At(10, 20, 30, 0));
        h.Step(16, 400);
        for (int i = 0; i < 120; i++) { h.Step(16); h.Render(); }

        var windows = new long[5];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { h.Step(16); h.Render(); } // 1.6 s: second ticks and digit rolls
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.True(windows.Min() == 0, "per-window allocated bytes: " + string.Join(", ", windows));
    }

    [Theory, MemberData(nameof(Apps))]
    public void FrameCost_IsSmall(Func<WidgetApp> make)
    {
        var h = new ClocksHarness(make(), ClocksHarness.At(10, 20, 30, 0));
        h.Step(16, 400);
        for (int i = 0; i < 100; i++) { h.Step(16); h.Render(); }
        var sw = Stopwatch.StartNew();
        const int n = 600;
        for (int i = 0; i < n; i++) { h.Step(16); h.Render(); }
        double ms = sw.Elapsed.TotalMilliseconds / n;
        _out.WriteLine($"{h.App.Id}: {ms:F3} ms/frame (update + render)");
        Assert.True(ms < 12, $"{ms} ms/frame");
    }
}
