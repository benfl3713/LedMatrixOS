using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Effects;

/// <summary>
/// Cheap bloom: keeps the part of each channel above <see cref="Threshold"/>, blurs it with a separable box filter
/// and adds it back. Scratch buffers are allocated once (again only if the frame size changes).
/// </summary>
public sealed class GlowEffect : IPostEffect
{
    /// <summary>Channel level (0-255) below which a pixel does not glow.</summary>
    public float Threshold { get; set; } = 140f;
    /// <summary>How much of the blurred glow is added back (0 = off).</summary>
    public float Strength { get; set; } = 1f;
    /// <summary>Box blur radius in pixels.</summary>
    public int Radius { get; set; } = 2;

    private float[] _bright = [];
    private float[] _tmp = [];
    private int _width, _height;

    public void Apply(FrameBuffer frame, FrameContext ctx)
    {
        if (Strength <= 0f || Radius < 1) return;
        int w = frame.Width, h = frame.Height, n = w * h;
        if (_width != w || _height != h)
        {
            _width = w; _height = h;
            _bright = new float[n * 3];
            _tmp = new float[n];
        }

        var px = frame.GetPixelsSpan();
        float t = Threshold;
        for (int i = 0; i < n; i++)
        {
            var p = px[i];
            _bright[i] = Math.Max(p.R - t, 0f);
            _bright[n + i] = Math.Max(p.G - t, 0f);
            _bright[2 * n + i] = Math.Max(p.B - t, 0f);
        }

        for (int c = 0; c < 3; c++)
        {
            var plane = _bright.AsSpan(c * n, n);
            BlurRows(plane, _tmp, w, h, Radius);
            BlurColumns(_tmp, plane, w, h, Radius);
        }

        float s = Strength;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var p = px[i];
                frame.SetPixel(x, y, new Pixel(
                    Add(p.R, _bright[i] * s), Add(p.G, _bright[n + i] * s), Add(p.B, _bright[2 * n + i] * s)));
            }
    }

    private static byte Add(byte c, float glow) => (byte)Math.Min(c + glow + 0.5f, 255f);

    // Sliding-window box blur; samples outside the frame count as zero.
    private static void BlurRows(ReadOnlySpan<float> src, Span<float> dst, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float sum = 0f;
            for (int x = 0; x <= Math.Min(r, w - 1); x++) sum += src[row + x];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = sum * inv;
                if (x + r + 1 < w) sum += src[row + x + r + 1];
                if (x - r >= 0) sum -= src[row + x - r];
            }
        }
    }

    private static void BlurColumns(ReadOnlySpan<float> src, Span<float> dst, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int y = 0; y <= Math.Min(r, h - 1); y++) sum += src[y * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = sum * inv;
                if (y + r + 1 < h) sum += src[(y + r + 1) * w + x];
                if (y - r >= 0) sum -= src[(y - r) * w + x];
            }
        }
    }
}
