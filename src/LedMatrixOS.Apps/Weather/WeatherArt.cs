using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Weather;

/// <summary>A string rasterised once; redrawing it allocates nothing (DrawText rebuilds the glyph map every call).</summary>
internal sealed class StaticText
{
    private BdfFont? _font;
    private string _text = "";
    private bool[,]? _map;

    public int Width { get; private set; }

    public bool Set(BdfFont font, string text)
    {
        if (ReferenceEquals(font, _font) && string.Equals(text, _text)) return false;
        _font = font;
        _text = text;
        _map = text.Length == 0 ? null : font.GetMapOfString(text);
        Width = _map?.GetLength(0) ?? 0;
        return true;
    }

    public void Draw(FrameBuffer frame, int x, int y, Pixel color, bool shadow = true)
    {
        if (_map is null) return;
        int rows = _map.GetLength(1);
        for (int line = 0; line < rows; line++)
            for (int bit = 0; bit < Width; bit++)
            {
                if (!_map[bit, line]) continue;
                frame.SetPixel(x + bit, y + line, color);
                if (shadow) frame.SetPixel(x + bit + 1, y + line + 1, Pixel.Black);
            }
    }
}

/// <summary>Shared by the weather and tube apps: memoises a formatted string so a label's text source allocates only when its input changes.</summary>
internal sealed class Memo<T>(Func<T> read, Func<T, string> format)
{
    private T? _last;
    private string? _text;

    public string Get()
    {
        var v = read();
        if (_text is null || !EqualityComparer<T>.Default.Equals(v, _last))
        {
            _last = v;
            _text = format(v);
        }
        return _text;
    }
}

/// <summary>Cloud and sun shapes shared by the big scene and the little forecast icons.</summary>
internal static class WeatherArt
{
    // x offset, radius (in cloud units); every circle rests on the same baseline, so the bottom is flat.
    private static readonly (float X, float R)[] CloudCircles = [(0f, 4.5f), (5.5f, 6.5f), (12f, 5.5f), (17.5f, 3.5f), (-4.5f, 3f)];

    /// <summary>Width of a cloud at scale 1 (add the left bump).</summary>
    public const float CloudWidth = 24f;

    /// <summary>Fills a cloud whose flat bottom sits at <paramref name="baseY"/>; <paramref name="cx"/> is its horizontal centre.</summary>
    public static void Cloud(FrameBuffer f, float cx, float baseY, float scale, Pixel top, Pixel bottom)
    {
        float left = cx - CloudWidth * scale / 2f + 4.5f * scale;
        float topY = baseY - 13f * scale;
        float h = baseY - topY;
        foreach (var (dx, r) in CloudCircles)
        {
            float ccx = left + dx * scale, rad = r * scale, ccy = baseY - rad;
            int y0 = (int)MathF.Ceiling(ccy - rad), y1 = (int)MathF.Floor(baseY - 0.5f);
            for (int y = y0; y <= y1; y++)
            {
                float dy = y + 0.5f - ccy;
                float half2 = rad * rad - dy * dy;
                if (half2 <= 0) continue;
                float half = MathF.Sqrt(half2);
                int x0 = (int)MathF.Round(ccx - half), x1 = (int)MathF.Round(ccx + half);
                f.Fill(new Rectangle(x0, y, x1 - x0, 1), Pixel.Lerp(top, bottom, Math.Clamp((y - topY) / h, 0f, 1f)));
            }
        }
    }

    public static void Sun(FrameBuffer f, float cx, float cy, float r, float t, Pixel core, Pixel rim, bool rays)
    {
        int ix = (int)MathF.Round(cx), iy = (int)MathF.Round(cy);
        if (rays)
        {
            int n = r >= 6 ? 12 : 8;
            for (int i = 0; i < n; i++)
            {
                float a = t * 0.35f + i * MathF.Tau / n;
                float len = r * (i % 2 == 0 ? 0.75f : 0.45f) * (1f + 0.2f * MathF.Sin(t * 2.2f + i));
                float c = MathF.Cos(a), s = MathF.Sin(a);
                f.DrawLine((int)MathF.Round(cx + c * (r + 1.5f)), (int)MathF.Round(cy + s * (r + 1.5f)),
                    (int)MathF.Round(cx + c * (r + 1.5f + len)), (int)MathF.Round(cy + s * (r + 1.5f + len)), rim);
            }
        }
        int ri = (int)MathF.Ceiling(r);
        for (int y = -ri; y <= ri; y++)
            for (int x = -ri; x <= ri; x++)
            {
                float d = MathF.Sqrt(x * x + y * y);
                if (d <= r) f.SetPixel(ix + x, iy + y, Pixel.Lerp(core, rim, d / r * 0.8f));
            }
    }

