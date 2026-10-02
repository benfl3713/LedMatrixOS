using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Effects;

/// <summary>
/// CRT look: darkens one row out of every <see cref="Period"/>.
/// </summary>
public sealed class ScanlinesEffect : IPostEffect
{
    /// <summary>How much the dark rows are dimmed (0 = off, 1 = black).</summary>
    public float Intensity { get; set; } = 0.35f;
    /// <summary>Rows per repeat; the last row of each group is the dark one.</summary>
    public int Period { get; set; } = 2;

    public void Apply(FrameBuffer frame, FrameContext ctx)
    {
        if (Intensity <= 0f || Period < 1) return;
        int keep = (int)((1f - Math.Min(Intensity, 1f)) * 256f + 0.5f); // 8.8 fixed point
        var px = frame.GetPixelsSpan();
        for (int y = Period - 1; y < frame.Height; y += Period)
        {
            int row = y * frame.Width;
            for (int x = 0; x < frame.Width; x++)
            {
                var p = px[row + x];
                frame.SetPixel(x, y, new Pixel((byte)(p.R * keep >> 8), (byte)(p.G * keep >> 8), (byte)(p.B * keep >> 8)));
            }
        }
    }
}
