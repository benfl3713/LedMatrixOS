using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Xunit;

namespace LedMatrixOS.Tests;

public class ChartTests
{
    public ChartTests() => Fonts.Load();

    private static readonly float[] Prices = [12, 9, 7, 6, 8, 14, 22, 31, 28, 24, 19, 15, 13, 11, 10, 12, 18, 27, 35, 30, 22, 16, 12, 9];

    private static Dock Scene() => new()
    {
        Top = new Sparkline { Values = Prices, Height = 20, Padding = new Thickness(4, 2) },
        Fill = new BarChart
        {
            Values = Prices,
            HighlightIndex = 7,
            Baseline = true,
            Padding = new Thickness(4, 2),
            ColorOf = static (_, v) => v < 12 ? new Pixel(0, 200, 90) : v < 25 ? new Pixel(255, 170, 40) : new Pixel(230, 40, 40),
        },
    };

    [Fact]
    public void Charts_Snapshot()
    {
        var stage = new Stage(Scene());
        stage.Step(16);
        var frame = stage.Snapshot();
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, "charts_sparkline_bars");
    }

    [Fact]
    public void Charts_AllocateNothingPerFrame()
    {
        var stage = new Stage(Scene());
        var frame = new FrameBuffer(256, 64);

        void Frame()
        {
            stage.Step(16);
            frame.Clear(Pixel.Black);
            stage.Host.Render(frame);
        }

        for (int i = 0; i < 50; i++) Frame();

        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Frame();
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.True(windows.Min() == 0, "allocated bytes per 100-frame window: " + string.Join(", ", windows));
    }

    [Fact]
    public void EmptyAndSingleValueSeries_DoNotThrow()
    {
        foreach (float[] values in new[] { Array.Empty<float>(), [5f], [3f, 3f, 3f] })
        {
            var root = new Dock { Top = new Sparkline { Values = values, Height = 10 }, Fill = new BarChart { Values = values } };
            var stage = new Stage(root);
            stage.Step(16);
            _ = stage.Snapshot();
        }
    }
}
