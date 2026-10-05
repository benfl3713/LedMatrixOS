using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>Draws non-negative integers from ten pre-rasterised digit runs, so a changing score never allocates.</summary>
internal sealed class NumberRun
{
    private readonly TextRun[] _digits = new TextRun[10];
    private readonly int _advance;

    public NumberRun(BdfFont font)
    {
        for (int i = 0; i < 10; i++)
        {
            _digits[i] = new TextRun();
            _digits[i].Set(font, ((char)('0' + i)).ToString());
        }
        _advance = _digits[0].Width;
    }

    public int WidthOf(int value)
    {
        int n = 1;
        for (int v = value / 10; v > 0; v /= 10) n++;
        return n * _advance;
    }

    /// <summary>Draws with the top left at (x, y); returns the x after the last digit.</summary>
    public int Draw(FrameBuffer frame, int x, int y, int value, Pixel color)
    {
        if (value < 0) value = 0;
        int n = WidthOf(value) / _advance;
        int px = x + (n - 1) * _advance;
        for (int v = value; ; v /= 10)
        {
            _digits[v % 10].Draw(frame, px, y, color);
            px -= _advance;
            if (v < 10) break;
        }
        return x + n * _advance;
    }
}
