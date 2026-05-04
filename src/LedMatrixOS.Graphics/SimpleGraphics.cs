using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics;

public static class SimpleGraphics
{
    public static void DrawLine(this FrameBuffer frame, int x1, int y1, int x2, int y2, Pixel color)
    {
        for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
        {
            for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
            {
                frame.SetPixel(x, y, color);
            }
        }
    }

    public static void DrawHorizontalLine(this FrameBuffer frame, int y, int length, Pixel color, int x = 0)
    {
        for (int i = 0; i < length; i++)
        {
            frame.SetPixel(x + i, y, color);
        }
    }
}
