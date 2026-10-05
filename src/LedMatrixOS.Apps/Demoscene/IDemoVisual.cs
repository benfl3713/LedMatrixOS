using System.Runtime.CompilerServices;
using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>Per-frame inputs shared by every demoscene effect.</summary>
internal readonly struct DemoFrame
{
    /// <summary>Speed-scaled animation clock in seconds (continuous even when the Speed setting changes).</summary>
    public required float T { get; init; }

    /// <summary>Speed-scaled frame delta in seconds.</summary>
    public required float Dt { get; init; }

    /// <summary>Size multiplier (about 0.4 to 2.4): bigger means bigger features, a larger cube, a deeper zoom.</summary>
    public required float Scale { get; init; }

    public required ColorRamp Ramp { get; init; }
}

/// <summary>One demoscene effect. It owns its lookup tables (built in <see cref="Resize"/>) and paints into a plain pixel array.</summary>
internal interface IDemoVisual
{
    string Name { get; }

    /// <summary>(Re)builds size dependent tables. Called once per display size, never per frame.</summary>
    void Resize(int width, int height);

    /// <summary>Paints every pixel of <paramref name="dst"/> (row major, width x height). Must not allocate.</summary>
    void Draw(Pixel[] dst, in DemoFrame f);
}

/// <summary>Small drawing helpers on raw pixel arrays.</summary>
internal static class DemoCanvas
{
    /// <summary>Palette index (0..255) for a phase in turns that ping-pongs through the ramp, so any ramp loops seamlessly.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PingPong(float turns)
    {
        float f = turns - MathF.Floor(turns);
        float tri = f < 0.5f ? f * 2f : 2f - f * 2f;
        return (int)(tri * 255f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Pixel Shade(Pixel c, float k) => new(Kit.ToByte(c.R * k), Kit.ToByte(c.G * k), Kit.ToByte(c.B * k));

    /// <summary>Adds colour (clamped), a cheap light-emission blend.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(Pixel[] d, int w, int h, int x, int y, Pixel c, float k)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
        ref var p = ref d[y * w + x];
        p = new Pixel(Kit.ToByte(p.R + c.R * k), Kit.ToByte(p.G + c.G * k), Kit.ToByte(p.B + c.B * k));
    }

    /// <summary>Alpha blends a colour over the pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Over(Pixel[] d, int w, int h, int x, int y, Pixel c, float a)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h || a <= 0f) return;
        if (a > 1f) a = 1f;
        ref var p = ref d[y * w + x];
        p = new Pixel(Kit.ToByte(p.R + (c.R - p.R) * a), Kit.ToByte(p.G + (c.G - p.G) * a), Kit.ToByte(p.B + (c.B - p.B) * a));
    }

    /// <summary>Anti-aliased (Xiaolin Wu) line. <paramref name="additive"/> adds light, otherwise it blends over.</summary>
    public static void Line(Pixel[] d, int w, int h, float x0, float y0, float x1, float y1, Pixel c, float k, bool additive)
    {
        bool steep = MathF.Abs(y1 - y0) > MathF.Abs(x1 - x0);
        if (steep) LineCore(d, w, h, y0, x0, y1, x1, true, c, k, additive);
        else LineCore(d, w, h, x0, y0, x1, y1, false, c, k, additive);
    }

    private static void LineCore(Pixel[] d, int w, int h, float x0, float y0, float x1, float y1, bool steep, Pixel c, float k, bool additive)
    {
        if (x0 > x1) { (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); }
        float dx = x1 - x0, dy = y1 - y0;
        float grad = dx < 1e-4f ? 0f : dy / dx;
        int xs = (int)MathF.Floor(x0), xe = (int)MathF.Floor(x1);
        if (xe - xs > 1200) return; // absurdly long projection; never needed on a real display
        float y = y0 + grad * (xs - x0);
        for (int x = xs; x <= xe; x++, y += grad)
        {
            int yi = (int)MathF.Floor(y);
            float fr = y - yi;
            if (steep) { Plot(d, w, h, yi, x, c, (1f - fr) * k, additive); Plot(d, w, h, yi + 1, x, c, fr * k, additive); }
            else { Plot(d, w, h, x, yi, c, (1f - fr) * k, additive); Plot(d, w, h, x, yi + 1, c, fr * k, additive); }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Plot(Pixel[] d, int w, int h, int x, int y, Pixel c, float a, bool additive)
    {
        if (additive) Add(d, w, h, x, y, c, a); else Over(d, w, h, x, y, c, a);
    }
}
