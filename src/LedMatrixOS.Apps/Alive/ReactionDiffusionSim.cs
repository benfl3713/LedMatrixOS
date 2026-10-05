using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Alive;

/// <summary>
/// Gray-Scott reaction-diffusion on a half-resolution torus (bilinearly upscaled 2x) with four presets.
/// Stale or runaway patterns (frozen, uniform or extinct) trigger a reseed.
/// </summary>
internal sealed class ReactionDiffusionSim : IAliveSim
{
    private static readonly (string Name, float F, float K)[] Presets =
    [
        ("Coral", 0.0545f, 0.062f),
        ("Spots", 0.030f, 0.062f),
        ("Worms", 0.078f, 0.061f),
        ("Mitosis", 0.0367f, 0.0649f),
    ];

    private const float Du = 1.0f, Dv = 0.5f;
    private const int CheckEvery = 150, WarmupSteps = 140, MaxStepsPerFrame = 20;

    private readonly FieldUpscaler _up = new();
    private float[] _u = [], _v = [], _u2 = [], _v2 = [];
    private int _gw, _gh, _preset, _lastPreset = -1;
    private float _f = 0.0545f, _k = 0.062f, _acc;
    private double _lastCheck = -1;
    private int _frozenChecks;
    private bool _stagnant;

    public string Name => "Reaction-Diffusion";
    public int Ticks { get; private set; }
    public bool Stagnant => _stagnant;
    public string Preset => Presets[_preset].Name;

    public void Resize(int width, int height)
    {
        _gw = Math.Max(8, width / 2); _gh = Math.Max(8, height / 2);
        int n = _gw * _gh;
        _u = new float[n]; _v = new float[n]; _u2 = new float[n]; _v2 = new float[n];
        _up.Resize(width, height, _gw, _gh);
    }

    public void Wipe() { Array.Clear(_v); Array.Fill(_u, 1f); }

    public void Seed(AliveContext c)
    {
        var rng = c.Rng;
        int pick = Array.FindIndex(Presets, p => p.Name == c.Variant);   // runs once per seed, not per frame
        if (pick < 0)
        {
            pick = rng.Next(Presets.Length);
            if (pick == _lastPreset) pick = (pick + 1) % Presets.Length;
        }
        _preset = _lastPreset = pick;
        _f = Presets[pick].F; _k = Presets[pick].K;
        Ticks = 0; _acc = 0; _frozenChecks = 0; _stagnant = false; _lastCheck = 0; _prevSig = 0;

        Array.Fill(_u, 1f);
        Array.Clear(_v);
        int blobs = 3 + c.Population;
        for (int b = 0; b < blobs; b++)
        {
            int size = 3 + rng.Next(4);
            int cx = rng.Next(_gw), cy = rng.Next(_gh);
            for (int dy = -size; dy <= size; dy++)
                for (int dx = -size; dx <= size; dx++)
                {
                    if (dx * dx + dy * dy > size * size) continue;
                    int i = ((cy + dy + _gh) % _gh) * _gw + (cx + dx + _gw) % _gw;
                    _u[i] = 0.5f; _v[i] = 0.25f + 0.05f * rng.NextSingle();
                }
        }
        for (int i = 0; i < WarmupSteps; i++) Advance();
    }

    public void Step(float dt, AliveContext c)
    {
        _acc += dt * c.Speed * 45f;
        int steps = 0;
        while (_acc >= 1f && steps < MaxStepsPerFrame) { _acc -= 1f; Advance(); steps++; }
        if (_acc > 1f) _acc = 1f;
        if (Ticks - _lastCheck >= CheckEvery) Check();
    }

    private double _prevSig;

    private void Check()
    {
        _lastCheck = Ticks;
        double sum = 0, sig = 0;
        int lit = 0;
        for (int i = 0; i < _v.Length; i++)
        {
            float v = _v[i];
            sum += v;
            sig += v * ((i % 97) + 1);
            if (v > 0.12f) lit++;
        }
        double fill = lit / (double)_v.Length;
        if (sum < 4 || fill > 0.92) { _stagnant = true; return; }
        double rel = _prevSig > 0 ? Math.Abs(sig - _prevSig) / _prevSig : 1;
        _prevSig = sig;
        _frozenChecks = rel < 1.5e-4 ? _frozenChecks + 1 : 0;
        if (_frozenChecks >= 3) _stagnant = true;
    }

    private void Advance()
    {
        Ticks++;
        int gw = _gw, gh = _gh;
        float f = _f, fk = _f + _k;
        float[] u = _u, v = _v, u2 = _u2, v2 = _v2;
        for (int y = 0; y < gh; y++)
        {
            int ym = (y == 0 ? gh - 1 : y - 1) * gw, y0 = y * gw, yp = (y == gh - 1 ? 0 : y + 1) * gw;
            for (int x = 0; x < gw; x++)
            {
                int xm = x == 0 ? gw - 1 : x - 1, xp = x == gw - 1 ? 0 : x + 1;
                int i = y0 + x;
                float uc = u[i], vc = v[i];
                float lapU = 0.2f * (u[ym + x] + u[yp + x] + u[y0 + xm] + u[y0 + xp])
                           + 0.05f * (u[ym + xm] + u[ym + xp] + u[yp + xm] + u[yp + xp]) - uc;
                float lapV = 0.2f * (v[ym + x] + v[yp + x] + v[y0 + xm] + v[y0 + xp])
                           + 0.05f * (v[ym + xm] + v[ym + xp] + v[yp + xm] + v[yp + xp]) - vc;
                float uvv = uc * vc * vc;
                float nu = uc + Du * lapU - uvv + f * (1f - uc);
                float nv = vc + Dv * lapV + uvv - fk * vc;
                u2[i] = nu < 0f ? 0f : nu > 1f ? 1f : nu;
                v2[i] = nv < 0f ? 0f : nv > 1f ? 1f : nv;
            }
        }
        (_u, _u2) = (_u2, _u);
        (_v, _v2) = (_v2, _v);
    }

    public void Draw(FrameBuffer frame, Rectangle bounds, AliveContext c) => _up.Draw(frame, bounds, _v, c.Palette.Ramp, 3.2f);
}
