using BdfFontParser;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Toys;

/// <summary>Named colour palettes shared by the toy apps. Colours are bright and saturated so they survive an LED panel.</summary>
public static class ToyPalettes
{
    public static readonly string[] Names = ["Neon", "Sunset", "Ocean", "Candy", "Aurora", "Rainbow", "Mono"];

    private static readonly Pixel[] Neon = [P(255, 40, 120), P(255, 200, 0), P(40, 255, 120), P(0, 200, 255), P(170, 70, 255), P(255, 110, 20)];
    private static readonly Pixel[] Sunset = [P(255, 70, 40), P(255, 140, 0), P(255, 215, 60), P(255, 60, 130), P(200, 40, 220), P(255, 100, 80)];
    private static readonly Pixel[] Ocean = [P(0, 190, 255), P(0, 255, 200), P(60, 120, 255), P(120, 255, 240), P(0, 140, 200), P(150, 200, 255)];
    private static readonly Pixel[] Candy = [P(255, 90, 170), P(120, 255, 230), P(255, 240, 90), P(190, 130, 255), P(255, 150, 90), P(110, 220, 120)];
    private static readonly Pixel[] Aurora = [P(60, 255, 130), P(0, 220, 200), P(90, 140, 255), P(180, 90, 255), P(255, 80, 200), P(160, 255, 60)];

    private static readonly Pixel[] Rainbow = [P(255, 40, 40), P(255, 190, 0), P(60, 255, 60), P(0, 220, 255), P(90, 90, 255), P(255, 60, 220)];
    private static readonly Pixel[] Mono = [P(255, 255, 255), P(200, 205, 220), P(150, 155, 175), P(235, 240, 255), P(110, 115, 135), P(180, 185, 200)];

    private static Pixel P(byte r, byte g, byte b) => new(r, g, b);

    /// <summary>The palette for a name (any casing; unknown names give Neon). Allocation-free.</summary>
    public static Pixel[] Get(string? name)
    {
        if (name is null) return Neon;
        if (name.Equals("Sunset", StringComparison.OrdinalIgnoreCase)) return Sunset;
        if (name.Equals("Ocean", StringComparison.OrdinalIgnoreCase)) return Ocean;
        if (name.Equals("Candy", StringComparison.OrdinalIgnoreCase)) return Candy;
        if (name.Equals("Aurora", StringComparison.OrdinalIgnoreCase)) return Aurora;
        if (name.Equals("Rainbow", StringComparison.OrdinalIgnoreCase)) return Rainbow;
        if (name.Equals("Mono", StringComparison.OrdinalIgnoreCase)) return Mono;
        return Neon;
    }

    /// <summary>Smooth cyclic lookup; <paramref name="t"/> wraps around the palette.</summary>
    public static Pixel Sample(Pixel[] palette, float t)
    {
        float f = t - MathF.Floor(t);
        f *= palette.Length;
        int i = (int)f;
        if (i >= palette.Length) i = palette.Length - 1;
        return Pixel.Lerp(palette[i], palette[(i + 1) % palette.Length], f - i);
    }
}

/// <summary>Turns variable frame deltas into a whole number of fixed physics steps so simulations do not depend on frame rate.</summary>
public struct FixedStepper
{
    public const float Step = 1f / 120f;
    private float _acc;

    /// <summary>Adds a frame delta (seconds) and returns how many fixed steps to run now (at most 12).</summary>
    public int Advance(float dt)
    {
        _acc += Math.Clamp(dt, 0f, 0.1f);
        int n = (int)(_acc / Step);
        if (n > 12) n = 12;
        _acc -= n * Step;
        if (_acc > Step) _acc = 0;
        return n;
    }

    public void Reset() => _acc = 0;
}

/// <summary>An RGB accumulation plane for motion trails: decays towards black, takes max-blended splats, and composites additively.</summary>
public sealed class TrailPlane
{
    private readonly Pixel[] _px;
    public int Width { get; }
    public int Height { get; }

    public TrailPlane(int width, int height)
    {
        Width = width;
        Height = height;
        _px = new Pixel[width * height];
    }

    public void Clear() => Array.Clear(_px);

    /// <summary>Multiplies every pixel by <paramref name="keep"/> (0-1).</summary>
    public void Decay(float keep)
    {
        int k = (int)(Math.Clamp(keep, 0f, 1f) * 256f);
        var px = _px;
        for (int i = 0; i < px.Length; i++)
        {
            var p = px[i];
            if ((p.R | p.G | p.B) == 0) continue;
            px[i] = new Pixel((byte)(p.R * k >> 8), (byte)(p.G * k >> 8), (byte)(p.B * k >> 8));
        }
    }

