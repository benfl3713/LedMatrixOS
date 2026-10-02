using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Tests;

/// <summary>Drives a clock WidgetApp with a fake wall clock: advancing the harness advances frame time and wall time together.</summary>
internal sealed class ClocksHarness
{
    private long _frame;
    private TimeSpan _time;

    public ClocksHarness(WidgetApp app, DateTimeOffset start, int width = 256, int height = 64)
    {
        Fonts.Load();
        App = app;
        Clock = new FakeTime { Now = start };
        app.Time = Clock;
        app.OnActivatedAsync((height, width), new ConfigurationBuilder().Build(), CancellationToken.None).GetAwaiter().GetResult();
        Frame = new FrameBuffer(width, height);
    }

    public WidgetApp App { get; }
    public FakeTime Clock { get; }
    public FrameBuffer Frame { get; }

    /// <summary>Advances <paramref name="count"/> frames of <paramref name="ms"/> milliseconds (both the frame clock and the wall clock).</summary>
    public void Step(int ms, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var delta = TimeSpan.FromMilliseconds(ms);
            _time += delta;
            Clock.Now += delta;
            App.Update(new FrameContext(_time, delta, _frame++), CancellationToken.None);
        }
    }

    /// <summary>Jumps the wall clock (not the frame clock), e.g. across midnight.</summary>
    public void SetWall(DateTimeOffset now) => Clock.Now = now;

    /// <summary>Renders the current state into the reusable frame buffer.</summary>
    public FrameBuffer Render()
    {
        Frame.Clear(Pixel.Black);
        App.Render(Frame, CancellationToken.None);
        return Frame;
    }

    public FrameBuffer Snapshot()
    {
        Render();
        var copy = new FrameBuffer(Frame.Width, Frame.Height);
        copy.CopyFrom(Frame);
        return copy;
    }

    /// <summary>Golden-image check; when CLOCK_PREVIEW_DIR is set also writes a 4x LED-dot preview there for eyeballing.</summary>
    public static void Verify(FrameBuffer frame, string name, [System.Runtime.CompilerServices.CallerFilePath] string caller = "")
    {
        SnapshotHelper.AssertMatchesSnapshot(frame, name, caller);
        var dir = Environment.GetEnvironmentVariable("CLOCK_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        const int s = 4;
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(frame.Width * s, frame.Height * s);
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                var p = frame.GetPixel(x, y);
                for (int dy = 0; dy < s; dy++)
                    for (int dx = 0; dx < s; dx++)
                    {
                        bool gap = dx == s - 1 || dy == s - 1;
                        img[x * s + dx, y * s + dy] = gap
                            ? new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 255)
                            : new SixLabors.ImageSharp.PixelFormats.Rgba32(p.R, p.G, p.B, 255);
                    }
            }
        Directory.CreateDirectory(dir);
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img,Path.Combine(dir, name + ".png"));
    }

    public static DateTimeOffset At(int h, int m, int s, int ms = 0, int month = 10, int day = 1) =>
        new(2026, month, day, h, m, s, ms, TimeSpan.Zero);
}
