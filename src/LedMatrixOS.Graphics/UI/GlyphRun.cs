using BdfFontParser;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// A string rasterised once with a BDF font. <c>DrawText</c> rebuilds the glyph map on every call (an allocation per frame),
/// so widgets keep one of these and only rebuild it when the text or font changes.
/// </summary>
internal sealed class GlyphRun
{
    private BdfFont? _font;
    private string _text = "";
    private bool[,]? _map;

    public int Width { get; private set; }

    /// <summary>Line height of the font, the same for every string.</summary>
    public int Height { get; private set; }

    /// <summary>Re-rasterises when needed; returns true if the size or content changed.</summary>
    public bool Set(BdfFont font, string text)
    {
        if (ReferenceEquals(font, _font) && string.Equals(text, _text)) return false;
        _font = font;
        _text = text;
        _map = text.Length == 0 ? null : font.GetMapOfString(text);
        Width = _map?.GetLength(0) ?? 0;
        Height = font.BoundingBox.Y;
        return true;
    }

    /// <summary>Draws with the top-left corner of the line box at (x, y).</summary>
    public void Draw(FrameBuffer frame, int x, int y, Pixel color, bool shadow, Pixel shadowColor)
    {
        if (_map is null) return;
        int rows = _map.GetLength(1);
        for (int line = 0; line < rows; line++)
        {
            int cy = y + line;
            for (int bit = 0; bit < Width; bit++)
            {
                if (!_map[bit, line]) continue;
                frame.SetPixel(x + bit, cy, color);
                if (shadow) frame.SetPixel(x + bit + 1, cy + 1, shadowColor);
            }
        }
    }

    /// <summary>
    /// Draws source rows [<paramref name="srcFrom"/>, <paramref name="srcTo"/>) squeezed or stretched onto <paramref name="destHeight"/> rows starting at <paramref name="destY"/>.
    /// Used by the flip-card effect.
    /// </summary>
    public void DrawRows(FrameBuffer frame, int x, int srcFrom, int srcTo, int destY, int destHeight, Pixel color)
    {
        if (_map is null || destHeight <= 0) return;
        int rows = _map.GetLength(1);
        for (int dy = 0; dy < destHeight; dy++)
        {
            int src = srcFrom + (int)((dy + 0.5f) * (srcTo - srcFrom) / destHeight);
            if (src < 0 || src >= rows) continue;
            for (int bit = 0; bit < Width; bit++)
                if (_map[bit, src]) frame.SetPixel(x + bit, destY + dy, color);
        }
    }
}

internal static class UiDraw
{
    /// <summary>
    /// Fills a rectangle with rounded corners. <c>SimpleGraphics.FillRoundedRect</c> allocates a closure per call,
    /// which would break the no-allocation guarantee of the widget layer.
    /// </summary>
    public static void FillRoundedRect(FrameBuffer frame, Rectangle rect, int radius, Pixel color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        float rad = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        for (int row = 0; row < rect.Height; row++)
        {
            int fromBottom = rect.Height - 1 - row;
            int edge = Math.Min(row, fromBottom);
            int inset = 0;
            if (rad > 0 && edge < rad)
            {
                float dy = rad - edge - 0.5f;
                inset = (int)MathF.Round(rad - MathF.Sqrt(Math.Max(0f, rad * rad - dy * dy)), MidpointRounding.AwayFromZero);
            }
            frame.Fill(new Rectangle(rect.X + inset, rect.Y + row, rect.Width - inset * 2, 1), color);
        }
    }
}
