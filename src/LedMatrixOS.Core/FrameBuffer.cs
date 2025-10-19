using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Core;

public sealed class FrameBuffer
{
    public int Width { get; }
    public int Height { get; }

    private readonly Pixel[] _pixels; // row-major

    public FrameBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
        Width = width;
        Height = height;
        _pixels = new Pixel[width * height];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Index(int x, int y) => y * Width + x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, Pixel pixel)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _pixels[Index(x, y)] = pixel;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pixel GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return Pixel.Black;
        return _pixels[Index(x, y)];
    }

    public void Clear(Pixel? pixel = null)
    {
        pixel ??= new Pixel(0, 0, 0);
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = pixel.Value;
    }

    public ReadOnlySpan<Pixel> GetPixelsSpan() => _pixels;

    public ReadOnlySpan<Pixel> GetPixelRowSpan(int y) => _pixels.AsSpan().Slice(Index(0, y));

    public void RenderImage(Image<Rgb24> image)
    {
        for (int y = 0; y < Math.Min(image.Height, Height); y++)
        {
            for (int x = 0; x < Math.Min(image.Width, Width); x++)
            {
                var pixel = image[x, y];
                SetPixel(x, y, new Pixel(pixel.R, pixel.G, pixel.B));
            }
        }
    }
}


