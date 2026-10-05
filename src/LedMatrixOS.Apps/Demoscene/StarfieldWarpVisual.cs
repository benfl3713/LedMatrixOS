using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>Flying through stars: 3D points fall toward the camera and are drawn as anti-aliased streaks whose length follows the warp speed.</summary>
internal sealed class StarfieldWarpVisual(int seed) : IDemoVisual
{
    private const int Count = 700;
    private readonly Random _rng = new(seed);
    private readonly float[] _x = new float[Count], _y = new float[Count], _z = new float[Count], _hue = new float[Count];
    private int _w, _h;

    public string Name => "Starfield Warp";

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        for (int i = 0; i < Count; i++) { Spawn(i); _z[i] = 0.03f + _rng.NextSingle() * 0.97f; }
    }

    private void Spawn(int i)
    {
        _x[i] = (_rng.NextSingle() * 2f - 1f) * 4.2f;
        _y[i] = (_rng.NextSingle() * 2f - 1f) * 1.1f;
        _z[i] = 1f;
        _hue[i] = _rng.NextSingle();
    }

    public void Draw(Pixel[] dst, in DemoFrame f)
    {
        int w = _w, h = _h;
        Array.Clear(dst, 0, w * h);
        float cx = w * 0.5f, cy = h * 0.5f;
        float fov = h * 0.5f * f.Scale * 0.9f;
        float warp = 0.42f + 0.3f * FastTrig.Sin(f.T * 0.03f);
        float speed = warp * f.Dt;
        float trail = 0.02f + warp * 0.09f;
        var colors = f.Ramp.Colors;
        for (int i = 0; i < Count; i++)
        {
            float z = _z[i] - speed;
            if (z < 0.025f) { Spawn(i); continue; }
            _z[i] = z;
            float x = _x[i], y = _y[i];
            float hx = cx + x / z * fov, hy = cy + y / z * fov;
            if (hx < -8f || hx > w + 8f || hy < -8f || hy > h + 8f) { Spawn(i); continue; }
            float zt = z + trail;
            float tx = cx + x / zt * fov, ty = cy + y / zt * fov;
            float near = 1f - z;
            float k = 0.5f + 1.0f * near * near;
            var c = colors[(int)(_hue[i] * 200f + 55f)];
            // close stars wash to white
            float white = near * near * near;
            var col = new Pixel(Kit.ToByte(c.R + (255 - c.R) * white), Kit.ToByte(c.G + (255 - c.G) * white), Kit.ToByte(c.B + (255 - c.B) * white));
            DemoCanvas.Line(dst, w, h, tx, ty, hx, hy, col, MathF.Min(k, 1f), true);
        }
    }
}
