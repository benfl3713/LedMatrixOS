using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Effects;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Visuals;

/// <summary>
/// Per-pixel shader with all the expensive geometry (angle, log-radius, core falloff, blend weights) precomputed at resize, so a frame is
/// a few table-driven sines and a palette lookup per pixel. Styles: log-spiral vortices (one or a counter-rotating pair), a contoured
/// plasma and a depth tunnel. Comets with ring-buffer trails ride the spiral on top and a bloom pass finishes it.
/// </summary>
internal sealed class SpiralVisual : VisualNode
{
    private const int CometCount = 14;
    private const int TrailLength = 34;

    private struct Comet
    {
        public int Vortex;
        public float Theta, Rho, Spin, Speed, Hue;
        public int Head;
    }

    private readonly RainbowSpiralApp _owner;
    private readonly Random _rng;
    private readonly GlowEffect _glow = new() { Threshold = 150f, Radius = 3, Strength = 0.8f };
    private readonly ColorRamp _ramp = new();
    private readonly Comet[] _comets = new Comet[CometCount];
    private readonly float[] _trailX = new float[CometCount * TrailLength];
    private readonly float[] _trailY = new float[CometCount * TrailLength];

    private int _w, _h;
    private float[] _angA = [], _lrA = [], _angB = [], _lrB = [], _rad = [], _core = [], _weightA = [];
    private float[] _sx = [], _sy = [];
    private float _phase, _pulse;
    private string? _paletteName;
    private string? _layoutKey;
    private int _vortexCount = 2;
    private float _ex = 1.6f;
    private float _cxA, _cxB, _cy;
    private FrameContext _ctx;

    public SpiralVisual(RainbowSpiralApp owner, int seed)
    {
        _owner = owner;
        _rng = new Random(seed);
        _phase = (float)(_rng.NextDouble() * 6.0);
    }

    protected override void Step(FrameContext ctx)
    {
        _ctx = ctx;
        var host = Host!;
        bool twin = _owner.Style == "Twin Vortex";
        string key = twin ? "twin" : "single";
        if (_w != host.Width || _h != host.Height || _layoutKey != key) Resize(host.Width, host.Height, twin, key);
        if (_paletteName != _owner.Palette) BuildPalette(_owner.Palette);

        float speed = 0.25f + 0.15f * _owner.Speed;
        _phase += Dt * speed;
        _pulse = 0.78f + 0.22f * FastTrig.Sin(_phase * 0.35f);

        if (_owner.Comets) StepComets(speed);
    }

    private void Resize(int w, int h, bool twin, string key)
    {
        _w = w;
        _h = h;
        _layoutKey = key;
        _vortexCount = twin ? 2 : 1;
        int n = w * h;
        _angA = new float[n]; _lrA = new float[n]; _angB = new float[n]; _lrB = new float[n];
        _rad = new float[n]; _core = new float[n]; _weightA = new float[n];
        _sx = new float[w]; _sy = new float[h];
        _cy = (h - 1) / 2f;
        _cxA = twin ? w * 0.25f : (w - 1) / 2f;
        _cxB = w * 0.75f;
        _ex = twin ? 1.55f : 2.5f; // horizontal squash so spirals fill a 4:1 panel

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float dy = y - _cy;
                float dxA = (x - _cxA) / _ex;
                _angA[i] = MathF.Atan2(dy, dxA) / Kit.TwoPi;
                float rA = MathF.Sqrt(dxA * dxA + dy * dy);
                _lrA[i] = MathF.Log(rA + 1.5f);
                float rMin = rA;
                float coreSum = 1f / (1f + rA * rA / 28f);
                float wA = 1f;
                if (twin)
                {
                    float dxB = (x - _cxB) / _ex;
                    _angB[i] = MathF.Atan2(dy, dxB) / Kit.TwoPi;
                    float rB = MathF.Sqrt(dxB * dxB + dy * dy);
                    _lrB[i] = MathF.Log(rB + 1.5f);
                    coreSum += 1f / (1f + rB * rB / 28f);
                    rMin = Math.Min(rA, rB);
                    wA = 1f - Kit.Smooth((x - (w * 0.5f - 34f)) / 68f);
                }
                _rad[i] = rMin;
                _core[i] = coreSum;
                _weightA[i] = wA;
            }

