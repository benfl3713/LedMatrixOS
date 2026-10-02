using BdfFontParser;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Ambient;

/// <summary>Small colour and drawing helpers shared by the ambient apps. Everything here is allocation free.</summary>
internal static class Gfx
{
    public static Pixel Mix(Pixel a, Pixel b, float t) => Pixel.Lerp(a, b, t);

    /// <summary>Blends through hue (shortest way round the wheel) so a cyan to amber fade stays vivid instead of passing through grey.</summary>
    public static Pixel HueMix(Pixel a, Pixel b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        ToHsv(a, out var h1, out var s1, out var v1);
        ToHsv(b, out var h2, out var s2, out var v2);
        float d = h2 - h1;
        if (d > 180f) d -= 360f;
        else if (d < -180f) d += 360f;
        return Pixel.FromHsv(h1 + d * t, s1 + (s2 - s1) * t, v1 + (v2 - v1) * t);
    }

    public static Pixel Dim(Pixel p, float f) => p.WithBrightness(f);

    /// <summary>Hue in degrees, saturation and value 0-1.</summary>
    public static Pixel Hsv(float h, float s, float v) => Pixel.FromHsv(h, s, v);

    public static void ToHsv(Pixel p, out float h, out float s, out float v)
    {
        float r = p.R / 255f, g = p.G / 255f, b = p.B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        float d = max - min;
        v = max;
        s = max <= 0f ? 0f : d / max;
        if (d <= 1e-6f) h = 0f;
        else if (max == r) h = 60f * (((g - b) / d) % 6f);
        else if (max == g) h = 60f * ((b - r) / d + 2f);
        else h = 60f * ((r - g) / d + 4f);
        if (h < 0f) h += 360f;
    }

