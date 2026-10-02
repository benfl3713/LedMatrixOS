using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Effects;

/// <summary>
/// Brightness, saturation and a multiplicative tint over the whole frame. Default settings are the identity.
/// </summary>
public sealed class ColorGradeEffect : IPostEffect
{
    /// <summary>Scales all channels (1 = unchanged).</summary>
    public float Brightness { get; set; } = 1f;
    /// <summary>0 = greyscale, 1 = unchanged, above 1 = more vivid.</summary>
    public float Saturation { get; set; } = 1f;
    /// <summary>Multiplied into each pixel; white = no tint.</summary>
    public Pixel Tint { get; set; } = Pixel.White;

    /// <summary>Dim, warm preset for night use.</summary>
    public static ColorGradeEffect Night() => new()
    {
        Brightness = 0.5f,
        Saturation = 0.85f,
        Tint = new Pixel(255, 170, 90),
    };

    public void Apply(FrameBuffer frame, FrameContext ctx)
    {
        float sat = Saturation;
        float mr = Brightness * Tint.R / 255f, mg = Brightness * Tint.G / 255f, mb = Brightness * Tint.B / 255f;
        bool scale = mr != 1f || mg != 1f || mb != 1f;
        if (!scale && sat == 1f) return;

        var px = frame.GetPixelsSpan();
        int w = frame.Width;
        for (int i = 0; i < px.Length; i++)
        {
            float r = px[i].R, g = px[i].G, b = px[i].B;
            if (sat != 1f)
            {
                float luma = 0.299f * r + 0.587f * g + 0.114f * b;
                r = luma + (r - luma) * sat;
                g = luma + (g - luma) * sat;
                b = luma + (b - luma) * sat;
            }
            frame.SetPixel(i % w, i / w, new Pixel(ToByte(r * mr), ToByte(g * mg), ToByte(b * mb)));
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v + 0.5f, 0f, 255f);
}
