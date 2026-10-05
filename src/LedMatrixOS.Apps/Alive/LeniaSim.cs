using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Alive;

/// <summary>
/// A small continuous cellular automaton in the spirit of Lenia: a ring-shaped smooth kernel convolved over the field,
/// a gaussian growth function, and creature-like soft blobs as seeds. Runs on a half-resolution torus (padded for the
/// convolution) and is upscaled with a glow that lingers behind moving creatures.
/// </summary>
internal sealed class LeniaSim : IAliveSim
{
    private const int R = 7;
    private const float Mu = 0.15f, Sigma = 0.016f, Dt = 0.1f;
    private const int GrowthSteps = 1024, CheckEvery = 90, MaxStepsPerFrame = 3;

    private readonly FieldUpscaler _up = new();
    private readonly float[] _growth = new float[GrowthSteps + 1];
    private readonly int[] _rowDy = new int[2 * R + 1], _rowDx0 = new int[2 * R + 1], _rowLen = new int[2 * R + 1], _rowOff = new int[2 * R + 1];
    private float[] _kw = [];
    private int _rows;
    private float[] _a = [], _next = [], _disp = [];     // _a is padded
    private int _gw, _gh, _pw;
    private float _acc;
    private double _prevSig, _lastCheck;
    private int _frozenChecks;
    private bool _stagnant;

    public string Name => "Lenia";
    public int Ticks { get; private set; }
    public bool Stagnant => _stagnant;
    public float Mass { get; private set; }

    public LeniaSim()
    {
        for (int i = 0; i <= GrowthSteps; i++)
        {
            float u = i / (float)GrowthSteps;
            _growth[i] = 2f * MathF.Exp(-((u - Mu) * (u - Mu)) / (2f * Sigma * Sigma)) - 1f;
        }
        BuildKernel();
    }

    private void BuildKernel()
    {
        var weights = new List<float>();
        float total = 0;
        var raw = new float[2 * R + 1, 2 * R + 1];
        for (int dy = -R; dy <= R; dy++)
            for (int dx = -R; dx <= R; dx++)
            {
                float r = MathF.Sqrt(dx * dx + dy * dy) / R;
                if (r <= 0f || r >= 1f) continue;
                float k = MathF.Exp(4f - 1f / (r * (1f - r)));
                raw[dy + R, dx + R] = k;
                total += k;
            }
        _rows = 0;
        for (int dy = -R; dy <= R; dy++)
        {
            int first = -1, last = -1;
            for (int dx = -R; dx <= R; dx++)
                if (raw[dy + R, dx + R] > 0f) { if (first < 0) first = dx; last = dx; }
            if (first < 0) continue;
            _rowDy[_rows] = dy; _rowDx0[_rows] = first; _rowLen[_rows] = last - first + 1; _rowOff[_rows] = weights.Count;
            for (int dx = first; dx <= last; dx++) weights.Add(raw[dy + R, dx + R] / total);
            _rows++;
        }
        _kw = weights.ToArray();
    }

    public void Resize(int width, int height)
    {
        _gw = Math.Max(16, width / 2); _gh = Math.Max(16, height / 2);
        _pw = _gw + 2 * R;
        _a = new float[_pw * (_gh + 2 * R)];
        _next = new float[_gw * _gh];
        _disp = new float[_gw * _gh];
        _up.Resize(width, height, _gw, _gh);
    }

    public void Wipe() { Array.Clear(_a); Array.Clear(_disp); }

    public void Seed(AliveContext c)
    {
        var rng = c.Rng;
        Array.Clear(_a); Array.Clear(_disp);
        Ticks = 0; _acc = 0; _stagnant = false; _frozenChecks = 0; _prevSig = 0; _lastCheck = 0;
        int blobs = 1 + c.Population / 3;
        for (int b = 0; b < blobs; b++)
        {
            float cx = rng.NextSingle() * _gw, cy = rng.NextSingle() * _gh;
            float radius = R * (0.9f + 0.5f * rng.NextSingle());
            int span = (int)radius + 2;
            for (int dy = -span; dy <= span; dy++)
                for (int dx = -span; dx <= span; dx++)
                {
                    float d2 = (dx * dx + dy * dy) / (radius * radius);
                    if (d2 >= 1f) continue;
                    int gx = ((int)cx + dx + _gw) % _gw, gy = ((int)cy + dy + _gh) % _gh;
                    float v = (1f - d2) * (0.35f + 0.65f * rng.NextSingle());
                    _a[(gy + R) * _pw + gx + R] = Math.Max(_a[(gy + R) * _pw + gx + R], v);
                }
        }
        Wrap();
    }

