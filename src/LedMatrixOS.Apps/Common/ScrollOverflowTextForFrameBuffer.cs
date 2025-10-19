using System.Diagnostics;
using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;

namespace LedMatrixOS.Apps.Common;

public class ScrollOverflowTextForFrameBuffer
{
    private readonly int _x;
    private readonly int _y;
    private readonly int _maxWidth;
    private readonly BdfFont _font;
    private readonly Pixel _color;

    public ScrollOverflowTextForFrameBuffer(int x, int y, int maxWidth, BdfFont font, Pixel? color = null)
    {
        _x = x;
        _y = y;
        _maxWidth = maxWidth;
        _font = font;
        _color = color ?? new Pixel(150, 150, 150);
    }

    private int _currentOffset = 0;
    private readonly Stopwatch _initialPause = Stopwatch.StartNew();
    private readonly Stopwatch _endPause = new Stopwatch();

    public void Draw(FrameBuffer frame, string text)
    {
        if (text.Length <= _maxWidth || _initialPause.ElapsedMilliseconds < 4000)
        {
            string toShow = text;
            if (text.Length > _maxWidth)
                toShow = text[.._maxWidth];
            
            frame.DrawText(_font, _x, _y, _color, toShow);
            return;
        }

        var textMap = _font.GetMapOfString(text);
        int textWidth = textMap.GetLength(0);
        int startX = _x - _currentOffset;
        // if (startX + textWidth < _x)
        // {
        //     _currentOffset = 0; // Reset offset if text is completely off-screen
        //     _initialPause.Restart();
        // }

        for (int i = 0; i < textMap.GetLength(1); i++)
        {
            for (int j = 0; j < textMap.GetLength(0); j++)
            {
                if (startX + j < _x || startX + j >= _x + (_maxWidth * _font.BoundingBox.X))
                {
                    continue;
                }
                
                var charX = j + startX;
                var charY = i + (_y - _font.BoundingBox.Y - _font.BoundingBox.OffsetY);
                if (textMap[j, i])
                {
                    frame.SetPixel(charX, charY, _color);
                    frame.SetPixel(charX + 1, charY + 1, Pixel.Black);
                }
            }
        }
        
        if ((text.Length * _font.BoundingBox.X) - _currentOffset > _maxWidth * _font.BoundingBox.X)
        {
            _currentOffset++; // Increment offset for the next frame
        }
        else if (!_endPause.IsRunning)
        {
            _endPause.Restart();
        }
        if (_endPause.ElapsedMilliseconds > 2000)
        {
            _currentOffset = 0; // Reset offset after a pause
            _initialPause.Restart();
            _endPause.Reset();
        }
    }
}
