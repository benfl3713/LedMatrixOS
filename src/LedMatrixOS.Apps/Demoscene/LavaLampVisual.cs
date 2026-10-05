using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>Big slow blobs that rise, sink and merge in a warm glass; soft thresholded edges over a dim vertical glow.</summary>
internal sealed class LavaLampVisual(int seed) : IDemoVisual
{
    private const int Blobs = 6;
    private readonly Random _rng = new(seed);
    private readonly float[] _fx = new float[Blobs], _fy = new float[Blobs], _px = new float[Blobs], _py = new float[Blobs], _r = new float[Blobs];
    private readonly float[] _bx = new float[Blobs], _by = new float[Blobs], _r2 = new float[Blobs], _dy2 = new float[Blobs];
    private float[] _dx2 = [];
    private int _w, _h;

    public string Name => "Lava Lamp";

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        _dx2 = new float[Blobs * width];
        for (int i = 0; i < Blobs; i++)
        {
            _fx[i] = 0.05f + _rng.NextSingle() * 0.08f;
            _fy[i] = 0.08f + _rng.NextSingle() * 0.1f;
            _px[i] = (i + _rng.NextSingle() * 0.6f) / Blobs; // spread horizontally
            _py[i] = _rng.NextSingle();
            _r[i] = 0.4f + _rng.NextSingle() * 0.3f;
        }
    }

    public void Draw(Pixel[] dst, in DemoFrame f)
    {
        int w = _w, h = _h;
        float t = f.T;
        float u = h * 0.5f;
        for (int b = 0; b < Blobs; b++)
        {
            _bx[b] = w * (0.08f + 0.84f * (0.5f + 0.5f * FastTrig.Sin(_px[b] + t * _fx[b] * 0.4f)));
            _by[b] = h * 0.5f + FastTrig.Sin(t * _fy[b] * 0.5f + _py[b]) * h * 0.62f;
            float r = _r[b] * u * f.Scale * 0.95f;
            _r2[b] = r * r;
            for (int x = 0; x < w; x++) { float d = x - _bx[b]; _dx2[b * w + x] = d * d; }
        }

        var colors = f.Ramp.Colors;
        float inv = 1f / (h - 1);
        for (int y = 0; y < h; y++)
        {
            for (int b = 0; b < Blobs; b++) { float d = y - _by[b]; _dy2[b] = d * d * 0.6f; }
            // warm glass: brighter near the base lamp, dim at the top
            var bg = DemoCanvas.Shade(colors[(int)((0.1f + 0.2f * y * inv) * 255f)], 0.35f + 0.35f * y * inv);
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float field = 0f;
                for (int b = 0; b < Blobs; b++) field += _r2[b] / (_dx2[b * w + x] + _dy2[b] + 1f);
                float a = Kit.Smooth((field - 0.85f) / 0.35f);
                if (a <= 0f) { dst[row + x] = bg; continue; }
                float core = Kit.Smooth((field - 0.9f) / 0.8f);
                var blob = DemoCanvas.Shade(colors[(int)((0.4f + 0.42f * core) * 255f)], 0.8f + 0.3f * core);
                dst[row + x] = Pixel.Lerp(bg, blob, a);
            }
        }
    }
}
