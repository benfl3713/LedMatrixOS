using BdfFontParser;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Engine;

/// <summary>
/// Renders a BDF bitmap font via <see cref="SpriteBatch"/>.
/// All <c>DrawString</c> calls must be made inside a <c>Begin()</c> / <c>End()</c> block.
/// </summary>
public sealed class MatrixFont
{
    private readonly BdfFont _font;

    public MatrixFont(BdfFont font) => _font = font;

    /// <summary>Height of the font bounding box in pixels (useful for vertical centering).</summary>
    public int Height => _font.BoundingBox.Y;

    /// <summary>
    /// Returns the pixel width of <paramref name="text"/> rendered in this font.
    /// </summary>
    public int MeasureWidth(string text)
    {
        var map = _font.GetMapOfString(text);
        return map.GetLength(0);
    }

    /// <summary>
    /// Draws <paramref name="text"/> with its top-left at pixel (<paramref name="x"/>, <paramref name="y"/>).
    /// The y position is adjusted by the font's bounding-box offsets so the result matches
    /// the legacy <c>TextExtensions.DrawText</c> convention.
    /// </summary>
    public void DrawString(SpriteBatch sb, string text, int x, int y, Color color)
    {
        var map  = _font.GetMapOfString(text);
        int mapW = map.GetLength(0);
        int mapH = map.GetLength(1);
        int yBase = y - _font.BoundingBox.Y - _font.BoundingBox.OffsetY;

        for (int line = 0; line < mapH; line++)
        for (int bit  = 0; bit  < mapW; bit++)
        {
            if (!map[bit, line]) continue;
            int px = x + bit;
            int py = yBase + line;
            sb.DrawPixel(px, py, color);
        }
    }

    /// <summary>
    /// Draws text centred horizontally inside a region of <paramref name="containerWidth"/> pixels
    /// starting at <paramref name="x"/>.
    /// </summary>
    public void DrawStringCentred(SpriteBatch sb, string text, int x, int y, int containerWidth, Color color)
    {
        int w = MeasureWidth(text);
        int cx = x + (containerWidth - w) / 2;
        DrawString(sb, text, cx, y, color);
    }
}
