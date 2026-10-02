using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Tests;

/// <summary>Drives a <see cref="WidgetApp"/> with deterministic frame times for the Ambient* tests.</summary>
internal sealed class AmbientRig
{
    private long _index;

    public AmbientRig(WidgetApp app, FakeTime? time = null)
    {
        Fonts.Load();
        App = app;
        Clock = time ?? new FakeTime();
        app.Time = Clock;
        app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None).GetAwaiter().GetResult();
        Frame = new FrameBuffer(256, 64);
    }

    public WidgetApp App { get; }
    public FakeTime Clock { get; }
    public FrameBuffer Frame { get; }
    public TimeSpan Now { get; private set; }

    /// <summary>Advances frame time (and wall time, so clocks tick with it) in <paramref name="stepMs"/> steps.</summary>
    public AmbientRig Advance(TimeSpan span, int stepMs = 50, bool moveWallClock = true)
    {
        var end = Now + span;
        var step = TimeSpan.FromMilliseconds(stepMs);
        while (Now < end)
        {
            var d = end - Now < step ? end - Now : step;
            Now += d;
            if (moveWallClock) Clock.Now += d;
            App.Update(new FrameContext(Now, d, _index++), CancellationToken.None);
        }
        return this;
    }

    public AmbientRig Advance(int ms, int stepMs = 50, bool moveWallClock = true) => Advance(TimeSpan.FromMilliseconds(ms), stepMs, moveWallClock);

    public FrameBuffer Draw()
    {
        Frame.Clear(Pixel.Black);
        App.Render(Frame, CancellationToken.None);
        return Frame;
    }

    public FrameBuffer Copy()
    {
        var copy = new FrameBuffer(256, 64);
        copy.CopyFrom(Draw());
        return copy;
    }

    /// <summary>Fails if any window of 100 steady-state frames allocates (one-off JIT allocations only hit some windows, so the minimum is used).</summary>
    public AmbientRig AssertNoAllocationsPerFrame(int warmupFrames = 200)
    {
        for (int i = 0; i < warmupFrames; i++) { Advance(16, 16); Draw(); }
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { Advance(16, 16); Draw(); }
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Xunit.Assert.True(windows.Min() == 0, "allocated bytes per 100-frame window: " + string.Join(", ", windows));
        return this;
    }

    public static FrameBuffer Render(WidgetApp app, TimeSpan until, FakeTime? time = null) => new AmbientRig(app, time).Advance(until).Copy();
}
