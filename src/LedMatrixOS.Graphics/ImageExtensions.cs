using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Graphics;

public static class ImageExtensions
{
    public static void DrawSprite(this FrameBuffer frame, Sprite sprite, int x, int y, int frameIndex = 0)
    {
        var f = sprite.Frames[Math.Clamp(frameIndex, 0, sprite.Frames.Count - 1)];
        for (int sy = 0; sy < sprite.Height; sy++)
        {
            for (int sx = 0; sx < sprite.Width; sx++)
            {
                int i = sy * sprite.Width + sx;
                DrawWithAlpha(frame, x + sx, y + sy, f.Pixels[i], f.Alpha[i]);
            }
        }
    }

    /// <summary>
    /// Draws the sprite frame that is current after <paramref name="elapsed"/> (looping playback).
    /// </summary>
    public static void DrawSprite(this FrameBuffer frame, Sprite sprite, int x, int y, TimeSpan elapsed)
        => frame.DrawSprite(sprite, x, y, sprite.FrameIndexAt(elapsed));

    public static void DrawImage(this FrameBuffer frame, Image<Rgba32> image, int x, int y)
    {
        for (int iy = 0; iy < image.Height; iy++)
        {
            for (int ix = 0; ix < image.Width; ix++)
            {
                var p = image[ix, iy];
                DrawWithAlpha(frame, x + ix, y + iy, new Pixel(p.R, p.G, p.B), p.A);
            }
        }
    }

    public static void DrawImage(this FrameBuffer frame, Image<Rgb24> image, int x, int y)
    {
        for (int iy = 0; iy < image.Height; iy++)
        {
            for (int ix = 0; ix < image.Width; ix++)
            {
                var p = image[ix, iy];
                frame.SetPixel(x + ix, y + iy, new Pixel(p.R, p.G, p.B));
            }
        }
    }

    private static void DrawWithAlpha(FrameBuffer frame, int x, int y, Pixel color, byte alpha)
    {
        if (alpha == 0) return;
        if (alpha == 255) frame.SetPixel(x, y, color);
        else frame.BlendPixel(x, y, color, alpha / 255f);
    }
}
