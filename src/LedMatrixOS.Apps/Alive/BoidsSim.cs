using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Alive;

/// <summary>
/// Flocking: separation, alignment and cohesion on a torus, plus an optional predator the flock flees from.
/// Positions are stamped into a fading luminance field, so each bird leaves a coloured trail (colour = heading).
/// </summary>
internal sealed class BoidsSim : IAliveSim
{
    private const int MaxBoids = 360;
    private const float Perception = 14f, SeparationRadius = 4.5f, PredatorRange = 30f;
    private const float MaxSpeed = 34f, MinSpeed = 15f, PredatorSpeed = 40f;

    private readonly float[] _x = new float[MaxBoids], _y = new float[MaxBoids];
    private readonly float[] _vx = new float[MaxBoids], _vy = new float[MaxBoids];
    private readonly float[] _ax = new float[MaxBoids], _ay = new float[MaxBoids];
    private float[] _lum = [];
    private byte[] _hue = [];
    private int _w, _h, _n;
    private float _px, _py, _pvx, _pvy;
    private bool _predatorActive;

    public string Name => "Boids";
    public int Ticks { get; private set; }
    public bool Stagnant => _n == 0;
    public int Count => _n;

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        _lum = new float[width * height];
        _hue = new byte[width * height];
        _n = 0;
    }

    private static int Target(AliveContext c) => Math.Min(MaxBoids, 30 + 30 * c.Population);

    public void Seed(AliveContext c)
    {
        var rng = c.Rng;
        Array.Clear(_lum);
        Ticks = 0;
        _n = Target(c);
        for (int i = 0; i < _n; i++) Spawn(i, rng);
        _px = rng.NextSingle() * _w; _py = rng.NextSingle() * _h;
        float a = rng.NextSingle() * Kit.TwoPi;
        _pvx = MathF.Cos(a) * PredatorSpeed; _pvy = MathF.Sin(a) * PredatorSpeed;
    }

    private void Spawn(int i, Random rng)
    {
        _x[i] = rng.NextSingle() * _w; _y[i] = rng.NextSingle() * _h;
        float a = rng.NextSingle() * Kit.TwoPi;
        float s = MinSpeed + rng.NextSingle() * (MaxSpeed - MinSpeed);
        _vx[i] = MathF.Cos(a) * s; _vy[i] = MathF.Sin(a) * s;
    }

    public void Wipe() { _n = 0; Array.Clear(_lum); }

    public void Step(float dt, AliveContext c)
    {
        if (_n == 0) return;
        Ticks++;
        int target = Target(c);
        while (_n < target) Spawn(_n++, c.Rng);
        if (_n > target) _n = target;

        float sdt = dt * (0.3f + 0.14f * c.Speed);
        float w = _w, h = _h, hw = w * 0.5f, hh = h * 0.5f;
        float per2 = Perception * Perception, sep2 = SeparationRadius * SeparationRadius, pred2 = PredatorRange * PredatorRange;
        _predatorActive = c.Predator;

        for (int i = 0; i < _n; i++)
        {
            float xi = _x[i], yi = _y[i], vxi = _vx[i], vyi = _vy[i];
            float cx = 0, cy = 0, avx = 0, avy = 0, sx = 0, sy = 0;
            int cnt = 0;
            for (int j = 0; j < _n; j++)
            {
                if (j == i) continue;
                float dx = AliveMath.Delta(_x[j] - xi, w, hw), dy = AliveMath.Delta(_y[j] - yi, h, hh);
                float d2 = dx * dx + dy * dy;
                if (d2 >= per2) continue;
                cnt++;
                cx += dx; cy += dy; avx += _vx[j]; avy += _vy[j];
                if (d2 < sep2)
                {
                    float inv = 1f / (d2 + 0.5f);
                    sx -= dx * inv; sy -= dy * inv;
                }
            }
            float ax = sx * 60f, ay = sy * 60f;
            if (cnt > 0)
            {
                float k = 1f / cnt;
                ax += (avx * k - vxi) * 3f + cx * k * 1.6f;
                ay += (avy * k - vyi) * 3f + cy * k * 1.6f;
            }
            if (_predatorActive)
            {
                float dx = AliveMath.Delta(_px - xi, w, hw), dy = AliveMath.Delta(_py - yi, h, hh);
                float d2 = dx * dx + dy * dy;
                if (d2 < pred2 && d2 > 0.01f)
                {
                    float d = MathF.Sqrt(d2);
                    float f = (1f - d / PredatorRange) * 220f / d;
                    ax -= dx * f; ay -= dy * f;
                }
            }
            _ax[i] = ax; _ay[i] = ay;
        }

        float nearest = float.MaxValue, tx = _px, ty = _py;
        for (int i = 0; i < _n; i++)
        {
            float vx = _vx[i] + _ax[i] * sdt, vy = _vy[i] + _ay[i] * sdt;
            float sp2 = vx * vx + vy * vy;
            if (sp2 > MaxSpeed * MaxSpeed) { float k = MaxSpeed / MathF.Sqrt(sp2); vx *= k; vy *= k; }
            else if (sp2 < MinSpeed * MinSpeed)
            {
                if (sp2 < 1e-4f) { vx = MinSpeed; vy = 0; }
                else { float k = MinSpeed / MathF.Sqrt(sp2); vx *= k; vy *= k; }
            }
            _vx[i] = vx; _vy[i] = vy;
            float nx = AliveMath.Wrap(_x[i] + vx * sdt, w), ny = AliveMath.Wrap(_y[i] + vy * sdt, h);
            // Stamp the midpoint too so fast birds still leave an unbroken trail.
            Stamp(AliveMath.Wrap(_x[i] + AliveMath.Delta(nx - _x[i], w, hw) * 0.5f, w),
                  AliveMath.Wrap(_y[i] + AliveMath.Delta(ny - _y[i], h, hh) * 0.5f, h), vx, vy);
            _x[i] = nx; _y[i] = ny;
            Stamp(nx, ny, vx, vy);
            if (_predatorActive)
            {
                float dx = AliveMath.Delta(nx - _px, w, hw), dy = AliveMath.Delta(ny - _py, h, hh);
                float d2 = dx * dx + dy * dy;
                if (d2 < nearest) { nearest = d2; tx = _px + dx; ty = _py + dy; }
            }
        }

        if (_predatorActive)
        {
            float dx = tx - _px, dy = ty - _py;
            float d = MathF.Sqrt(dx * dx + dy * dy) + 0.001f;
            _pvx += (dx / d * PredatorSpeed - _pvx) * 1.4f * sdt;
            _pvy += (dy / d * PredatorSpeed - _pvy) * 1.4f * sdt;
            _px = AliveMath.Wrap(_px + _pvx * sdt, w);
            _py = AliveMath.Wrap(_py + _pvy * sdt, h);
        }

        // Fade the field once per frame (frame-rate independent enough: dt is clamped upstream).
        float decay = 0.78f + 0.02f * c.Trail;
        decay = MathF.Pow(decay, dt * 30f);
        var lum = _lum;
        for (int i = 0; i < lum.Length; i++) lum[i] *= decay;
    }

    private void Stamp(float x, float y, float vx, float vy)
    {
        int ix = (int)x, iy = (int)y;
        if ((uint)ix >= (uint)_w || (uint)iy >= (uint)_h) return;
        int idx = iy * _w + ix;
        _lum[idx] = 1f;
        _hue[idx] = (byte)((MathF.Atan2(vy, vx) / Kit.TwoPi + 0.5f) * 255f);
    }

    public void Draw(FrameBuffer frame, Rectangle bounds, AliveContext c)
    {
        var cycle = c.Palette.Cycle.Colors;
        int w = _w;
        for (int y = 0; y < _h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float l = _lum[row + x];
                if (l < 0.04f) continue;
                frame.SetPixel(bounds.X + x, bounds.Y + y, Kit.Scale(cycle[_hue[row + x]], MathF.Sqrt(l) * 0.9f * l + 0.1f * l));
            }
        }
        for (int i = 0; i < _n; i++)
        {
            int ix = (int)_x[i], iy = (int)_y[i];
            if ((uint)ix >= (uint)w || (uint)iy >= (uint)_h) continue;
            frame.SetPixel(bounds.X + ix, bounds.Y + iy, Pixel.Lerp(cycle[_hue[iy * w + ix]], Pixel.White, 0.55f));
        }
        if (_predatorActive && _n > 0)
        {
            int px = (int)_px, py = (int)_py;
            var edge = new Pixel(255, 70, 50);
            Put(frame, bounds, px - 1, py, edge); Put(frame, bounds, px + 1, py, edge);
            Put(frame, bounds, px, py - 1, edge); Put(frame, bounds, px, py + 1, edge);
            Put(frame, bounds, px, py, Pixel.White);
        }
    }

    private void Put(FrameBuffer frame, Rectangle bounds, int x, int y, Pixel p)
    {
        if ((uint)x < (uint)_w && (uint)y < (uint)_h) frame.SetPixel(bounds.X + x, bounds.Y + y, p);
    }
}
