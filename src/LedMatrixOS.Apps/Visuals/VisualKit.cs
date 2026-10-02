using System.Runtime.CompilerServices;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Visuals;

/// <summary>
/// Base for the full-screen generative nodes (fire, rain, spiral, equalizer). It captures frame time in <see cref="Update"/>
/// (never reads the clock), clamps huge deltas so a stall cannot explode a simulation, and calls <see cref="Step"/>.
/// Concrete nodes size their buffers lazily from the bounds they are painted into and never allocate in steady state.
/// </summary>
internal abstract class VisualNode : Node
{
    /// <summary>Seconds since the previous frame, clamped to 50 ms.</summary>
    protected float Dt { get; private set; }

    /// <summary>Seconds since the render engine started.</summary>
    protected float Time { get; private set; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        Dt = MathF.Min((float)ctx.Delta.TotalSeconds, 0.05f);
        Time = (float)ctx.Time.TotalSeconds;
        Step(ctx);
    }

    protected abstract void Step(FrameContext ctx);
}

/// <summary>256-entry colour lookup built from gradient stops; the workhorse of every shader loop here.</summary>
internal sealed class ColorRamp
{
    public const int Size = 256;

    public Pixel[] Colors { get; } = new Pixel[Size];

    public static ColorRamp FromStops(params (float at, Pixel color)[] stops)
    {
        var ramp = new ColorRamp();
        ramp.Fill(stops);
        return ramp;
    }

    public void Fill(ReadOnlySpan<(float at, Pixel color)> stops)
    {
        for (int i = 0; i < Size; i++)
        {
            float t = i / (float)(Size - 1);
            int s = 0;
            while (s < stops.Length - 2 && t > stops[s + 1].at) s++;
            var a = stops[s];
            var b = stops[Math.Min(s + 1, stops.Length - 1)];
            float span = b.at - a.at;
            float f = span <= 1e-6f ? 1f : Math.Clamp((t - a.at) / span, 0f, 1f);
            Colors[i] = Pixel.Lerp(a.color, b.color, f);
        }
    }

    /// <summary>t in 0..1, clamped.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pixel Sample(float t)
    {
        int i = (int)(t * (Size - 1) + 0.5f);
        return Colors[i < 0 ? 0 : i >= Size ? Size - 1 : i];
    }

    /// <summary>Wraps t around (for cyclic palettes like hue).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pixel SampleWrapped(float t)
    {
        t -= MathF.Floor(t);
        return Colors[(int)(t * (Size - 1) + 0.5f)];
    }
}

internal static class Kit
{
    public const float TwoPi = MathF.PI * 2f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToByte(float v) => (byte)(v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f));

    /// <summary>Adds colour to whatever is already there (clamped); a cheap light-emission blend.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(FrameBuffer f, int x, int y, float r, float g, float b)
    {
        if ((uint)x >= (uint)f.Width || (uint)y >= (uint)f.Height) return;
        var p = f.GetPixel(x, y);
        f.SetPixel(x, y, new Pixel(ToByte(p.R + r), ToByte(p.G + g), ToByte(p.B + b)));
    }

    /// <summary>Adds a pixel colour scaled by <paramref name="k"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(FrameBuffer f, int x, int y, Pixel c, float k) => Add(f, x, y, c.R * k, c.G * k, c.B * k);

    /// <summary>Scales a colour; k above 1 brightens (clamped).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Pixel Scale(Pixel c, float k) => new(ToByte(c.R * k), ToByte(c.G * k), ToByte(c.B * k));

    /// <summary>Integer hash to 0..1 (stable, no state).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Hash(int x, int y, int seed = 0)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>
    /// Fills a square, x- and y-tileable value-noise texture (0..1) with two octaves. Built once at activation.
    /// </summary>
    public static float[] TileableNoise(int size, int cell, Random rng)
    {
        int lat = size / cell;
        var l1 = new float[lat * lat];
        var l2 = new float[lat * lat * 4];
        for (int i = 0; i < l1.Length; i++) l1[i] = rng.NextSingle();
        for (int i = 0; i < l2.Length; i++) l2[i] = rng.NextSingle();
        var tex = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex[y * size + x] = 0.68f * Sample(l1, lat, x / (float)cell, y / (float)cell)
                                    + 0.32f * Sample(l2, lat * 2, x / (cell * 0.5f), y / (cell * 0.5f));
        // Value noise clusters around 0.5; stretch it to the full 0..1 so callers get real contrast.
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in tex) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        float inv = 1f / Math.Max(hi - lo, 1e-4f);
        for (int i = 0; i < tex.Length; i++) tex[i] = (tex[i] - lo) * inv;
        return tex;

        static float Sample(float[] lattice, int n, float fx, float fy)
        {
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
            float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
            int xa = x0 % n, ya = y0 % n, xb = (x0 + 1) % n, yb = (y0 + 1) % n;
            float a = Lerp(lattice[ya * n + xa], lattice[ya * n + xb], tx);
            float b = Lerp(lattice[yb * n + xa], lattice[yb * n + xb], tx);
            return Lerp(a, b, ty);
        }
    }

    /// <summary>Full-colour hue ramp (saturation/value 1) used by palettes.</summary>
    public static ColorRamp HueRamp(float saturation = 1f, float value = 1f)
    {
        var r = new ColorRamp();
        for (int i = 0; i < ColorRamp.Size; i++) r.Colors[i] = Pixel.FromHsv(i * 360f / ColorRamp.Size, saturation, value);
        return r;
    }

    public static Rectangle Full(FrameBuffer f) => new(0, 0, f.Width, f.Height);
}

/// <summary>Sine/cosine from a 1024-entry table; plenty accurate for visuals and far cheaper than MathF per pixel.</summary>
internal static class FastTrig
{
    private const int N = 1024;
    private const int Mask = N - 1;
    private static readonly float[] Table = BuildTable();

    private static float[] BuildTable()
    {
        var t = new float[N + 1];
        for (int i = 0; i <= N; i++) t[i] = MathF.Sin(i * Kit.TwoPi / N);
        return t;
    }

    /// <summary>Sine of a phase measured in turns (1 = one full cycle), with linear interpolation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sin(float turns)
    {
        float p = (turns - MathF.Floor(turns)) * N;
        int i = (int)p;
        float f = p - i;
        return Table[i & Mask] + (Table[(i & Mask) + 1] - Table[i & Mask]) * f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cos(float turns) => Sin(turns + 0.25f);
}
