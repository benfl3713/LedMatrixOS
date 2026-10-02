using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Clocks;

/// <summary>Pre-rendered glyph coverage masks (one per face of a split-flap card, plus a trailing blank face).</summary>
internal sealed class FaceSet
{
    public int W { get; }
    public int H { get; }
    public byte[][] Faces { get; }
    public int Blank => Faces.Length - 1;

    private FaceSet(int w, int h, byte[][] faces)
    {
        W = w;
        H = h;
        Faces = faces;
    }

    /// <summary>Digits 0-9 (stroke glyphs centred on the card) plus a blank.</summary>
    public static FaceSet Digits(GlyphAtlas atlas, int w, int h)
    {
        var faces = new byte[11][];
        int ox = (w - atlas.Stride) / 2, oy = (h - atlas.Rows) / 2;
        for (int d = 0; d < 10; d++)
        {
            var m = new byte[w * h];
            for (int y = 0; y < atlas.Rows; y++)
                for (int x = 0; x < atlas.Stride; x++)
                {
                    int tx = x + ox, ty = y + oy;
                    if ((uint)tx < (uint)w && (uint)ty < (uint)h) m[ty * w + tx] = atlas.Ink[d][y * atlas.Stride + x];
                }
            faces[d] = m;
        }
        faces[10] = new byte[w * h];
        return new FaceSet(w, h, faces);
    }

    /// <summary>One face per string, drawn with a bitmap font and centred, plus a blank.</summary>
    public static FaceSet Texts(IReadOnlyList<string> texts, BdfFont font, int w, int h)
    {
        var faces = new byte[texts.Count + 1][];
        for (int i = 0; i < texts.Count; i++)
        {
            var map = font.GetMapOfString(texts[i]);
            int tw = map.GetLength(0), th = map.GetLength(1);
            int ox = (w - tw) / 2, oy = (h - th) / 2;
            var m = new byte[w * h];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                    if (map[x, y] && (uint)(x + ox) < (uint)w && (uint)(y + oy) < (uint)h) m[(y + oy) * w + x + ox] = 255;
            faces[i] = m;
        }
        faces[texts.Count] = new byte[w * h];
        return new FaceSet(w, h, faces);
    }
}

/// <summary>Colours of a flip card: the card body and the glyph.</summary>
internal sealed record FlipStyle(Pixel Card, Pixel Glyph)
{
    public static FlipStyle From(string? background, string? text)
    {
        var card = background switch
        {
            "DarkBlue" => new Pixel(22, 34, 80),
            "DarkGray" => new Pixel(62, 62, 68),
            "White" => new Pixel(228, 228, 220),
            _ => new Pixel(42, 42, 48),
        };
        var glyph = text is null or "White" ? new Pixel(250, 244, 228) : ClockPalette.ByColorName(text);
        float cardLum = (card.R + card.G + card.B) / 3f, glyphLum = (glyph.R + glyph.G + glyph.B) / 3f;
        if (cardLum > 140 && glyphLum > 140) glyph = new Pixel(26, 26, 32);
        else if (cardLum < 90 && glyphLum < 90) glyph = new Pixel(250, 244, 228);
        return new FlipStyle(card, glyph);
    }
}

/// <summary>
/// A split-flap card. When the face changes the top flap of the old face falls forward and shrinks away (accelerating like gravity,
/// darkening as it turns), then the bottom flap of the new face swings down onto the lower half and settles with a small bounce.
/// A shadow slides across the lower half while the flap falls. Faces are pre-rendered masks, so a frame allocates nothing.
/// </summary>
internal sealed class FlipCard : Node
{
    private const int ShadowRows = 3;
    private readonly FaceSet _faces;
    private readonly Func<int> _source;
    private readonly Tween<float> _roll = new(1f);
    private readonly int _w, _h, _half;
    private readonly int[] _inset;
    private readonly Pixel[] _rowColor;
    private int _applied = -2, _previous;
    private float _gate;
    private FlipStyle _style;

    public FlipCard(FaceSet faces, Func<int> source, FlipStyle style)
    {
        _faces = faces;
        _source = source;
        _style = style;
        _w = faces.W;
        _h = faces.H - faces.H % 2;
        _half = _h / 2;
        Width = _w;
        Height = _h + ShadowRows;
        _inset = new int[_h];
        const float r = 3f;
        for (int row = 0; row < _h; row++)
        {
            int edge = Math.Min(row, _h - 1 - row);
            if (edge >= r) continue;
            float dy = r - edge - 0.5f;
            _inset[row] = (int)MathF.Round(r - MathF.Sqrt(Math.Max(0f, r * r - dy * dy)), MidpointRounding.AwayFromZero);
        }
        _rowColor = new Pixel[_h];
        BuildRows();
    }

