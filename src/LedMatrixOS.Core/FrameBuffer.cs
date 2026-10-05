using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Core;

public sealed class FrameBuffer
{
    public int Width { get; }
    public int Height { get; }

    private readonly Pixel[] _pixels; // row-major

    // Active clip rectangle (max exclusive); always within the buffer bounds.
    private int _clipX0, _clipY0, _clipX1, _clipY1;
    private readonly Stack<Rectangle> _clipStack = new();

    public FrameBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
        Width = width;
        Height = height;
        _pixels = new Pixel[width * height];
        _clipX1 = width;
        _clipY1 = height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Index(int x, int y) => y * Width + x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, Pixel pixel)
    {
        if (x < _clipX0 || x >= _clipX1 || y < _clipY0 || y >= _clipY1) return;
        _pixels[Index(x, y)] = pixel;
    }

    /// <summary>
    /// Draws <paramref name="color"/> over the existing pixel with the given opacity (0-1).
    /// </summary>
    public void BlendPixel(int x, int y, Pixel color, float alpha)
    {
        if (x < _clipX0 || x >= _clipX1 || y < _clipY0 || y >= _clipY1) return;
        ref var dst = ref _pixels[Index(x, y)];
        dst = dst.Blend(color, alpha);
    }

    /// <summary>
    /// Restricts drawing to the given rectangle (intersected with the current clip) until <see cref="PopClip"/>.
    /// </summary>
    public void PushClip(Rectangle rect)
    {
        _clipStack.Push(new Rectangle(_clipX0, _clipY0, _clipX1 - _clipX0, _clipY1 - _clipY0));
        _clipX0 = Math.Max(_clipX0, rect.Left);
        _clipY0 = Math.Max(_clipY0, rect.Top);
        _clipX1 = Math.Max(_clipX0, Math.Min(_clipX1, rect.Right));
        _clipY1 = Math.Max(_clipY0, Math.Min(_clipY1, rect.Bottom));
    }

    public void PopClip()
    {
        if (_clipStack.Count == 0) throw new InvalidOperationException("PopClip without matching PushClip");
        var r = _clipStack.Pop();
        _clipX0 = r.Left; _clipY0 = r.Top; _clipX1 = r.Right; _clipY1 = r.Bottom;
    }

    /// <summary>
    /// Fills a rectangle with a solid colour (clipped).
    /// </summary>
    public void Fill(Rectangle rect, Pixel color)
    {
        int x0 = Math.Max(rect.Left, _clipX0), x1 = Math.Min(rect.Right, _clipX1);
        int y0 = Math.Max(rect.Top, _clipY0), y1 = Math.Min(rect.Bottom, _clipY1);
        if (x0 >= x1) return;
        for (int y = y0; y < y1; y++)
            _pixels.AsSpan(Index(x0, y), x1 - x0).Fill(color);
    }

    /// <summary>
    /// Copies another buffer onto this one with its origin at (dx, dy) (clipped).
    /// </summary>
    public void CopyFrom(FrameBuffer other, int dx = 0, int dy = 0)
    {
        int x0 = Math.Max(Math.Max(dx, 0), _clipX0), x1 = Math.Min(Math.Min(dx + other.Width, Width), _clipX1);
        int y0 = Math.Max(Math.Max(dy, 0), _clipY0), y1 = Math.Min(Math.Min(dy + other.Height, Height), _clipY1);
        if (x0 >= x1) return;
        for (int y = y0; y < y1; y++)
            other._pixels.AsSpan(other.Index(x0 - dx, y - dy), x1 - x0).CopyTo(_pixels.AsSpan(Index(x0, y)));
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
}
