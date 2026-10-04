using BdfFontParser;
using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// A string rasterised once. Widgets keep one and only rebuild it when the text or font changes,
/// so drawing allocates nothing per frame. The platform's own GlyphRun is internal to Graphics,
/// so apps use this as the public equivalent.
/// </summary>
public sealed class TextRun
{
    private BdfFont? _font;
    private string _text = "";
    private bool[,]? _map;

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Sets the text and font, returning true if either changed (and thus the run needs to be redrawn).</summary>
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

    /// <summary>Whether the glyph map has ink at (x, y); handy for baking a mask from text.</summary>
    public bool Ink(int x, int y) => _map is not null && (uint)x < (uint)Width && (uint)y < (uint)_map.GetLength(1) && _map[x, y];

    /// <summary>Draws the text with the top left of the line box at (x, y).</summary>
    /// <param name="shadow">If true, adds a black 1px drop shadow.</param>
    /// <param name="outline">If set, a 4-neighbour outline in this colour goes under the glyphs.</param>
    public void Draw(FrameBuffer frame, int x, int y, Pixel color, bool shadow = false, Pixel? outline = null)
    {
        if (_map is null) return;
        int rows = _map.GetLength(1);

        if (outline is { } o)
        {
            for (int line = 0; line < rows; line++)
                for (int bit = 0; bit < Width; bit++)
                {
                    if (!_map[bit, line]) continue;
                    frame.SetPixel(x + bit + 1, y + line, o);
                    frame.SetPixel(x + bit - 1, y + line, o);
                    frame.SetPixel(x + bit, y + line + 1, o);
                    frame.SetPixel(x + bit, y + line - 1, o);
                }
        }

        for (int line = 0; line < rows; line++)
        {
            for (int bit = 0; bit < Width; bit++)
            {
                if (!_map[bit, line]) continue;
                if (shadow) frame.SetPixel(x + bit + 1, y + line + 1, Pixel.Black);
            }
        }

        for (int line = 0; line < rows; line++)
        {
            for (int bit = 0; bit < Width; bit++)
                if (_map[bit, line]) frame.SetPixel(x + bit, y + line, color);
        }
    }
}
