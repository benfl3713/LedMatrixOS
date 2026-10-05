using System.Runtime.CompilerServices;
using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LedMatrixOS.Tests;

/// <summary>
/// Golden-image snapshot helper. Goldens live in tests/LedMatrixOS.Tests/Snapshots/.
/// Missing golden or UPDATE_SNAPSHOTS=1 writes the golden and passes; otherwise pixel-exact compare,
/// and on mismatch an *.actual.png is written next to the golden.
/// </summary>
public static class SnapshotHelper
{
    public const int DefaultWidth = 256;
    public const int DefaultHeight = 64;

    public static FrameBuffer Render(Action<FrameBuffer> draw, int width = DefaultWidth, int height = DefaultHeight)
    {
        var frame = new FrameBuffer(width, height);
        draw(frame);
        return frame;
    }

    public static FrameBuffer RenderApp(IMatrixApp app, int width = DefaultWidth, int height = DefaultHeight,
        TimeSpan? delta = null)
    {
        app.OnActivatedAsync((height, width), new ConfigurationBuilder().Build(), CancellationToken.None)
            .GetAwaiter().GetResult();
        var step = delta ?? TimeSpan.FromMilliseconds(16);
        app.Update(new FrameContext(step, step, 0), CancellationToken.None);
        return Render(f => app.Render(f, CancellationToken.None), width, height);
    }

    public static bool IsBlank(FrameBuffer frame)
    {
        foreach (var p in frame.GetPixelsSpan())
            if (p.R != 0 || p.G != 0 || p.B != 0) return false;
        return true;
    }

    /// <summary>Mirrors SimulatedMatrixDevice (RGBA32 canvas, opaque, full brightness) so PNGs match the preview.</summary>
    public static Image<Rgba32> ToImage(FrameBuffer frame)
    {
        var image = new Image<Rgba32>(frame.Width, frame.Height);
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                var p = frame.GetPixel(x, y);
                image[x, y] = new Rgba32(p.R, p.G, p.B, 255);
            }
        return image;
    }

    public static void AssertMatchesSnapshot(FrameBuffer frame, string name, [CallerFilePath] string callerFile = "")
    {
        var dir = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        Directory.CreateDirectory(dir);
        var goldenPath = Path.Combine(dir, name + ".png");
        var actualPath = Path.Combine(dir, name + ".actual.png");

        using var actual = ToImage(frame);
        var update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

        if (update || !File.Exists(goldenPath))
        {
            actual.SaveAsPng(goldenPath);
            if (File.Exists(actualPath)) File.Delete(actualPath);
            return;
        }

        using var golden = Image.Load<Rgba32>(goldenPath);
        string? diff = Compare(golden, actual);
        if (diff == null)
        {
            if (File.Exists(actualPath)) File.Delete(actualPath);
            return;
        }

        actual.SaveAsPng(actualPath);
        Assert.Fail($"Snapshot '{name}' does not match golden. {diff} Actual written to {actualPath}. " +
                    "If the change is intended, re-run with UPDATE_SNAPSHOTS=1.");
    }

    private static string? Compare(Image<Rgba32> golden, Image<Rgba32> actual)
    {
        if (golden.Width != actual.Width || golden.Height != actual.Height)
            return $"Size differs: golden {golden.Width}x{golden.Height}, actual {actual.Width}x{actual.Height}.";

        int count = 0;
        (int x, int y)? first = null;
        for (int y = 0; y < golden.Height; y++)
            for (int x = 0; x < golden.Width; x++)
                if (golden[x, y] != actual[x, y])
                {
                    count++;
                    first ??= (x, y);
                }
        return count == 0 ? null : $"{count} pixel(s) differ, first at ({first!.Value.x},{first.Value.y}).";
    }
}
