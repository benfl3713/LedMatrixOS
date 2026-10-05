using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Tests;

/// <summary>A node that fills its bounds with a colour; used to see layout, clipping and opacity in pixels.</summary>
internal sealed class Solid : Node
{
    public Solid(Pixel color, int? width = null, int? height = null)
    {
        Color = color;
        Width = width;
        Height = height;
    }

    public Pixel Color { get; set; }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds) => frame.Fill(bounds, Color);
}

internal sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 2, 13, 45, 7, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class FakeLive<T> : ILiveData<T>
{
    public T? Value { get; set; }
    public bool IsLoading => false;
    public Exception? Error => null;
    public DateTimeOffset? LastUpdated => null;
    public event EventHandler? Changed { add { } remove { } }
}

/// <summary>Drives a <see cref="UiHost"/> with deterministic frame times.</summary>
internal sealed class Stage
{
    private long _frame;

    public Stage(Node root, int width = 256, int height = 64, TimeProvider? time = null)
    {
        Fonts.Load();
        Host = new UiHost(width, height, time ?? new FakeTime()) { Root = root };
        Frame = new FrameBuffer(width, height);
    }

    public UiHost Host { get; }
    public FrameBuffer Frame { get; }
    public TimeSpan Time { get; private set; }

    /// <summary>Runs <paramref name="count"/> frames of <paramref name="ms"/> milliseconds each.</summary>
    public void Step(int ms, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var delta = TimeSpan.FromMilliseconds(ms);
            Time += delta;
            Host.Update(new FrameContext(Time, delta, _frame++));
        }
    }

    public FrameBuffer Render()
    {
        Frame.Clear(Pixel.Black);
        Host.Render(Frame);
        return Frame;
    }

    public FrameBuffer Snapshot()
    {
        var copy = new FrameBuffer(Frame.Width, Frame.Height);
        Render();
        copy.CopyFrom(Frame);
        return copy;
    }

    public static bool Same(FrameBuffer a, FrameBuffer b) => a.GetPixelsSpan().SequenceEqual(b.GetPixelsSpan());
}

/// <summary>Result of <see cref="StageAllocationExtensions.MeasureSteadyAllocation"/>.</summary>
/// <param name="Least">Smallest allocation, in bytes, over the steady windows.</param>
/// <param name="MsPerFrame">Frame time of the last steady window.</param>
/// <param name="Measured">How many windows were steady and so counted.</param>
internal readonly record struct AllocationMeasurement(long Least, double MsPerFrame, int Measured);

internal static class StageAllocationExtensions
{
    /// <summary>
    /// Warms the stage up, then runs <paramref name="windows"/> windows of 60 frames at 33 ms and reports the least
    /// allocation seen. <paramref name="beginWindow"/> is called before each window and returns a predicate, evaluated
    /// after it, that says whether the window was steady (e.g. no page change); windows that were not are skipped.
    /// </summary>
    public static AllocationMeasurement MeasureSteadyAllocation(
        this Stage stage, int windows = 6, int warmFrames = 100, Func<Func<bool>>? beginWindow = null) =>
        Measure(ms => stage.Step(ms), () => stage.Render(), windows, warmFrames, beginWindow);

    /// <inheritdoc cref="MeasureSteadyAllocation(Stage, int, int, Func{Func{bool}}?)"/>
    public static AllocationMeasurement MeasureSteadyAllocation(
        this AppStage stage, int windows = 6, int warmFrames = 100, Func<Func<bool>>? beginWindow = null) =>
        Measure(ms => stage.Step(ms), () => stage.Render(), windows, warmFrames, beginWindow);

    private static AllocationMeasurement Measure(
        Action<int> step, Action render, int windows, int warmFrames, Func<Func<bool>>? beginWindow)
    {
        for (int i = 0; i < warmFrames; i++) { step(33); render(); }

        int measured = 0;
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < windows; window++)
        {
            var isSteady = beginWindow?.Invoke();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { step(33); render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (isSteady != null && !isSteady()) continue;
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        return new AllocationMeasurement(least, ms, measured);
    }
}