        for (int c = 0; c < CometCount; c++) SpawnComet(ref _comets[c], c, true);
        _paletteName = null;
    }

    private void BuildPalette(string name)
    {
        _paletteName = name;
        switch (name)
        {
            case "Neon":
                _ramp.Fill([
                    (0f, new Pixel(255, 0, 140)), (0.25f, new Pixel(130, 20, 255)), (0.5f, new Pixel(0, 200, 255)),
                    (0.75f, new Pixel(0, 255, 160)), (1f, new Pixel(255, 0, 140))]);
                break;
            case "Sunset":
                _ramp.Fill([
                    (0f, new Pixel(255, 40, 80)), (0.25f, new Pixel(255, 140, 20)), (0.5f, new Pixel(255, 230, 90)),
                    (0.75f, new Pixel(200, 30, 190)), (1f, new Pixel(255, 40, 80))]);
                break;
            case "Ocean":
                _ramp.Fill([
                    (0f, new Pixel(10, 40, 255)), (0.3f, new Pixel(0, 170, 255)), (0.55f, new Pixel(0, 255, 200)),
                    (0.8f, new Pixel(70, 90, 255)), (1f, new Pixel(10, 40, 255))]);
                break;
            case "Aurora":
                _ramp.Fill([
                    (0f, new Pixel(30, 255, 90)), (0.3f, new Pixel(0, 230, 200)), (0.55f, new Pixel(120, 70, 255)),
                    (0.8f, new Pixel(255, 60, 200)), (1f, new Pixel(30, 255, 90))]);
                break;
            default:
                for (int i = 0; i < ColorRamp.Size; i++) _ramp.Colors[i] = Pixel.FromHsv(i * 360f / ColorRamp.Size, 1f, 1f);
                break;
        }
    }

    private void SpawnComet(ref Comet c, int index, bool scatter)
    {
        c.Vortex = _vortexCount == 2 ? index & 1 : 0;
        c.Rho = scatter ? 2f + (float)_rng.NextDouble() * 30f : 1.5f;
        c.Theta = (float)_rng.NextDouble();
        c.Spin = (c.Vortex == 0 ? 1f : -1f) * (0.10f + (float)_rng.NextDouble() * 0.07f);
        c.Speed = 9f + (float)_rng.NextDouble() * 9f;
        c.Hue = (float)_rng.NextDouble();
        c.Head = 0;
        float cx = c.Vortex == 0 ? _cxA : _cxB;
        for (int k = 0; k < TrailLength; k++)
        {
            _trailX[index * TrailLength + k] = cx;
            _trailY[index * TrailLength + k] = _cy;
        }
    }

    private void StepComets(float speed)
    {
        for (int i = 0; i < CometCount; i++)
        {
            ref var c = ref _comets[i];
            c.Rho += c.Speed * Dt * speed;
            c.Theta += c.Spin * Dt * speed * (4f / (1f + c.Rho * 0.08f));
            if (c.Rho > 38f) SpawnComet(ref c, i, false);
            float cx = c.Vortex == 0 ? _cxA : _cxB;
            c.Head = (c.Head + 1) % TrailLength;
            _trailX[i * TrailLength + c.Head] = cx + FastTrig.Cos(c.Theta) * c.Rho * _ex;
            _trailY[i * TrailLength + c.Head] = _cy + FastTrig.Sin(c.Theta) * c.Rho;
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_w == 0 || frame.Width != _w || frame.Height != _h) return;

        float ph = _phase;
        for (int x = 0; x < _w; x++) _sx[x] = FastTrig.Sin(x * 0.017f + ph * 0.09f);
        for (int y = 0; y < _h; y++) _sy[y] = FastTrig.Sin(y * 0.052f - ph * 0.11f);

        switch (_owner.Style)
        {
            case "Plasma": RenderPlasma(frame, ph); break;
            case "Tunnel": RenderTunnel(frame, ph); break;
            default: RenderVortex(frame, ph); break;
        }

        if (_owner.Comets && _owner.Style != "Plasma") DrawComets(frame);
        if (_owner.Glow) _glow.Apply(frame, _ctx);
    }

    private void RenderVortex(FrameBuffer frame, float ph)
    {
        int arms = Math.Clamp(_owner.Arms, 1, 8);
        const float pitch = 2.3f;
        float hueScale = 1f / arms;
        float spin = ph * 0.55f;
        bool twin = _vortexCount == 2;
        var colors = _ramp.Colors;
        float pulse = _pulse;
        float hueShift = ph * 0.04f;

        for (int y = 0; y < _h; y++)
        {
            int row = y * _w;
            float sy = _sy[y];
            for (int x = 0; x < _w; x++)
            {
                int i = row + x;
                float p = (_sx[x] + sy + FastTrig.Sin(_rad[i] * 0.02f - ph * 0.12f)) * (1f / 3f);
                float uA = _angA[i] * arms + _lrA[i] * pitch - spin + p * 0.14f;
                ArmColor(uA, hueScale, hueShift, colors, out float r, out float g, out float b);
                if (twin)
                {
                    // Colour (not hue index) is blended: each vortex's angle wraps at its own branch cut.
                    float uB = _angB[i] * arms + _lrB[i] * pitch + spin + p * 0.14f;
                    ArmColor(uB, hueScale, hueShift, colors, out float r2, out float g2, out float b2);
                    float wA = _weightA[i];
                    r = r * wA + r2 * (1f - wA);
                    g = g * wA + g2 * (1f - wA);
                    b = b * wA + b2 * (1f - wA);
                }

                float core = _core[i] * pulse;
                float c = core * core * 230f;
                frame.SetPixel(x, y, new Pixel(Kit.ToByte(r + c), Kit.ToByte(g + c), Kit.ToByte(b + c)));
            }
        }
    }

    /// <summary>Arm brightness from the spiral phase; the arm takes the palette colour, the gap a dim complementary one.</summary>
    private static void ArmColor(float u, float hueScale, float hueShift, Pixel[] colors, out float r, out float g, out float b)
    {
        float arm = 0.5f + 0.5f * FastTrig.Cos(u);
        arm = arm * arm * (3f - 2f * arm);
        float hue = u * hueScale + hueShift;
        hue -= MathF.Floor(hue);
        float hue2 = hue + 0.5f;
        hue2 -= MathF.Floor(hue2);
        var main = colors[(int)(hue * 255.99f)];
        var gap = colors[(int)(hue2 * 255.99f)];
        const float k = 0.13f;
        r = gap.R * k + (main.R - gap.R * k) * arm;
        g = gap.G * k + (main.G - gap.G * k) * arm;
        b = gap.B * k + (main.B - gap.B * k) * arm;
    }

    private void RenderPlasma(FrameBuffer frame, float ph)
    {
        var colors = _ramp.Colors;
        for (int y = 0; y < _h; y++)
        {
            int row = y * _w;
            float q2 = _sy[y];
            for (int x = 0; x < _w; x++)
            {
                float q1 = _sx[x];
                float sdr = FastTrig.Sin(_rad[row + x] * 0.014f - ph * 0.08f);
                float a = FastTrig.Sin(x * 0.0125f + ph * 0.045f + 0.18f * q2);
                float b = FastTrig.Sin(y * 0.040f - ph * 0.060f + 0.20f * q1);
                float c = FastTrig.Sin(x * 0.006f + y * 0.020f + ph * 0.030f + 0.22f * sdr);
                float v = (a + b + c) * (1f / 3f) * 0.5f + 0.5f; // 0..1
                float idx = v * 0.6f + ph * 0.02f;
                float f = idx - MathF.Floor(idx);
                var col = colors[(int)(f * 255.99f)];
                // Contour lines at palette band edges give it the classic demoscene look.
                float edge = 1f - Kit.Smooth(MathF.Abs(MathF.Abs(f - 0.5f) * 2f - 1f) * 14f);
                float bright = 0.78f + 0.22f * FastTrig.Sin(v * 2f + ph * 0.07f);
                float e = edge * 55f;
                frame.SetPixel(x, y, new Pixel(Kit.ToByte(col.R * bright + e), Kit.ToByte(col.G * bright + e), Kit.ToByte(col.B * bright + e)));
            }
        }
    }

    private void RenderTunnel(FrameBuffer frame, float ph)
    {
        int arms = Math.Clamp(_owner.Arms, 1, 8) * 2;
        var colors = _ramp.Colors;
        float depth = ph * 1.4f;
        float rot = ph * 0.12f;
        for (int y = 0; y < _h; y++)
        {
            int row = y * _w;
            for (int x = 0; x < _w; x++)
            {
                int i = row + x;
                float r = _rad[i];
                float v = _lrA[i] * 5f - depth;
                float checker = FastTrig.Sin(_angA[i] * arms + rot) * FastTrig.Sin(v * 0.5f);   // -1..1 rings x spokes
                float s = Kit.Smooth(checker * 4f + 0.5f);
                float hue = v * 0.06f + ph * 0.03f;
                var col = colors[(int)((hue - MathF.Floor(hue)) * 255.99f)];
                float fog = Kit.Smooth((r - 1f) / 12f);          // fade into the vanishing point
                float k = (0.12f + 0.88f * s) * fog;
                float c = _core[i] * _pulse;
                float add = c * c * 200f;
                frame.SetPixel(x, y, new Pixel(Kit.ToByte(col.R * k + add), Kit.ToByte(col.G * k + add), Kit.ToByte(col.B * k + add)));
            }
        }
    }

    private void DrawComets(FrameBuffer frame)
    {
        var colors = _ramp.Colors;
        for (int i = 0; i < CometCount; i++)
        {
            ref var c = ref _comets[i];
            var tint = colors[(int)(((c.Hue + _phase * 0.05f) - MathF.Floor(c.Hue + _phase * 0.05f)) * 255.99f)];
            for (int k = 0; k < TrailLength; k++)
            {
                int idx = i * TrailLength + ((c.Head - k + TrailLength) % TrailLength);
                float f = 1f - k / (float)TrailLength;
                float energy = f * f * 1.1f;
                float x = _trailX[idx], y = _trailY[idx];
                int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
                float wx = x - ix, wy = y - iy;
                // Head is white-hot, the tail takes the comet's colour.
                float white = k < 3 ? 0.6f : 0f;
                float r = Kit.Lerp(tint.R, 255f, white), g = Kit.Lerp(tint.G, 255f, white), b = Kit.Lerp(tint.B, 255f, white);
                Kit.Add(frame, ix, iy, r * energy * (1 - wx) * (1 - wy), g * energy * (1 - wx) * (1 - wy), b * energy * (1 - wx) * (1 - wy));
                Kit.Add(frame, ix + 1, iy, r * energy * wx * (1 - wy), g * energy * wx * (1 - wy), b * energy * wx * (1 - wy));
                Kit.Add(frame, ix, iy + 1, r * energy * (1 - wx) * wy, g * energy * (1 - wx) * wy, b * energy * (1 - wx) * wy);
                Kit.Add(frame, ix + 1, iy + 1, r * energy * wx * wy, g * energy * wx * wy, b * energy * wx * wy);
            }
        }
    }
}
