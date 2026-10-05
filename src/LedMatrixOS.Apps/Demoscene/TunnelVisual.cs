using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>Texture-mapped tunnel: angle, depth and fog are precomputed per pixel; a 64x64 soft checker texture scrolls through them.</summary>
internal sealed class TunnelVisual : IDemoVisual
{
    private const int Tex = 256;
    private int _w, _h;
    private float[] _ang = [], _depth = [], _fog = [];
    private readonly float[] _tex = new float[Tex * Tex];

    public string Name => "Tunnel";

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        _ang = new float[width * height];
        _depth = new float[width * height];
        _fog = new float[width * height];
        float cx = (width - 1) * 0.5f, cy = (height - 1) * 0.5f;
        float u = height * 0.5f;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = x - cx, dy = y - cy;
                float r = MathF.Sqrt(dx * dx + dy * dy) + 0.5f;
                int i = y * width + x;
                _ang[i] = MathF.Atan2(dy, dx) / Kit.TwoPi + 0.5f;
                _depth[i] = u * 1.2f / r;
                _fog[i] = Kit.Smooth((r - 2f) / (u * 0.9f)) * 0.85f + 0.15f * Kit.Smooth((r - 2f) / 6f);
            }
        for (int y = 0; y < Tex; y++)
            for (int x = 0; x < Tex; x++)
            {
                float a = MathF.Sin(x * Kit.TwoPi / Tex * 4f), b = MathF.Sin(y * Kit.TwoPi / Tex * 4f);
                float v = a * b; // soft checker
                v = 0.5f + 0.5f * MathF.Sign(v) * MathF.Pow(MathF.Abs(v), 0.5f);
                _tex[y * Tex + x] = 0.15f + 0.85f * v;
            }
    }

    public void Draw(Pixel[] dst, in DemoFrame f)
    {
        int n = _w * _h;
        float t = f.T;
        float around = 2f, depthK = 0.6f / f.Scale;
        float rot = t * 0.04f, fly = t * 0.7f;
        var colors = f.Ramp.Colors;
        for (int i = 0; i < n; i++)
        {
            float u = _ang[i] * around + rot;
            float v = _depth[i] * depthK + fly;
            int tx = (int)((u - MathF.Floor(u)) * Tex) & (Tex - 1);
            int ty = (int)((v - MathF.Floor(v)) * Tex) & (Tex - 1);
            float tex = _tex[ty * Tex + tx];
            int idx = DemoCanvas.PingPong(tex * 0.22f + _ang[i] + v * 0.015f + t * 0.02f);
            dst[i] = DemoCanvas.Shade(colors[idx], _fog[i] * (0.1f + 1.0f * tex * tex));
        }
    }
}
