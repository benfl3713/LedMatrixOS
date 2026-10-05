using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>Classic sum-of-sines plasma. Row and column terms are tabulated per frame; the radial terms use a fixed and a drifting source.</summary>
internal sealed class PlasmaVisual : IDemoVisual
{
    private int _w, _h;
    private float[] _col = [], _row = [], _dx = [], _dy2 = [];
    private float[] _centreDist = [];

    public string Name => "Plasma";

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        _col = new float[width]; _row = new float[height];
        _dx = new float[width]; _dy2 = new float[height];
        _centreDist = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = x - width * 0.5f, dy = (y - height * 0.5f) * 2f;
                _centreDist[y * width + x] = MathF.Sqrt(dx * dx + dy * dy);
            }
    }

    public void Draw(Pixel[] dst, in DemoFrame f)
    {
        int w = _w, h = _h;
        float t = f.T, k = 1f / f.Scale;
        for (int x = 0; x < w; x++) _col[x] = FastTrig.Sin(x * 0.024f * k + t * 0.07f) + FastTrig.Sin(x * 0.0095f * k - t * 0.05f);
        for (int y = 0; y < h; y++) _row[y] = FastTrig.Sin(y * 0.045f * k + t * 0.09f);

        float sx = w * (0.5f + 0.38f * FastTrig.Sin(t * 0.031f)), sy = h * (0.5f + 0.3f * FastTrig.Sin(t * 0.047f + 0.2f));
        for (int x = 0; x < w; x++) { float d = x - sx; _dx[x] = d * d; }
        for (int y = 0; y < h; y++) { float d = (y - sy) * 2f; _dy2[y] = d * d; }

        var colors = f.Ramp.Colors;
        float diagK = 0.014f * k, radK = 0.02f * k;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float ry = _row[y], dy2 = _dy2[y];
            for (int x = 0; x < w; x++)
            {
                float v = _col[x] + ry
                          + FastTrig.Sin((x * 0.7f + y * 1.4f) * diagK - t * 0.06f)
                          + FastTrig.Sin(_centreDist[row + x] * radK + t * 0.1f)
                          + FastTrig.Sin(MathF.Sqrt(_dx[x] + dy2) * radK * 1.3f - t * 0.08f);
                dst[row + x] = colors[DemoCanvas.PingPong(v * 0.14f + t * 0.025f)];
            }
        }
    }
}