    /// <summary>Adds colour light to a pixel (clamped), scaled by <paramref name="amount"/>.</summary>
    public static void Add(FrameBuffer frame, int x, int y, Pixel c, float amount)
    {
        if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height || amount <= 0f) return;
        var d = frame.GetPixel(x, y);
        frame.SetPixel(x, y, new Pixel(
            (byte)Math.Min(255, d.R + (int)(c.R * amount)),
            (byte)Math.Min(255, d.G + (int)(c.G * amount)),
            (byte)Math.Min(255, d.B + (int)(c.B * amount))));
    }

    /// <summary>A soft round glow: bright in the middle, falling off smoothly to nothing at <paramref name="radius"/>.</summary>
    public static void GlowDisc(FrameBuffer frame, float cx, float cy, float radius, Pixel c, float strength)
    {
        int r = (int)MathF.Ceiling(radius);
        int x0 = (int)MathF.Floor(cx) - r, y0 = (int)MathF.Floor(cy) - r;
        float inv = 1f / (radius * radius);
        for (int y = y0; y <= y0 + 2 * r; y++)
        {
            if (y < 0 || y >= frame.Height) continue;
            float dy = y - cy;
            for (int x = x0; x <= x0 + 2 * r; x++)
            {
                if (x < 0 || x >= frame.Width) continue;
                float dx = x - cx;
                float q = 1f - (dx * dx + dy * dy) * inv;
                if (q <= 0f) continue;
                Add(frame, x, y, c, q * q * strength);
            }
        }
    }

    /// <summary>A stretched glow (ellipse) used for horizon light and bands.</summary>
    public static void GlowEllipse(FrameBuffer frame, float cx, float cy, float rx, float ry, Pixel c, float strength)
    {
        int x0 = (int)MathF.Floor(cx - rx), x1 = (int)MathF.Ceiling(cx + rx);
        int y0 = (int)MathF.Floor(cy - ry), y1 = (int)MathF.Ceiling(cy + ry);
        float ix = 1f / (rx * rx), iy = 1f / (ry * ry);
        for (int y = Math.Max(y0, 0); y <= Math.Min(y1, frame.Height - 1); y++)
        {
            float dy = y - cy;
            for (int x = Math.Max(x0, 0); x <= Math.Min(x1, frame.Width - 1); x++)
            {
                float dx = x - cx;
                float q = 1f - (dx * dx * ix + dy * dy * iy);
                if (q <= 0f) continue;
                Add(frame, x, y, c, q * q * strength);
            }
        }
    }

    /// <summary>Vertical gradient filled row by row (one span fill per row).</summary>
    public static void VerticalGradient(FrameBuffer frame, Rectangle area, Pixel top, Pixel bottom)
    {
        if (area.Height <= 0) return;
        for (int row = 0; row < area.Height; row++)
        {
            float t = area.Height == 1 ? 0f : row / (float)(area.Height - 1);
            frame.Fill(new Rectangle(area.X, area.Y + row, area.Width, 1), Pixel.Lerp(top, bottom, t));
        }
    }

    /// <summary>A hard-edged filled disc (centre pixel plus radius).</summary>
    public static void Disc(FrameBuffer frame, int cx, int cy, int radius, Pixel color)
    {
        float r = radius + 0.5f;
        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
                if (x * x + y * y <= r * r) frame.SetPixel(cx + x, cy + y, color);
    }

    public static float Smooth(float t) => t * t * (3f - 2f * t);

    public static float Saturate(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    /// <summary>Deterministic hash noise 0-1 from an integer.</summary>
    public static float Hash(int n)
    {
        unchecked
        {
            uint x = (uint)n * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
            x = (x >> 22) ^ x;
            return (x & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Smooth value noise in 1D.</summary>
    public static float Noise1(float x, int seed = 0)
    {
        int i = (int)MathF.Floor(x);
        float f = x - i;
        return Hash(i * 31 + seed) * (1f - Smooth(f)) + Hash((i + 1) * 31 + seed) * Smooth(f);
    }
}

internal enum PaintMode { Solid, Vertical, Rainbow }

/// <summary>How a <see cref="GlyphLine"/> is coloured and animated for one draw call. Pure value, so it can be rebuilt every frame for free.</summary>
internal struct TextPaint
{
    public PaintMode Mode;
    /// <summary>Solid colour, or the top colour of a vertical gradient.</summary>
    public Pixel A;
    /// <summary>Bottom colour of a vertical gradient.</summary>
    public Pixel B;
    public float Alpha;
    public float HueBase, HueSpan, Sat, Val;
    public float WaveAmp, WavePhase, WaveStep;
    public bool Shadow, Bold;
    public Pixel ShadowColor;
    public float Glow;

    public static TextPaint Solid(Pixel color) => new()
    {
        Mode = PaintMode.Solid, A = color, B = color, Alpha = 1f, Sat = 1f, Val = 1f, ShadowColor = Pixel.Black,
    };

    public static TextPaint Vertical(Pixel top, Pixel bottom)
    {
        var p = Solid(top);
        p.Mode = PaintMode.Vertical;
        p.B = bottom;
        return p;
    }

    public static TextPaint Rainbow(float hueBase, float huePerPixel, float sat = 1f, float val = 1f)
    {
        var p = Solid(Pixel.White);
        p.Mode = PaintMode.Rainbow;
        p.HueBase = hueBase;
        p.HueSpan = huePerPixel;
        p.Sat = sat;
        p.Val = val;
        return p;
    }
}

/// <summary>
/// A string rasterised once with a BDF font, one glyph map per character so each letter can be placed on its own (wave text).
/// Rebuilt only when the text or font changes. Drawing paints with a <see cref="TextPaint"/>, scaled by whole-pixel blocks,
/// which keeps the chunky look on an LED panel and is far cheaper than scaling fonts through reflection.
/// </summary>
internal sealed class GlyphLine
{
    private BdfFont? _font;
    private string _text = "";
    private bool[][,] _maps = [];
    private int[] _xs = [];

    public string Text => _text;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Count => _maps.Length;

    public bool Set(BdfFont font, string text)
    {
        text ??= "";
        if (ReferenceEquals(font, _font) && string.Equals(text, _text)) return false;
        _font = font;
        _text = text;
        Height = font.BoundingBox.Y;
        _maps = new bool[text.Length][,];
        _xs = new int[text.Length];
        int x = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var m = font.GetMapOfString(text[i].ToString());
            _maps[i] = m;
            _xs[i] = x;
            x += m.GetLength(0);
        }
        Width = x;
        return true;
    }

    /// <summary>X position of a character (unscaled).</summary>
    public int CharX(int index) => _xs[index];

    public int ScaledWidth(int scale) => Width * scale;
    public int ScaledHeight(int scale) => Height * scale;

    /// <summary>Draws with the top-left of the line box at (x, y); <paramref name="scale"/> multiplies each font pixel into a block.</summary>
    public void Draw(FrameBuffer frame, int x, int y, int scale, in TextPaint paint)
    {
        if (_maps.Length == 0 || paint.Alpha <= 0f) return;
        if (paint.Shadow) DrawPass(frame, x + Math.Max(1, scale / 2), y + Math.Max(1, scale / 2), scale, paint, 0);
        if (paint.Glow > 0f) DrawPass(frame, x, y, scale, paint, 1);
        DrawPass(frame, x, y, scale, paint, 2);
    }

    // pass 0 = shadow, 1 = glow halo, 2 = body
    private void DrawPass(FrameBuffer frame, int ox, int oy, int scale, in TextPaint p, int pass)
    {
        int rows = Height;
        Span<Pixel> rowColor = stackalloc Pixel[Math.Min(rows, 128)];
        if (p.Mode == PaintMode.Vertical && pass == 2)
            for (int r = 0; r < rowColor.Length; r++)
                rowColor[r] = Pixel.Lerp(p.A, p.B, rows <= 1 ? 0f : r / (float)(rows - 1));

        for (int c = 0; c < _maps.Length; c++)
        {
            var map = _maps[c];
            int w = map.GetLength(0), h = map.GetLength(1);
            int cx = ox + _xs[c] * scale;
            int cy = oy;
            if (p.WaveAmp != 0f)
                cy += (int)MathF.Round(MathF.Sin(p.WavePhase + c * p.WaveStep) * p.WaveAmp);
            if (cx + w * scale < 0 || cx >= frame.Width) continue;

            for (int line = 0; line < h && line < rowColor.Length; line++)
            {
                for (int bit = 0; bit < w; bit++)
                {
                    if (!map[bit, line]) continue;
                    int px = cx + bit * scale, py = cy + line * scale;
                    if (pass == 0) Plot(frame, px, py, scale, p.ShadowColor, p.Alpha * 0.85f, p.Bold);
                    else if (pass == 1) PlotGlow(frame, px, py, scale, p, line, rowColor);
                    else
                    {
                        var color = p.Mode switch
                        {
                            PaintMode.Vertical => rowColor[line],
                            PaintMode.Rainbow => Pixel.FromHsv(p.HueBase + (_xs[c] + bit) * scale * p.HueSpan, p.Sat, p.Val),
                            _ => p.A,
                        };
                        Plot(frame, px, py, scale, color, p.Alpha, p.Bold);
                    }
                }
            }
        }
    }

    private static void PlotGlow(FrameBuffer frame, int px, int py, int s, in TextPaint p, int line, Span<Pixel> rowColor)
    {
        var color = p.Mode == PaintMode.Vertical ? rowColor[line] : p.Mode == PaintMode.Rainbow ? Pixel.FromHsv(p.HueBase + px * p.HueSpan, p.Sat, p.Val) : p.A;
        float a = p.Glow * p.Alpha;
        for (int i = 0; i < s; i++)
        {
            frame.BlendPixel(px - 1, py + i, color, a);
            frame.BlendPixel(px + s, py + i, color, a);
            frame.BlendPixel(px + i, py - 1, color, a);
            frame.BlendPixel(px + i, py + s, color, a);
        }
    }

    private static void Plot(FrameBuffer frame, int x, int y, int s, Pixel color, float alpha, bool bold)
    {
        if (alpha >= 0.999f)
        {
            if (s == 1)
            {
                frame.SetPixel(x, y, color);
                if (bold) frame.SetPixel(x + 1, y, color);
            }
            else frame.Fill(new Rectangle(x, y, s + (bold ? 1 : 0), s), color);
            return;
        }

        for (int j = 0; j < s; j++)
            for (int i = 0; i < s + (bold ? 1 : 0); i++)
                frame.BlendPixel(x + i, y + j, color, alpha);
    }
}

/// <summary>Colour of the 16 named colours the old settings used, plus a few extra, resolved without ImageSharp.</summary>
internal static class NamedColors
{
    public static Pixel Resolve(string? name, Pixel fallback) => name switch
    {
        "Red" => new Pixel(255, 40, 40),
        "Green" => new Pixel(40, 255, 90),
        "Blue" => new Pixel(60, 120, 255),
        "Yellow" => new Pixel(255, 220, 40),
        "Cyan" => new Pixel(30, 235, 255),
        "Magenta" => new Pixel(255, 60, 230),
        "Orange" => new Pixel(255, 140, 20),
        "White" => new Pixel(255, 255, 255),
        "Pink" => new Pixel(255, 100, 170),
        "Purple" => new Pixel(160, 80, 255),
        "Lime" => new Pixel(170, 255, 40),
        "Gold" => new Pixel(255, 190, 30),
        _ => fallback,
    };

    public static Pixel Background(string? name) => name switch
    {
        "DarkBlue" => new Pixel(0, 0, 55),
        "DarkGray" => new Pixel(38, 38, 42),
        "Navy" => new Pixel(0, 12, 70),
        _ => Pixel.Black,
    };
}

/// <summary>Maps the old "unknown values fall back" contract onto a fixed option list.</summary>
internal static class Options
{
    public static string Pick(string? value, string[] allowed, string fallback)
    {
        if (value is null) return fallback;
        foreach (var a in allowed)
            if (string.Equals(a, value, StringComparison.OrdinalIgnoreCase)) return a;
        return fallback;
    }
}