    public static void Moon(FrameBuffer f, float cx, float cy, float r, Pixel color, Pixel shade)
    {
        int ix = (int)MathF.Round(cx), iy = (int)MathF.Round(cy), ri = (int)MathF.Ceiling(r);
        float bx = r * 0.55f, by = -r * 0.30f, br = r * 0.95f;
        for (int y = -ri; y <= ri; y++)
            for (int x = -ri; x <= ri; x++)
            {
                if (x * x + y * y > r * r) continue;
                float ex = x - bx, ey = y - by;
                if (ex * ex + ey * ey <= br * br) continue;
                f.SetPixel(ix + x, iy + y, color);
            }
    }
}

/// <summary>Small animated weather icon, sized to a square. Day/night picks sun or moon; rain, snow and bolts animate on the frame clock.</summary>
internal sealed class WeatherGlyph : Node
{
    private readonly Func<(WeatherKind Kind, bool Day)> _source;
    private TimeSpan _time;
    private readonly int _size;

    public WeatherGlyph(Func<(WeatherKind, bool)> source, int size = 14)
    {
        _source = source;
        _size = size;
        Width = size;
        Height = size;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var (kind, day) = _source();
        float t = (float)_time.TotalSeconds;
        float s = _size / 14f;
        float cx = b.X + _size / 2f, cy = b.Y + _size / 2f;
        var cloudTop = new Pixel(245, 248, 255);
        var cloudBottom = new Pixel(165, 178, 200);
        var stormTop = new Pixel(120, 128, 150);
        var stormBottom = new Pixel(70, 76, 98);
        var rain = new Pixel(80, 170, 255);

        void Body(float x, float y, bool dark = false) =>
            WeatherArt.Cloud(f, x, y, s * 0.42f, dark ? stormTop : cloudTop, dark ? stormBottom : cloudBottom);

        void Heavens(float x, float y, float r)
        {
            if (day) WeatherArt.Sun(f, x, y, r, t, new Pixel(255, 245, 160), new Pixel(255, 190, 40), true);
            else WeatherArt.Moon(f, x, y, r, new Pixel(240, 240, 215), new Pixel(200, 200, 180));
        }

        switch (kind)
        {
            case WeatherKind.Clear:
                Heavens(cx, cy, 3.6f * s);
                break;
            case WeatherKind.PartlyCloudy:
                Heavens(cx - 2.5f * s, cy - 2.5f * s, 3f * s);
                Body(cx + 1.5f * s, cy + 5f * s);
                break;
            case WeatherKind.Cloudy:
                Body(cx, cy + 3f * s);
                break;
            case WeatherKind.Fog:
                for (int i = 0; i < 4; i++)
                    f.Fill(new Rectangle((int)(cx - 5 * s + (i % 2) * 2), (int)(cy - 4.5f * s + i * 3.2f * s), (int)(10 * s), Math.Max(1, (int)s)), new Pixel(180, 190, 205));
                break;
            case WeatherKind.Drizzle:
            case WeatherKind.Rain:
                Body(cx, cy + 1f * s, dark: true);
                for (int i = 0; i < 3; i++)
                {
                    float fall = (t * 6f + i * 3.1f) % 4f;
                    f.Fill(new Rectangle((int)(cx - 4 * s + i * 4 * s), (int)(cy + 2.5f * s + fall * s), 1, Math.Max(1, (int)(2 * s))), rain);
                }
                break;
            case WeatherKind.Snow:
                Body(cx, cy + 1f * s);
                for (int i = 0; i < 3; i++)
                {
                    float fall = (t * 2.5f + i * 1.7f) % 4f;
                    f.Fill(new Rectangle((int)(cx - 4 * s + i * 4 * s), (int)(cy + 2.5f * s + fall * s), Math.Max(1, (int)s), Math.Max(1, (int)s)), Pixel.White);
                }
                break;
            case WeatherKind.Thunderstorm:
                Body(cx, cy + 0.5f * s, dark: true);
                var bolt = new Pixel(255, 225, 50);
                int bx = (int)cx, by = (int)(cy + 1.5f * s);
                f.DrawLine(bx + 1, by, bx - 1, by + (int)(3 * s), bolt);
                f.DrawLine(bx - 1, by + (int)(3 * s), bx + 1, by + (int)(3 * s), bolt);
                f.DrawLine(bx + 1, by + (int)(3 * s), bx - 1, by + (int)(6 * s), bolt);
                break;
        }
    }
}