    private void Wrap()
    {
        int gw = _gw, gh = _gh, pw = _pw;
        for (int y = 0; y < gh; y++)
        {
            int row = (y + R) * pw;
            for (int k = 0; k < R; k++)
            {
                _a[row + k] = _a[row + gw + k];
                _a[row + R + gw + k] = _a[row + R + k];
            }
        }
        for (int k = 0; k < R; k++)
        {
            Array.Copy(_a, (R + gh - R + k) * pw, _a, k * pw, pw);       // top halo <- last rows
            Array.Copy(_a, (R + k) * pw, _a, (R + gh + k) * pw, pw);      // bottom halo <- first rows
        }
    }

    public void Step(float dt, AliveContext c)
    {
        _acc += dt * c.Speed * 6f;
        int steps = 0;
        while (_acc >= 1f && steps < MaxStepsPerFrame) { _acc -= 1f; Advance(); steps++; }
        if (_acc > 1f) _acc = 1f;

        // Afterglow for the picture.
        float decay = 0.35f + 0.058f * c.Trail;
        int gw = _gw, pw = _pw;
        for (int y = 0; y < _gh; y++)
        {
            int src = (y + R) * pw + R, dst = y * gw;
            for (int x = 0; x < gw; x++)
            {
                float a = _a[src + x], d = _disp[dst + x] * decay;
                _disp[dst + x] = a > d ? a : d;
            }
        }
        if (Ticks - _lastCheck >= CheckEvery) Check();
    }

    private void Check()
    {
        _lastCheck = Ticks;
        double sum = 0, sig = 0;
        int gw = _gw, pw = _pw;
        for (int y = 0; y < _gh; y++)
        {
            int src = (y + R) * pw + R;
            for (int x = 0; x < gw; x++)
            {
                float a = _a[src + x];
                sum += a;
                sig += a * ((y * gw + x) % 89 + 1);
            }
        }
        Mass = (float)(sum / (gw * _gh));
        if (Mass < 0.004f || Mass > 0.45f) { _stagnant = true; return; }
        double rel = _prevSig > 0 ? Math.Abs(sig - _prevSig) / _prevSig : 1;
        _prevSig = sig;
        _frozenChecks = rel < 2e-4 ? _frozenChecks + 1 : 0;
        if (_frozenChecks >= 3) _stagnant = true;
    }

    private void Advance()
    {
        Ticks++;
        int gw = _gw, gh = _gh, pw = _pw;
        float[] a = _a, kw = _kw, growth = _growth, next = _next;
        for (int y = 0; y < gh; y++)
        {
            for (int x = 0; x < gw; x++)
            {
                float sum = 0f;
                for (int r = 0; r < _rows; r++)
                {
                    int pos = (y + R + _rowDy[r]) * pw + x + R + _rowDx0[r];
                    int off = _rowOff[r], len = _rowLen[r];
                    for (int k = 0; k < len; k++) sum += kw[off + k] * a[pos + k];
                }
                int gi = (int)(sum * GrowthSteps);
                if (gi > GrowthSteps) gi = GrowthSteps;
                float v = a[(y + R) * pw + x + R] + Dt * growth[gi < 0 ? 0 : gi];
                next[y * gw + x] = v < 0f ? 0f : v > 1f ? 1f : v;
            }
        }
        for (int y = 0; y < gh; y++) Array.Copy(next, y * gw, a, (y + R) * pw + R, gw);
        Wrap();
    }

    public void Draw(FrameBuffer frame, Rectangle bounds, AliveContext c) => _up.Draw(frame, bounds, _disp, c.Palette.Ramp, 1.15f);
}