    public FlipStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            BuildRows();
        }
    }

    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(560);
    public bool IsFlipping => _roll.IsRunning;
    public int Face => _applied;

    /// <summary>Shows a blank card until the delay has passed, then flips to the real value.</summary>
    public void StartEntrance(TimeSpan delay) => _gate = (float)delay.TotalSeconds;

    private void BuildRows()
    {
        var c = _style.Card;
        for (int row = 0; row < _h; row++)
        {
            float k;
            if (row < _half) k = 1.22f - 0.25f * row / _half;   // top flap: lit from above, falling off toward the hinge
            else k = 0.78f + 0.2f * (row - _half) / _half;      // bottom flap: a little darker, lighter toward the bottom edge
            _rowColor[row] = Scale(c, k);
        }
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        int face;
        if (_gate > 0f)
        {
            _gate -= (float)ctx.Delta.TotalSeconds;
            face = _faces.Blank;
        }
        else
        {
            face = _source();
            if (face < 0 || face >= _faces.Faces.Length) face = _faces.Blank;
        }

        if (face == _applied) return;
        bool first = _applied == -2;
        _previous = first ? face : _applied;
        _applied = face;
        _roll.Set(0f);
        if (!first && Host is { } host) host.Animator.Animate(_roll, 1f, Duration);
        else _roll.Set(1f);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int x = bounds.X, y = bounds.Y;
        int cur = _applied < 0 ? _faces.Blank : _applied;

        // Drop shadow under the card.
        for (int i = 0; i < ShadowRows; i++)
        {
            float a = 0.5f - i * 0.17f;
            for (int cx = 2; cx < _w - 1; cx++) frame.BlendPixel(x + cx, y + _h + i, Pixel.Black, a);
        }

        if (!_roll.IsRunning)
        {
            DrawHalf(frame, x, y, cur, top: true, shade: 0f);
            DrawHalf(frame, x, y, cur, top: false, shade: 0f);
        }
        else
        {
            float p = _roll.Value;
            const float split = 0.45f;
            DrawHalf(frame, x, y, cur, top: true, shade: 0f);
            if (p < split)
            {
                float t = p / split;
                float ang = MathF.PI / 2f * Easing.InQuad(t);
                DrawHalf(frame, x, y, _previous, top: false, shade: 0.5f * MathF.Sin(ang));
                int fh = (int)MathF.Round(_half * MathF.Cos(ang));
                DrawFlap(frame, x, y, _previous, top: true, fh, 1f - 0.55f * MathF.Sin(ang));
            }
            else
            {
                float t = (p - split) / (1f - split);
                float u = Easing.OutBounce(t);
                DrawHalf(frame, x, y, _previous, top: false, shade: 0f);
                int fh = (int)MathF.Round(_half * MathF.Sin(u * MathF.PI / 2f));
                float shade = 0.55f + 0.45f * Math.Min(1f, t * 1.4f);
                DrawFlap(frame, x, y, cur, top: false, fh, shade);
            }
        }

        DrawHinge(frame, x, y);
    }

    private void DrawHalf(FrameBuffer frame, int x, int y, int face, bool top, float shade)
    {
        var mask = _faces.Faces[face];
        int r0 = top ? 0 : _half, r1 = top ? _half : _h;
        var glyph = _style.Glyph;
        for (int row = r0; row < r1; row++)
        {
            var bg = _rowColor[row];
            if (shade > 0f)
            {
                // Shadow cast by the falling flap, strongest at the hinge.
                float near = 1f - (row - _half) / (float)_half;
                bg = Scale(bg, 1f - shade * near);
            }
            int ins = _inset[row];
            int o = row * _w;
            for (int col = ins; col < _w - ins; col++)
            {
                byte m = mask[o + col];
                var c = m == 0 ? bg : m == 255 ? ShadeGlyph(glyph, shade, row) : Pixel.Lerp(bg, glyph, m / 255f);
                frame.SetPixel(x + col, y + row, c);
            }
        }
    }

    private Pixel ShadeGlyph(Pixel glyph, float shade, int row)
    {
        if (shade <= 0f) return glyph;
        float near = 1f - (row - _half) / (float)_half;
        return Scale(glyph, 1f - shade * near);
    }

    /// <summary>Draws a flap hinged on the centre line, squashed to <paramref name="fh"/> rows (the foreshortened view of a turning card).</summary>
    private void DrawFlap(FrameBuffer frame, int x, int y, int face, bool top, int fh, float shade)
    {
        if (fh <= 0) return;
        var mask = _faces.Faces[face];
        var glyph = Scale(_style.Glyph, shade);
        int destStart = top ? y + _half - fh : y + _half;
        for (int i = 0; i < fh; i++)
        {
            int src = (int)((i + 0.5f) * _half / fh);
            if (src >= _half) src = _half - 1;
            int row = top ? src : _half + src;
            var bg = Scale(_rowColor[row], shade);
            // The edge farthest from the hinge catches the light.
            bool edge = top ? i == 0 : i == fh - 1;
            if (edge) bg = Scale(bg, 1.25f);
            int ins = _inset[row];
            if (fh < 3) ins = Math.Max(ins, 1);
            int o = row * _w;
            for (int col = ins; col < _w - ins; col++)
            {
                byte m = mask[o + col];
                var c = m == 0 ? bg : m == 255 ? glyph : Pixel.Lerp(bg, glyph, m / 255f);
                frame.SetPixel(x + col, destStart + i, c);
            }
        }
    }

    private void DrawHinge(FrameBuffer frame, int x, int y)
    {
        int hy = y + _half;
        var dark = Scale(_style.Card, 0.22f);
        for (int col = 0; col < _w; col++)
        {
            frame.SetPixel(x + col, hy - 1, dark);
            frame.BlendPixel(x + col, hy, Pixel.White, 0.07f);
        }
        // Axle pins on both sides.
        var pin = Pixel.Black;
        for (int i = -2; i <= 0; i++)
        {
            frame.SetPixel(x, hy + i, pin);
            frame.SetPixel(x + _w - 1, hy + i, pin);
        }
    }

    private static Pixel Scale(Pixel p, float k) =>
        new((byte)Math.Clamp(p.R * k, 0f, 255f), (byte)Math.Clamp(p.G * k, 0f, 255f), (byte)Math.Clamp(p.B * k, 0f, 255f));
}

/// <summary>Dark, softly lit backdrop behind the flip cards.</summary>
internal sealed class FlipBackdrop : Node
{
    public FlipBackdrop()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        frame.FillLinearGradient(bounds, new Pixel(26, 24, 30), new Pixel(4, 4, 6), vertical: true);
    }
}
