using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using SixLabors.ImageSharp;
using Xunit;

namespace LedMatrixOS.Tests;

public class RoundedRectTests
{
    // The definition the span-based fill must match: a pixel is inside when its centre is within the corner circle.
    private static bool Inside(float px, float py, Rectangle r, int radius)
    {
        float rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        if (rad <= 0) return true;
        float cx = px < r.Left + rad ? r.Left + rad : px > r.Right - rad ? r.Right - rad : px;
        float cy = py < r.Top + rad ? r.Top + rad : py > r.Bottom - rad ? r.Bottom - rad : py;
        float dx = px - cx, dy = py - cy;
        return dx * dx + dy * dy <= rad * rad;
    }

    [Fact]
    public void FillRoundedRect_MatchesThePerPixelDefinition()
    {
        var white = new Pixel(255, 255, 255);
        for (int w = 1; w <= 40; w++)
            for (int h = 1; h <= 24; h++)
                for (int r = 0; r <= 5; r++)
                {
                    var frame = new FrameBuffer(48, 32);
                    var rect = new Rectangle(3, 2, w, h);
                    frame.FillRoundedRect(rect, r, white);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 48; x++)
                        {
                            bool expected = x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom && Inside(x + 0.5f, y + 0.5f, rect, r);
                            Assert.True(expected == (frame.GetPixel(x, y) == white), $"{w}x{h} radius {r} differs at ({x},{y})");
                        }
                }
    }

    [Fact]
    public void ShapeDrawing_DoesNotAllocate()
    {
        var frame = new FrameBuffer(64, 32);
        var rect = new Rectangle(2, 2, 40, 20);
        var white = new Pixel(255, 255, 255);
        for (int i = 0; i < 20; i++) { frame.FillRoundedRect(rect, 3, white); frame.DrawRoundedRect(rect, 3, white); frame.FillCircle(20, 16, 8, white); frame.DrawEllipse(rect, white); }

        // A real per-call allocation shows up in every window; one-off runtime (tiered JIT) allocations only hit some, so take the minimum.
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { frame.FillRoundedRect(rect, 3, white); frame.DrawRoundedRect(rect, 3, white); frame.FillCircle(20, 16, 8, white); frame.DrawEllipse(rect, white); }
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.True(windows.Min() < 256, "allocated bytes per 200-iteration window: " + string.Join(", ", windows));
    }
}
