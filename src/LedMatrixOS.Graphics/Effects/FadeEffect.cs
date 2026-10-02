using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Effects;

/// <summary>
/// Global fade to black: 1 = unchanged, 0 = black.
/// </summary>
public sealed class FadeEffect : IPostEffect
{
    public float Level { get; set; } = 1f;

    public void Apply(FrameBuffer frame, FrameContext ctx)
    {
        float level = Math.Clamp(Level, 0f, 1f);
        if (level >= 1f) return;
        int k = (int)(level * 256f + 0.5f); // 8.8 fixed point
        var px = frame.GetPixelsSpan();
        int w = frame.Width;
        for (int i = 0; i < px.Length; i++)
        {
            var p = px[i];
            frame.SetPixel(i % w, i / w, new Pixel((byte)(p.R * k >> 8), (byte)(p.G * k >> 8), (byte)(p.B * k >> 8)));
        }
    }
}
