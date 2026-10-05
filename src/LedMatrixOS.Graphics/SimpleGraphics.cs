using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics;

public static class SimpleGraphics
{
    /// <summary>
    /// Bresenham line between two points (both inclusive).
    /// </summary>
    public static void DrawLine(this FrameBuffer frame, int x1, int y1, int x2, int y2, Pixel color)
    {
        int dx = Math.Abs(x2 - x1), dy = -Math.Abs(y2 - y1);
        int sx = x1 < x2 ? 1 : -1, sy = y1 < y2 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            frame.SetPixel(x1, y1, color);
            if (x1 == x2 && y1 == y2) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x1 += sx; }
            if (e2 <= dx) { err += dx; y1 += sy; }
        }
    }

    public static void DrawRect(this FrameBuffer frame, Rectangle rect, Pixel color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (rect.Width <= 2 || rect.Height <= 2)
        {
            frame.Fill(rect, color);
            return;
        }

        frame.Fill(new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        frame.Fill(new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        frame.Fill(new Rectangle(rect.X, rect.Y + 1, 1, rect.Height - 2), color);
        frame.Fill(new Rectangle(rect.Right - 1, rect.Y + 1, 1, rect.Height - 2), color);
    }

    public static void FillRect(this FrameBuffer frame, Rectangle rect, Pixel color) => frame.Fill(rect, color);

    public static void DrawRoundedRect(this FrameBuffer frame, Rectangle rect, int radius, Pixel color)
        => DrawShape(frame, rect, color, radius, outline: true);

    /// <summary>Filled rounded rectangle, one span fill per row (same pixels as testing every pixel against the corner circles).</summary>
    public static void FillRoundedRect(this FrameBuffer frame, Rectangle rect, int radius, Pixel color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        float rad = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        for (int row = 0; row < rect.Height; row++)
        {
            int edge = Math.Min(row, rect.Height - 1 - row);
            int inset = 0;
            if (rad > 0 && edge < rad)
            {
                float dy = rad - edge - 0.5f;
                inset = (int)MathF.Round(rad - MathF.Sqrt(Math.Max(0f, rad * rad - dy * dy)), MidpointRounding.AwayFromZero);
            }
            frame.Fill(new Rectangle(rect.X + inset, rect.Y + row, rect.Width - inset * 2, 1), color);
        }
    }

    public static void DrawEllipse(this FrameBuffer frame, Rectangle bounds, Pixel color)
        => DrawShape(frame, bounds, color, radius: -1, outline: true);

    public static void FillEllipse(this FrameBuffer frame, Rectangle bounds, Pixel color)
        => DrawShape(frame, bounds, color, radius: -1, outline: false);

    public static void DrawCircle(this FrameBuffer frame, int cx, int cy, int radius, Pixel color)
        => DrawEllipse(frame, new Rectangle(cx - radius, cy - radius, radius * 2 + 1, radius * 2 + 1), color);

    public static void FillCircle(this FrameBuffer frame, int cx, int cy, int radius, Pixel color)
        => FillEllipse(frame, new Rectangle(cx - radius, cy - radius, radius * 2 + 1, radius * 2 + 1), color);

    /// <summary>
    /// Fills a rectangle with a gradient from <paramref name="from"/> (left/top) to <paramref name="to"/> (right/bottom).
    /// </summary>
    public static void FillLinearGradient(this FrameBuffer frame, Rectangle rect, Pixel from, Pixel to, bool vertical = false)
    {
        int steps = vertical ? rect.Height : rect.Width;
        if (steps <= 0 || (vertical ? rect.Width : rect.Height) <= 0) return;

        for (int i = 0; i < steps; i++)
        {
            var color = Pixel.Lerp(from, to, steps == 1 ? 0f : i / (float)(steps - 1));
            frame.Fill(vertical
                ? new Rectangle(rect.X, rect.Y + i, rect.Width, 1)
                : new Rectangle(rect.X + i, rect.Y, 1, rect.Height), color);
        }
    }

    /// <summary>
    /// Fills a disc with a gradient from <paramref name="center"/> colour to <paramref name="edge"/> colour.
    /// </summary>
    public static void FillRadialGradient(this FrameBuffer frame, int cx, int cy, int radius, Pixel center, Pixel edge)
    {
        if (radius < 0) return;
        float r = radius + 0.5f;
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                float dist = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (dist > r) continue;
                frame.SetPixel(x, y, Pixel.Lerp(center, edge, dist / r));
            }
        }
    }

    /// <summary>
    /// Draws a horizontal progress bar; <paramref name="progress"/> is 0-1. Optional 1px border.
    /// </summary>
    public static void DrawProgressBar(this FrameBuffer frame, Rectangle rect, float progress, Pixel fill, Pixel background, Pixel? border = null)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var inner = rect;
        if (border is { } borderColor)
        {
            frame.DrawRect(rect, borderColor);
            inner = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
            if (inner.Width <= 0 || inner.Height <= 0) return;
        }

        int filled = (int)MathF.Round(inner.Width * Math.Clamp(progress, 0f, 1f));
        frame.Fill(new Rectangle(inner.X, inner.Y, filled, inner.Height), fill);
        frame.Fill(new Rectangle(inner.X + filled, inner.Y, inner.Width - filled, inner.Height), background);
    }

    // Shapes are rasterised per pixel against an inside test (no delegates, so nothing is allocated per call); the outline is the shape
    // minus itself inset by 1px. A radius of -1 selects the ellipse test, anything else the rounded-rectangle test.
    private static void DrawShape(FrameBuffer frame, Rectangle rect, Pixel color, int radius, bool outline)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var inner = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);

        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                if (!Inside(px, py, rect, radius)) continue;
                if (outline && inner.Width > 0 && inner.Height > 0 && Inside(px, py, inner, radius)) continue;
                frame.SetPixel(x, y, color);
            }
        }
    }

    private static bool Inside(float px, float py, Rectangle r, int radius) =>
        radius < 0 ? InEllipse(px, py, r) : InRoundedRect(px, py, r, radius);

    private static bool InEllipse(float px, float py, Rectangle r)
    {
        float a = r.Width / 2f, b = r.Height / 2f;
        float dx = (px - (r.X + a)) / a, dy = (py - (r.Y + b)) / b;
        return dx * dx + dy * dy <= 1f;
    }

    private static bool InRoundedRect(float px, float py, Rectangle r, int radius)
    {
        if (px < r.Left || px > r.Right || py < r.Top || py > r.Bottom) return false;
        float rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        if (rad <= 0) return true;

        // Distance into the nearest corner square, if any.
        float cx = px < r.Left + rad ? r.Left + rad : px > r.Right - rad ? r.Right - rad : px;
        float cy = py < r.Top + rad ? r.Top + rad : py > r.Bottom - rad ? r.Bottom - rad : py;
        float dx = px - cx, dy = py - cy;
        return dx * dx + dy * dy <= rad * rad;
    }
}
