using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Toys;

/// <summary>One animated geometry style. Renders a full frame (it clears the target itself) at animation time <c>t</c> seconds.</summary>
public abstract class GeoPattern
{
    public const int Cell = 64;

    public abstract string Name { get; }

    public abstract void Render(FrameBuffer f, float t, Pixel[] pal);

    protected static float Frac(float v) => v - MathF.Floor(v);
}

/// <summary>Four flowers of epitrochoid curves breathing and counter-rotating, drawn as neon light.</summary>
public sealed class SpirographPattern : GeoPattern
{
    private static readonly int[] Ks = [3, 5, 4, 7];
    private const int Points = 320;

    public override string Name => "spirograph";

    public override void Render(FrameBuffer f, float t, Pixel[] pal)
    {
        f.Clear();
        int cells = Math.Max(1, f.Width / Cell);
        for (int i = 0; i < cells; i++)
        {
            float cx = Cell * i + Cell / 2f, cy = f.Height / 2f;
            int k = Ks[i % Ks.Length];
            int m = k + 2 + (i & 1);
            float dir = (i & 1) == 0 ? 1f : -1f;
            float a = 0.34f + 0.22f * MathF.Sin(t * 0.37f + i * 1.7f);
            float b = 0.2f + 0.16f * MathF.Sin(t * 0.23f + i);
            float phi1 = t * 0.45f * dir, phi2 = -t * 0.6f * dir;
            float scale = (f.Height / 2f - 2.5f) / (1f + a + b);

            // Faint rim so the medallion has a frame.
            var rim = ToyPalettes.Sample(pal, t * 0.04f + i * 0.2f);
            for (int s = 0; s < 40; s++)
            {
                float ang = s / 40f * MathF.Tau;
                ToyGfx.AddPixel(f, (int)MathF.Round(cx + MathF.Cos(ang) * 30.5f), (int)MathF.Round(cy + MathF.Sin(ang) * 30.5f), rim, 0.35f);
            }

            for (int echo = 0; echo < 3; echo++)
            {
                float es = scale * (1f - echo * 0.3f);
                float ephi = echo * 0.55f * dir;
                float bright = echo == 0 ? 0.95f : echo == 1 ? 0.6f : 0.4f;
                float px = 0, py = 0;
                for (int s = 0; s <= Points; s++)
                {
                    float ang = s / (float)Points * MathF.Tau;
                    float x = MathF.Cos(ang + ephi) + a * MathF.Cos(k * ang + phi1) + b * MathF.Cos(m * ang + phi2);
                    float y = MathF.Sin(ang + ephi) + a * MathF.Sin(k * ang + phi1) + b * MathF.Sin(m * ang + phi2);
                    x = cx + x * es; y = cy + y * es;
                    if (s > 0)
                    {
                        var c = ToyPalettes.Sample(pal, s / (float)Points * 0.7f + t * 0.05f + i * 0.17f + echo * 0.12f);
                        ToyGfx.AddLine(f, px, py, x, y, c, bright);
                    }
                    px = x; py = y;
                }
            }
        }
    }
}

/// <summary>A zooming tunnel of nested polygons whose side count morphs between 3 and 7 while they twist.</summary>
public sealed class PolygonsPattern : GeoPattern
{
    private const int Layers = 8;
    private const int Samples = 56;

    public override string Name => "polygons";

