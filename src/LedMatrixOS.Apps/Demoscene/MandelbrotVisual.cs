using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>
/// Endless Mandelbrot zoom into the seahorse valley and back out. The escape-time pass runs at half resolution (every other
/// pixel in each axis) with an iteration budget that grows slowly with depth and is capped, then is bilinearly upsampled so frame
/// time stays small on a Raspberry Pi. Smooth (fractional) iteration counts avoid colour banding.
/// </summary>
internal sealed class MandelbrotVisual : IDemoVisual
{
    private const double CentreX = -0.743643887037151, CentreY = 0.131825904205330;
    private const float Period = 70f;     // seconds for one zoom in + out cycle at speed 5
    private const int MaxIter = 96;

    private int _w, _h, _lw, _lh;
    private float[] _low = [];            // smooth iteration value; negative = inside the set

    public string Name => "Mandelbrot Zoom";

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        _lw = (width + 1) / 2 + 1; _lh = (height + 1) / 2 + 1;
        _low = new float[_lw * _lh];
    }

    public void Draw(Pixel[] dst, in DemoFrame f)
    {
        int w = _w, h = _h, lw = _lw, lh = _lh;
        float depth = MathF.Log(2500f * f.Scale * f.Scale);                    // ln of the maximum zoom factor
        float phase = 0.5f - 0.5f * FastTrig.Cos(f.T / Period);
        float lz = depth * phase;
        double widthUnits = 5.0 * Math.Exp(-lz);
        double step = widthUnits / (w - 1) * 2.0;                               // one low-res pixel
        int budget = Math.Min(MaxIter, 36 + (int)(lz * 7f));
        double x0 = CentreX - widthUnits * 0.5, y0 = CentreY - widthUnits * (h / (double)w) * 0.5;

        for (int ly = 0; ly < lh; ly++)
        {
            double ci = y0 + ly * step;
            int row = ly * lw;
            for (int lx = 0; lx < lw; lx++)
            {
                double cr = x0 + lx * step;
                double zr = 0, zi = 0, zr2 = 0, zi2 = 0;
                int n = 0;
                while (n < budget && zr2 + zi2 <= 256.0)
                {
                    zi = 2.0 * zr * zi + ci;
                    zr = zr2 - zi2 + cr;
                    zr2 = zr * zr; zi2 = zi * zi;
                    n++;
                }
                _low[row + lx] = n >= budget ? -1f : n + 1f - MathF.Log2(0.5f * MathF.Log2((float)(zr2 + zi2)));
            }
        }

        var colors = f.Ramp.Colors;
        float shift = f.T * 0.012f;
        for (int y = 0; y < h; y++)
        {
            int ly = y >> 1;
            float fy = (y & 1) * 0.5f;
            for (int x = 0; x < w; x++)
            {
                int lx = x >> 1;
                float fx = (x & 1) * 0.5f;
                float a = _low[ly * lw + lx], b = _low[ly * lw + lx + 1], c = _low[(ly + 1) * lw + lx], d = _low[(ly + 1) * lw + lx + 1];
                float v;
                if (a < 0f || b < 0f || c < 0f || d < 0f) v = a; // inside / boundary: nearest, keeps the set crisp
                else v = (a + (b - a) * fx) * (1f - fy) + (c + (d - c) * fx) * fy;
                if (v < 0f) { dst[y * w + x] = Pixel.Black; continue; }
                float p = v * 0.03f + shift;
                dst[y * w + x] = DemoCanvas.Shade(colors[DemoCanvas.PingPong(p)], 0.55f + 0.45f * Kit.Smooth(v * 0.25f));
            }
        }
    }
}
