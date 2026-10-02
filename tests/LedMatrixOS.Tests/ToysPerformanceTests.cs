using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

public class ToysPerformanceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases() =>
    [
        ["balls", new Func<WidgetApp>(() => new BouncingBallsApp { Count = 24 })],
        ["lava", new Func<WidgetApp>(() => new BouncingBallsApp { Style = "lava" })],
        ["geo-spirograph", new Func<WidgetApp>(() => new GeometricPatternsApp { Pattern = "spirograph" })],
        ["geo-polygons", new Func<WidgetApp>(() => new GeometricPatternsApp { Pattern = "polygons" })],
        ["geo-lissajous", new Func<WidgetApp>(() => new GeometricPatternsApp { Pattern = "lissajous" })],
        ["geo-kaleidoscope", new Func<WidgetApp>(() => new GeometricPatternsApp { Pattern = "kaleidoscope" })],
        ["geo-tessellation", new Func<WidgetApp>(() => new GeometricPatternsApp { Pattern = "tessellation" })],
        ["dvd", new Func<WidgetApp>(() => new DvdLogoApp())],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void FrameTime_IsReasonable(string name, Func<WidgetApp> make)
    {
        var r = new ToyRunner(make());
        r.Advance(2000);
        for (int i = 0; i < 30; i++) { r.Advance(16); r.Render(); }
        var sw = Stopwatch.StartNew();
        const int n = 300;
        for (int i = 0; i < n; i++) { r.Advance(16); r.Render(); }
        double ms = sw.Elapsed.TotalMilliseconds / n;
        output.WriteLine($"{name}: {ms:F3} ms/frame");
        Assert.True(ms < 25, $"{name} took {ms} ms/frame");
    }
}
