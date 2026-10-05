using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>Seven neon balls on Lissajous paths; the summed inverse-square field is drawn as a glow with a bright iso-contour where they merge.</summary>
internal sealed class MetaballsVisual(int seed) : IDemoVisual
{
    private const int Balls = 7;
    private readonly Random _rng = new(seed);
    private readonly float[] _fx = new float[Balls], _fy = new float[Balls], _px = new float[Balls], _py = new float[Balls], _r = new float[Balls];
    private readonly float[] _bx = new float[Balls], _by = new float[Balls], _r2 = new float[Balls], _dy2 = new float[Balls];
    private float[] _dx2 = [];
    private int _w, _h;

    public string Name => "Metaballs";

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        _dx2 = new float[Balls * width];
        for (int i = 0; i < Balls; i++)
        {
            _fx[i] = 0.17f + _rng.NextSingle() * 0.25f;
            _fy[i] = 0.21f + _rng.NextSingle() * 0.3f;
            _px[i] = _rng.NextSingle();
            _py[i] = _rng.NextSingle();
            _r[i] = 0.3f + _rng.NextSingle() * 0.35f;
        }
    }

    public void Draw(Pixel[] dst, in DemoFrame f)
    {
        int w = _w, h = _h;
        float t = f.T;
        float u = h * 0.5f;
        for (int b = 0; b < Balls; b++)
        {
            _bx[b] = w * 0.5f + FastTrig.Sin(t * _fx[b] * 0.5f + _px[b]) * w * 0.42f;
            _by[b] = h * 0.5f + FastTrig.Sin(t * _fy[b] * 0.5f + _py[b]) * h * 0.4f;
            float r = _r[b] * u * f.Scale * 0.8f;
            _r2[b] = r * r;
            for (int x = 0; x < w; x++) { float d = x - _bx[b]; _dx2[b * w + x] = d * d; }
        }

        var colors = f.Ramp.Colors;
        for (int y = 0; y < h; y++)
        {
            for (int b = 0; b < Balls; b++) { float d = y - _by[b]; _dy2[b] = d * d; }
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float field = 0f;
                for (int b = 0; b < Balls; b++) field += _r2[b] / (_dx2[b * w + x] + _dy2[b] + 1f);
                float p = field * 0.2f;
                int idx = p >= 1f ? 255 : (int)(p * 255f);
                float bri = Kit.Smooth(field * 1.1f);
                var c = DemoCanvas.Shade(colors[idx], 0.25f + 0.85f * bri);
                // bright contour where the field crosses 1: the blob rims
                float rim = 1f - MathF.Abs(field - 1.15f) * 4.5f;
                if (rim > 0f) c = new Pixel(Kit.ToByte(c.R + 150 * rim), Kit.ToByte(c.G + 150 * rim), Kit.ToByte(c.B + 150 * rim));
                if (field < 0.18f) c = DemoCanvas.Shade(c, field / 0.18f);
                dst[row + x] = c;
            }
        }
    }
}