    /// <summary>Soft disc at fractional coordinates, max-blended so overlapping stamps do not saturate.</summary>
    public void Stamp(float cx, float cy, float radius, Pixel color, float intensity)
    {
        int x0 = Math.Max(0, (int)MathF.Floor(cx - radius - 1)), x1 = Math.Min(Width - 1, (int)MathF.Ceiling(cx + radius + 1));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - radius - 1)), y1 = Math.Min(Height - 1, (int)MathF.Ceiling(cy + radius + 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx, dy = y - cy;
                float cov = Math.Clamp(radius + 0.5f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f) * intensity;
                if (cov <= 0f) continue;
                ref var d = ref _px[y * Width + x];
                d = new Pixel(
                    Math.Max(d.R, (byte)(color.R * cov)),
                    Math.Max(d.G, (byte)(color.G * cov)),
                    Math.Max(d.B, (byte)(color.B * cov)));
            }
    }

    /// <summary>Max-blends one pixel (used to stamp a mask-shaped ghost).</summary>
    public void StampPixel(int x, int y, Pixel color, float intensity)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || intensity <= 0f) return;
        ref var d = ref _px[y * Width + x];
        d = new Pixel(
            Math.Max(d.R, (byte)(color.R * intensity)),
            Math.Max(d.G, (byte)(color.G * intensity)),
            Math.Max(d.B, (byte)(color.B * intensity)));
    }

    public void AddTo(FrameBuffer frame)
    {
        int w = Math.Min(Width, frame.Width), h = Math.Min(Height, frame.Height);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = _px[y * Width + x];
                if ((t.R | t.G | t.B) == 0) continue;
                var f = frame.GetPixel(x, y);
                frame.SetPixel(x, y, new Pixel(
                    (byte)Math.Min(255, f.R + t.R), (byte)Math.Min(255, f.G + t.G), (byte)Math.Min(255, f.B + t.B)));
            }
    }
}

