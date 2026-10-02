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