    public override void Render(FrameBuffer f, float t, Pixel[] pal)
    {
        f.Clear();
        int cells = Math.Max(1, f.Width / Cell);
        for (int i = 0; i < cells; i++)
        {
            float cx = Cell * i + Cell / 2f, cy = f.Height / 2f;
            float dir = (i & 1) == 0 ? 1f : -1f;
            float sidesF = 3f + (0.5f + 0.5f * MathF.Sin(t * 0.21f + i * 0.9f)) * 4f;
            int n0 = (int)sidesF, n1 = n0 + 1;
            float mix = sidesF - n0;
            for (int l = 0; l < Layers; l++)
            {
                float u = Frac(l / (float)Layers + t * 0.07f);
                float radius = 33f * u;
                if (radius < 1.5f) continue;
                float fade = MathF.Sqrt(MathF.Sin(MathF.PI * u));
                float rot = dir * (t * 0.4f + (1f - u) * 1.3f * MathF.Sin(t * 0.15f + i));
                var color = ToyPalettes.Sample(pal, u * 0.8f + t * 0.04f + i * 0.15f);
                float px = 0, py = 0;
                for (int s = 0; s <= Samples; s++)
                {
                    float phi = s / (float)Samples * MathF.Tau;
                    float r = radius * Lerp(Radial(n0, phi), Radial(n1, phi), mix);
                    float x = cx + MathF.Cos(phi + rot) * r, y = cy + MathF.Sin(phi + rot) * r;
                    if (s > 0) ToyGfx.AddLine(f, px, py, x, y, color, fade);
                    px = x; py = y;
                }
            }
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // Distance from the centre to the edge of a regular n-gon (circumradius 1) in direction phi.
    private static float Radial(int n, float phi)
    {
        float sector = MathF.Tau / n;
        float local = phi - MathF.Floor(phi / sector) * sector - sector / 2f;
        return MathF.Cos(MathF.PI / n) / MathF.Cos(local);
    }
}

/// <summary>Three Lissajous figures that rotate through their phase, with bright heads tracing them.</summary>
public sealed class LissajousPattern : GeoPattern
{
    private static readonly (int a, int b)[] Ratios = [(3, 2), (5, 4), (2, 1), (7, 6), (3, 4), (5, 6)];
    private const int Points = 420;

    public override string Name => "lissajous";

    public override void Render(FrameBuffer f, float t, Pixel[] pal)
    {
        f.Clear();
        float cx = f.Width / 2f, cy = f.Height / 2f;
        int phase = (int)(t / 12f);
        for (int c = 0; c < 3; c++)
        {
            var (a, b) = Ratios[(phase * 2 + c * 2) % Ratios.Length];
            float ax = (f.Width / 2f - 6f) * (1f - c * 0.16f), ay = (f.Height / 2f - 4f) * (1f - c * 0.12f);
            float drift = t * (0.32f + 0.11f * c) * (c == 1 ? -1f : 1f);
            float px = 0, py = 0;
            for (int s = 0; s <= Points; s++)
            {
                float ang = s / (float)Points * MathF.Tau;
                float x = cx + ax * MathF.Sin(a * ang + drift);
                float y = cy + ay * MathF.Sin(b * ang);
                if (s > 0)
                {
                    var col = ToyPalettes.Sample(pal, s / (float)Points * 0.5f + t * 0.05f + c * 0.3f);
                    ToyGfx.AddLine(f, px, py, x, y, col, 0.8f);
                }
                px = x; py = y;
            }

            float head = t * (0.7f + c * 0.23f);
            float hx = cx + ax * MathF.Sin(a * head + drift), hy = cy + ay * MathF.Sin(b * head);
            ToyGfx.AddDisc(f, hx, hy, 2.4f, Pixel.Lerp(ToyPalettes.Sample(pal, t * 0.05f + c * 0.3f), Pixel.White, 0.6f), 1f);
        }
    }
}

/// <summary>A folded 64x64 kaleidoscope computed once per frame and tiled (mirrored) across the panel.</summary>
public sealed class KaleidoscopePattern : GeoPattern
{
    private readonly float[] _px = new float[Cell * Cell];
    private readonly float[] _py = new float[Cell * Cell];
    private readonly float[] _r = new float[Cell * Cell];
    private readonly Pixel[] _cell = new Pixel[Cell * Cell];
    private readonly Pixel[] _cell2 = new Pixel[Cell * Cell];

    public KaleidoscopePattern(int segments = 6)
    {
        float wedge = MathF.Tau / segments;
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                float dx = x + 0.5f - Cell / 2f, dy = y + 0.5f - Cell / 2f;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float th = MathF.Atan2(dy, dx);
                th -= MathF.Floor(th / wedge) * wedge;
                th = MathF.Abs(th - wedge / 2f);
                int i = y * Cell + x;
                _r[i] = r;
                _px[i] = r * MathF.Cos(th);
                _py[i] = r * MathF.Sin(th);
            }
    }

    public override string Name => "kaleidoscope";

    public override void Render(FrameBuffer f, float t, Pixel[] pal)
    {
        // Two different medallions (the second runs ahead in time and counter-rotates), alternated across the panel.
        FillCell(_cell, t, 0.22f, 0f, pal);
        FillCell(_cell2, t + 5.3f, -0.18f, 0.35f, pal);

        int cells = Math.Max(1, f.Width / Cell);
        int top = Math.Max(0, (f.Height - Cell) / 2);
        f.Clear();
        for (int ci = 0; ci < cells; ci++)
        {
            var src = (ci & 1) == 0 ? _cell : _cell2;
            for (int y = 0; y < Cell && y + top < f.Height; y++)
                for (int x = 0; x < Cell; x++)
                    f.SetPixel(ci * Cell + x, y + top, src[y * Cell + x]);
        }
    }

    private void FillCell(Pixel[] dst, float t, float spin, float hue, Pixel[] pal)
    {
        float co = MathF.Cos(t * spin), si = MathF.Sin(t * spin);
        for (int i = 0; i < dst.Length; i++)
        {
            float r = _r[i];
            if (r > 31.5f) { dst[i] = Pixel.Black; continue; }
            float x = _px[i] * co - _py[i] * si, y = _px[i] * si + _py[i] * co;
            float a = MathF.Sin(x * 0.28f + t * 1.1f) + MathF.Sin(y * 0.31f - t * 0.9f)
                      + MathF.Sin((x + y) * 0.17f + t * 0.6f) + MathF.Sin(r * 0.33f - t * 1.7f);
            float b = 0.5f + 0.5f * MathF.Sin(a * 1.4f + t);
            b = b * b * b;
            var c = ToyPalettes.Sample(pal, a * 0.1f + r * 0.012f + t * 0.04f + hue).WithBrightness(0.03f + 1.15f * b);
            if (r > 30.2f) c = ToyPalettes.Sample(pal, t * 0.05f + hue).WithBrightness(0.8f);
            dst[i] = c;
        }
    }
}

/// <summary>A hexagonal tessellation whose tiles ripple with colour waves from two wandering sources.</summary>
public sealed class TessellationPattern : GeoPattern
{
    private const float HexR = 6.2f;

