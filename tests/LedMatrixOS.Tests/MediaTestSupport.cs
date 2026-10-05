using LedMatrixOS.Core;
using LedMatrixOS.Core.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Tests;

/// <summary>A media library in a throw-away directory.</summary>
internal sealed class MediaFixture : IDisposable
{
    public MediaFixture(IVideoTranscoder? transcoder = null, Action<MediaConfig>? configure = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "ledmedia-" + Guid.NewGuid().ToString("N"));
        Config = new MediaConfig();
        configure?.Invoke(Config);
        Transcoder = transcoder;
        Library = new MediaLibrary(Root, Config, transcoder);
    }

    public string Root { get; }
    public MediaConfig Config { get; }
    public IVideoTranscoder? Transcoder { get; }
    public MediaLibrary Library { get; private set; }

    /// <summary>A second library over the same directory, as after a restart.</summary>
    public MediaLibrary Reopen() => Library = new MediaLibrary(Root, Config, Transcoder);

    public MediaItem Add(byte[] bytes, string? name = "test") =>
        Library.AddAsync(new MemoryStream(bytes), name).GetAwaiter().GetResult();

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Builds the pictures the media tests upload, in code.</summary>
internal static class MediaImages
{
    public static byte[] Png(int width, int height, Func<int, int, Rgba32> pixel)
    {
        using var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image[x, y] = pixel(x, y);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>A recognisable test card: red/green/blue/yellow quadrants, a white frame and a black cross at the centre.</summary>
    public static byte[] TestCard(int width = 160, int height = 80) => Png(width, height, (x, y) =>
    {
        if (x == 0 || y == 0 || x == width - 1 || y == height - 1) return new Rgba32(255, 255, 255);
        if (Math.Abs(x - width / 2) <= 1 || Math.Abs(y - height / 2) <= 1) return new Rgba32(0, 0, 0);
        bool right = x >= width / 2, bottom = y >= height / 2;
        return (right, bottom) switch
        {
            (false, false) => new Rgba32(220, 40, 40),
            (true, false) => new Rgba32(40, 200, 60),
            (false, true) => new Rgba32(50, 80, 230),
            _ => new Rgba32(230, 210, 40),
        };
    });

    /// <summary>A wide picture: a horizontal hue ramp with a white tick every 32 pixels, to see how far a scroll has moved.</summary>
    public static byte[] Panorama(int width = 512, int height = 64) => Png(width, height, (x, y) =>
    {
        if (x % 32 == 0) return new Rgba32(255, 255, 255);
        var p = Pixel.FromHsv(x * 360f / width, 0.9f, 0.9f - 0.4f * y / height);
        return new Rgba32(p.R, p.G, p.B);
    });

    /// <summary>An animated GIF: each frame a different colour with a white square that moves right. <paramref name="delayCs"/> is in hundredths of a second.</summary>
    public static byte[] Gif(int frames = 3, int delayCs = 50, int width = 64, int height = 32)
    {
        Rgba32[] colors = [new(200, 30, 30), new(30, 170, 50), new(40, 70, 220), new(210, 190, 30), new(150, 40, 190)];
        using var image = new Image<Rgba32>(width, height);
        for (int i = 0; i < frames; i++)
        {
            using var frame = new Image<Rgba32>(width, height, colors[i % colors.Length]);
            int sx = 4 + i * 10;
            for (int y = 10; y < 22; y++)
                for (int x = sx; x < sx + 8 && x < width; x++)
                    frame[x, y] = new Rgba32(255, 255, 255);
            frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = delayCs;
            if (i == 0)
            {
                image.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = delayCs;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        image[x, y] = frame[x, y];
            }
            else
            {
                image.Frames.AddFrame(frame.Frames.RootFrame);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsGif(stream, new GifEncoder());
        return stream.ToArray();
    }

    /// <summary>The first bytes of an MP4 file, enough to be recognised as video by content.</summary>
    public static byte[] Mp4Header() => [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0, (byte)'i', (byte)'s', (byte)'o', (byte)'m', 1, 2, 3, 4, 5, 6, 7, 8];
}

/// <summary>Stands in for ffmpeg: writes 10 solid frames whose red level rises with the frame number.</summary>
internal sealed class FakeTranscoder(bool available = true) : IVideoTranscoder
{
    public bool IsAvailable => available;
    public int Calls { get; private set; }

    public Task<VideoTranscodeResult> TranscodeAsync(string inputPath, string outputPath, int width, int height, int maxSeconds, int maxFps, CancellationToken ct)
    {
        Calls++;
        var frames = Enumerable.Range(0, 10).Select(i =>
        {
            var data = new byte[width * height * 3];
            for (int p = 0; p < width * height; p++) { data[p * 3] = (byte)(20 + i * 20); data[p * 3 + 1] = 40; data[p * 3 + 2] = 90; }
            return data;
        });
        return Task.FromResult(RawVideoFile.Write(outputPath, width, height, 10, frames));
    }
}
