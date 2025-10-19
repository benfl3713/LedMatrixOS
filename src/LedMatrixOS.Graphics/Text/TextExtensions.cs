using BdfFontParser;
using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Text;

public static class TextExtensions
{
    public static void DrawText(this FrameBuffer frame, BdfFont font, int x, int y, Pixel color, string text, int startLine = 0, int? endLine = null)
    {
        var map = font.GetMapOfString(text);
        DrawText(frame, font, x, y, color, map, startLine, endLine);
    }

    private static void DrawText(FrameBuffer frame, BdfFont font, int x, int y, Pixel color, bool[,] map, int startLine = 0, int? endLine = null)
    {
        bool withShadow = true;
        var width = map.GetLength(0);
        var height = map.GetLength(1);

        if (height > endLine)
            height = endLine.Value;

        for (int line = startLine; line < height; line++)
        {
            // iterate through every bit
            for (int bit = 0; bit < width; bit++)
            {
                var charX = bit + x;
                var charY = line + (y - font.BoundingBox.Y - font.BoundingBox.OffsetY);

                if (map[bit, line] && charX >= 0 && charY >= 0 && charX <= 256 - 1 && charY <= 64 - 1)
                {
                    try
                    {
                        frame.SetPixel(charX, charY, color);

                        if (withShadow && charX + 1 >= 0 && charY + 1 >= 0 && charX + 1 <= 256 - 1 && charY + 1 <= 64 - 1)
                        {
                            frame.SetPixel(charX + 1, charY + 1, Pixel.Black);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                }
            }
        }
    }

}