    private int _w, _h;
    private int[] _hexOf = [];
    private byte[] _bevel = [];
    private float[] _cx = [], _cy = [];
    private Pixel[] _color = [];

    public override string Name => "tessellation";

    private void Build(int w, int h)
    {
        _w = w; _h = h;
        _hexOf = new int[w * h];
        _bevel = new byte[w * h];
        var map = new Dictionary<(int, int), int>();
        var cx = new List<float>(); var cy = new List<float>();
        float a = HexR * 0.8660254f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float q = (0.57735027f * fx - fy / 3f) / HexR, r = (2f / 3f * fy) / HexR;
                var (rq, rr) = CubeRound(q, r);
                if (!map.TryGetValue((rq, rr), out int idx))
                {
                    idx = cx.Count;
                    map[(rq, rr)] = idx;
                    cx.Add(HexR * 1.7320508f * (rq + rr / 2f));
                    cy.Add(HexR * 1.5f * rr);
                }
                _hexOf[y * w + x] = idx;
                float dx = MathF.Abs(fx - cx[idx]), dy = MathF.Abs(fy - cy[idx]);
                float m = MathF.Max(dx, dx * 0.5f + dy * 0.8660254f) / a; // 1 at the edge
                float edge = (1f - m) * a; // pixels to the edge
                _bevel[y * w + x] = edge < 0.55f ? (byte)0 : (byte)(Math.Clamp(150f + edge * 38f, 0f, 255f));
            }

        _cx = cx.ToArray(); _cy = cy.ToArray();
        _color = new Pixel[_cx.Length];
    }

    private static (int q, int r) CubeRound(float q, float r)
    {
        float x = q, z = r, y = -x - z;
        float rx = MathF.Round(x), ry = MathF.Round(y), rz = MathF.Round(z);
        float dx = MathF.Abs(rx - x), dy = MathF.Abs(ry - y), dz = MathF.Abs(rz - z);
        if (dx > dy && dx > dz) rx = -ry - rz;
        else if (dy > dz) ry = -rx - rz;
        else rz = -rx - ry;
        return ((int)rx, (int)rz);
    }

    public override void Render(FrameBuffer f, float t, Pixel[] pal)
    {
        if (_w != f.Width || _h != f.Height) Build(f.Width, f.Height);

        float f1x = f.Width * (0.5f + 0.42f * MathF.Sin(t * 0.23f)), f1y = f.Height * (0.5f + 0.5f * MathF.Sin(t * 0.31f + 1f));
        float f2x = f.Width * (0.5f + 0.42f * MathF.Sin(t * 0.17f + 2.4f)), f2y = f.Height * (0.5f + 0.5f * MathF.Cos(t * 0.27f));
        for (int i = 0; i < _color.Length; i++)
        {
            float dx1 = _cx[i] - f1x, dy1 = _cy[i] - f1y, dx2 = _cx[i] - f2x, dy2 = _cy[i] - f2y;
            float d1 = MathF.Sqrt(dx1 * dx1 + dy1 * dy1), d2 = MathF.Sqrt(dx2 * dx2 + dy2 * dy2);
            float wave = 0.5f + 0.25f * MathF.Sin(d1 * 0.16f - t * 2.6f) + 0.25f * MathF.Sin(d2 * 0.13f - t * 2.1f);
            wave = Math.Clamp((wave - 0.3f) * 1.9f, 0f, 1f);
            wave = wave * wave * (3f - 2f * wave);
            _color[i] = ToyPalettes.Sample(pal, (d1 - d2) * 0.004f + t * 0.05f + wave * 0.25f).WithBrightness(0.07f + 1.15f * wave);
        }

        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                int p = y * _w + x;
                byte bev = _bevel[p];
                f.SetPixel(x, y, bev == 0 ? Pixel.Black : _color[_hexOf[p]].WithBrightness(bev / 255f * 1.15f));
            }
    }
}
