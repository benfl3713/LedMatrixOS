using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Engine;

/// <summary>
/// Primitive drawing helpers for SpriteBatch.
/// All Draw* methods must be called between <c>spriteBatch.Begin()</c> and <c>spriteBatch.End()</c>.
/// </summary>
public static class SpriteBatchExtensions
{
    // Single shared 1×1 white pixel texture – recreated if the GraphicsDevice changes.
    private static Texture2D? _pixel;

    private static Texture2D GetPixel(GraphicsDevice gd)
    {
        if (_pixel == null || _pixel.IsDisposed || _pixel.GraphicsDevice != gd)
        {
            _pixel = new Texture2D(gd, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        return _pixel;
    }

    /// <summary>Draws a single pixel at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void DrawPixel(this SpriteBatch sb, int x, int y, Color color)
        => sb.Draw(GetPixel(sb.GraphicsDevice), new Rectangle(x, y, 1, 1), color);

    /// <summary>Draws a filled rectangle.</summary>
    public static void DrawFilledRect(this SpriteBatch sb, int x, int y, int width, int height, Color color)
        => sb.Draw(GetPixel(sb.GraphicsDevice), new Rectangle(x, y, width, height), color);

    /// <summary>Draws a filled rectangle.</summary>
    public static void DrawFilledRect(this SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(GetPixel(sb.GraphicsDevice), rect, color);

    /// <summary>Draws a 1-pixel wide line between two points (Bresenham's algorithm).</summary>
    public static void DrawLine(this SpriteBatch sb, int x1, int y1, int x2, int y2, Color color)
    {
        int dx = Math.Abs(x2 - x1), dy = Math.Abs(y2 - y1);
        int sx = x1 < x2 ? 1 : -1, sy = y1 < y2 ? 1 : -1;
        int err = dx - dy;
        var pixel = GetPixel(sb.GraphicsDevice);
        while (true)
        {
            sb.Draw(pixel, new Rectangle(x1, y1, 1, 1), color);
            if (x1 == x2 && y1 == y2) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x1 += sx; }
            if (e2 <  dx) { err += dx; y1 += sy; }
        }
    }

    /// <summary>Draws a filled circle.</summary>
    public static void DrawFilledCircle(this SpriteBatch sb, int cx, int cy, int radius, Color color)
    {
        var pixel = GetPixel(sb.GraphicsDevice);
        int r2 = radius * radius;
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
            if (x * x + y * y <= r2)
                sb.Draw(pixel, new Rectangle(cx + x, cy + y, 1, 1), color);
    }

    /// <summary>
    /// Draws a regular polygon outline (e.g. triangle, hexagon).
    /// <paramref name="sides"/> must be ≥ 3. <paramref name="rotation"/> is in radians.
    /// </summary>
    public static void DrawPolygon(this SpriteBatch sb, float cx, float cy, int sides, float radius,
        float rotation, Color color)
    {
        var pts = new (int x, int y)[sides];
        for (int i = 0; i < sides; i++)
        {
            double angle = rotation + i * (Math.PI * 2 / sides);
            pts[i] = ((int)(cx + Math.Cos(angle) * radius), (int)(cy + Math.Sin(angle) * radius));
        }
        for (int i = 0; i < sides; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % sides];
            sb.DrawLine(a.x, a.y, b.x, b.y, color);
        }
    }

    /// <summary>Draws a filled regular polygon.</summary>
    public static void FillPolygon(this SpriteBatch sb, float cx, float cy, int sides, float radius,
        float rotation, Color color)
    {
        // Scanline fill: determine bounding box, fill row-by-row
        var pts = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            double angle = rotation + i * (Math.PI * 2 / sides);
            pts[i] = new Vector2((float)(cx + Math.Cos(angle) * radius), (float)(cy + Math.Sin(angle) * radius));
        }
        int minY = (int)pts.Min(p => p.Y), maxY = (int)pts.Max(p => p.Y);
        var pixel = GetPixel(sb.GraphicsDevice);
        for (int y = minY; y <= maxY; y++)
        {
            var intersects = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % sides];
                if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    intersects.Add((int)(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X)));
            }
            intersects.Sort();
            for (int k = 0; k + 1 < intersects.Count; k += 2)
                sb.Draw(pixel, new Rectangle(intersects[k], y, intersects[k + 1] - intersects[k], 1), color);
        }
    }

    /// <summary>
    /// Converts HSV to a MonoGame <see cref="Color"/>.
    /// <paramref name="hue"/> is in degrees (0–360); <paramref name="saturation"/> and
    /// <paramref name="value"/> are in the range 0–1.
    /// </summary>
    public static Color HsvToColor(float hue, float saturation, float value)
    {
        hue = ((hue % 360f) + 360f) % 360f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value      = Math.Clamp(value,      0f, 1f);

        float c = value * saturation;
        float x = c * (1f - MathF.Abs((hue / 60f) % 2f - 1f));
        float m = value - c;

        float r, g, b;
        if      (hue < 60)  { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else                { r = c; g = 0; b = x; }

        return new Color((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