/// <summary>Allocation-free drawing helpers (no ImageSharp, no closures) for the toy apps.</summary>
public static class ToyGfx
{
    /// <summary>Adds <paramref name="color"/> scaled by <paramref name="k"/> to the pixel (neon-style additive light).</summary>
    public static void AddPixel(FrameBuffer frame, int x, int y, Pixel color, float k)
    {
        if (k <= 0f || (uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) return;
        var f = frame.GetPixel(x, y);
        frame.SetPixel(x, y, new Pixel(
            (byte)Math.Min(255f, f.R + color.R * k), (byte)Math.Min(255f, f.G + color.G * k), (byte)Math.Min(255f, f.B + color.B * k)));
    }

    /// <summary>Antialiased additive line (Xiaolin Wu) with fractional endpoints.</summary>
    public static void AddLine(FrameBuffer frame, float x0, float y0, float x1, float y1, Pixel color, float k = 1f)
    {
        if (!float.IsFinite(x0 + y0 + x1 + y1)) return;
        bool steep = MathF.Abs(y1 - y0) > MathF.Abs(x1 - x0);
        if (steep) { (x0, y0) = (y0, x0); (x1, y1) = (y1, x1); }
        if (x0 > x1) { (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); }
        float dx = x1 - x0, dy = y1 - y0;
        float grad = dx < 0.0001f ? 1f : dy / dx;
        int xs = (int)MathF.Round(x0), xe = (int)MathF.Round(x1);
        if (xe - xs > 800) return;
        float y = y0 + grad * (xs - x0);
        for (int x = xs; x <= xe; x++)
        {
            int iy = (int)MathF.Floor(y);
            float f = y - iy;
            if (steep)
            {
                AddPixel(frame, iy, x, color, (1 - f) * k);
                AddPixel(frame, iy + 1, x, color, f * k);
            }
            else
            {
                AddPixel(frame, x, iy, color, (1 - f) * k);
                AddPixel(frame, x, iy + 1, color, f * k);
            }
            y += grad;
        }
    }

    /// <summary>Antialiased additive disc.</summary>
    public static void AddDisc(FrameBuffer frame, float cx, float cy, float r, Pixel color, float k = 1f)
    {
        int x0 = (int)MathF.Floor(cx - r - 1), x1 = (int)MathF.Ceiling(cx + r + 1);
        int y0 = (int)MathF.Floor(cy - r - 1), y1 = (int)MathF.Ceiling(cy + r + 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx, dy = y - cy;
                AddPixel(frame, x, y, color, Math.Clamp(r + 0.5f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f) * k);
            }
    }

    /// <summary>
    /// A lit, glossy ball squashed or stretched into an ellipse with half-axes (<paramref name="ra"/> along the axis angle,
    /// <paramref name="rb"/> across it). The edge is antialiased and shading gives a highlight so it reads as a sphere from across a room.
    /// </summary>
    public static void ShadedEllipse(FrameBuffer frame, float cx, float cy, float ra, float rb, float axisAngle, Pixel color, float alpha = 1f)
    {
        float rmax = MathF.Max(ra, rb);
        float ca = MathF.Cos(axisAngle), sa = MathF.Sin(axisAngle);
        int x0 = (int)MathF.Floor(cx - rmax - 1), x1 = (int)MathF.Ceiling(cx + rmax + 1);
        int y0 = (int)MathF.Floor(cy - rmax - 1), y1 = (int)MathF.Ceiling(cy + rmax + 1);
        float rmin = MathF.Min(ra, rb);
        var dark = color.WithBrightness(0.45f);
        var light = Pixel.Lerp(color, Pixel.White, 0.55f);
        for (int y = y0; y <= y1; y++)
        {
            if ((uint)y >= (uint)frame.Height) continue;
            for (int x = x0; x <= x1; x++)
            {
                if ((uint)x >= (uint)frame.Width) continue;
                float dx = x - cx, dy = y - cy;
                float u = dx * ca + dy * sa, v = -dx * sa + dy * ca;
                float d = MathF.Sqrt(u * u / (ra * ra) + v * v / (rb * rb));
                float cov = Math.Clamp((1f - d) * rmin + 0.5f, 0f, 1f);
                if (cov <= 0f) continue;
                float nx = dx / rmax, ny = dy / rmax;
                float lit = Math.Clamp(0.5f - nx * 0.45f - ny * 0.55f, 0f, 1f);
                float spec = Math.Clamp(1f - MathF.Sqrt((nx + 0.38f) * (nx + 0.38f) + (ny + 0.4f) * (ny + 0.4f)) * 3.2f, 0f, 1f);
                var body = lit < 0.5f ? Pixel.Lerp(dark, color, lit * 2f) : Pixel.Lerp(color, light, (lit - 0.5f) * 1.2f);
                if (spec > 0f) body = Pixel.Lerp(body, Pixel.White, spec * 0.85f);
                if (alpha >= 0.999f && cov >= 0.999f) frame.SetPixel(x, y, body);
                else frame.BlendPixel(x, y, body, cov * alpha);
            }
        }
    }

    /// <summary>One-pixel rectangle outline as additive light.</summary>
    public static void AddRectOutline(FrameBuffer frame, int x, int y, int w, int h, Pixel color, float k)
    {
        for (int i = 0; i < w; i++) { AddPixel(frame, x + i, y, color, k); AddPixel(frame, x + i, y + h - 1, color, k); }
        for (int j = 1; j < h - 1; j++) { AddPixel(frame, x, y + j, color, k); AddPixel(frame, x + w - 1, y + j, color, k); }
    }
}


/// <summary>
/// The shared speed convention of the toy apps: a 1-10 level (5 is the normal pace). Older versions of Dvd Logo and Geometric Patterns
/// stored a percentage (20-400), which <see cref="LevelFromPercent"/> maps onto the nearest level when such a value is loaded.
/// </summary>
public static class ToySpeed
{
    private static readonly int[] Percents = [25, 50, 70, 85, 100, 125, 150, 200, 300, 400];

    /// <summary>Animation speed percentage (100 = normal) for a 1-10 level.</summary>
    public static int Percent(int level) => Percents[Math.Clamp(level, 1, 10) - 1];

    /// <summary>The 1-10 level whose percentage is nearest <paramref name="percent"/>.</summary>
    public static int LevelFromPercent(int percent)
    {
        int best = 1, bestDiff = int.MaxValue;
        for (int i = 0; i < Percents.Length; i++)
        {
            int d = Math.Abs(Percents[i] - percent);
            if (d < bestDiff) { bestDiff = d; best = i + 1; }
        }
        return best;
    }

    /// <summary>
    /// Converts a stored/posted speed to a level: values above 10 can only be old percentages (the new range tops out at 10), so they are
    /// migrated; 1-10 are levels already. (An old 10 percent, the lowest Geometric Patterns ever allowed, reads as level 10: accepted ambiguity.)
    /// </summary>
    public static object Migrate(object value)
    {
        int n = LedMatrixOS.Core.Settings.SettingsBinder.CoerceInt(value, -1);
        return n > 10 ? LevelFromPercent(n) : value;
    }
}
