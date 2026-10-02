using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Graphics;

/// <summary>
/// A small image (optionally multi-frame) decoded once into pixel arrays, so drawing never touches ImageSharp.
/// </summary>
public sealed class Sprite
{
    public readonly record struct SpriteFrame(Pixel[] Pixels, byte[] Alpha, TimeSpan Duration);

    private static readonly TimeSpan DefaultFrameDuration = TimeSpan.FromMilliseconds(100);

    private readonly TimeSpan _totalDuration;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<SpriteFrame> Frames { get; }

    public Sprite(int width, int height, IReadOnlyList<SpriteFrame> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("A sprite needs at least one frame", nameof(frames));
        Width = width;
        Height = height;
        Frames = frames;
        foreach (var frame in frames) _totalDuration += frame.Duration;
    }

    public static Sprite Load(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        return FromImage(image);
    }

    public static Sprite Load(Stream stream)
    {
        using var image = Image.Load<Rgba32>(stream);
        return FromImage(image);
    }

    public static Sprite FromImage(Image<Rgba32> image)
    {
        var frames = new List<SpriteFrame>(image.Frames.Count);
        for (int f = 0; f < image.Frames.Count; f++)
        {
            var source = image.Frames[f];
            var pixels = new Pixel[image.Width * image.Height];
            var alpha = new byte[pixels.Length];
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var p = source[x, y];
                    pixels[y * image.Width + x] = new Pixel(p.R, p.G, p.B);
                    alpha[y * image.Width + x] = p.A;
                }
            }

            // GIF delay is in 1/100s; zero or missing falls back to a default
            var delay = source.Metadata.TryGetGifMetadata(out GifFrameMetadata? gif) && gif.FrameDelay > 0
                ? TimeSpan.FromMilliseconds(gif.FrameDelay * 10)
                : DefaultFrameDuration;
            frames.Add(new SpriteFrame(pixels, alpha, delay));
        }

        return new Sprite(image.Width, image.Height, frames);
    }

    /// <summary>
    /// Index of the frame to show after <paramref name="elapsed"/>, looping.
    /// </summary>
    public int FrameIndexAt(TimeSpan elapsed)
    {
        if (Frames.Count == 1 || _totalDuration <= TimeSpan.Zero) return 0;

        var t = TimeSpan.FromTicks(elapsed.Ticks % _totalDuration.Ticks);
        for (int i = 0; i < Frames.Count; i++)
        {
            if (t < Frames[i].Duration) return i;
            t -= Frames[i].Duration;
        }

        return Frames.Count - 1;
    }
}
