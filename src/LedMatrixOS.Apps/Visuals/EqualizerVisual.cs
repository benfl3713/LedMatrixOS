using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Effects;
using LedMatrixOS.Graphics.Particles;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Visuals;

/// <summary>
/// The equalizer engine. Per frame: pick a source (live bands, synthetic demo groove or idle breathing), turn it into per-bar targets, smooth
/// with fast attack / slow release, run peak-hold caps with gravity, detect beats from the bass bars, then draw in the chosen style with
/// per-bar vertical gradients, a reflection, beat particles and a bloom whose strength follows the beat. No allocation in steady state,
/// live audio included (bands are copied into a cached buffer with <see cref="AudioDataService.CopyFrequencyBands"/>).
/// </summary>
internal sealed class EqualizerVisual : VisualNode
{
    public enum SourceKind { Live, Demo, Idle }

    private const int Rows = 64;

    private readonly EqualizerApp _app;
    private readonly Random _rng;
    private readonly GlowEffect _glow = new() { Threshold = 110f, Radius = 2, Strength = 0.6f };
    private readonly Emitter _sparks;
    private readonly Pixel[] _palette = new Pixel[8];
    private readonly float[] _bands = new float[AudioDataService.FrequencyBandCount];

    private ParticleSystem? _particles;
    private int _w, _h, _n;
    private float[] _target = [], _value = [], _peak = [], _peakVel = [], _peakHold = [];
    private Pixel[] _grad = [];
    private string? _gradKey;
    private float _bassAvg, _prevBass, _sinceBeat = 10f, _pulse;
    private float _demoGain = 1f;
    private FrameContext _ctx;

    public SourceKind Source { get; private set; } = SourceKind.Idle;

    public EqualizerVisual(EqualizerApp app, int seed)
    {
        _app = app;
        _rng = new Random(seed);
        _sparks = new Emitter
        {
            Rate = 0f,
            LifetimeMin = 0.6f, LifetimeMax = 1.3f,
            SpeedMin = 20f, SpeedMax = 75f,
            Angle = -90f, Spread = 80f,
            GravityY = 85f,
            AlphaStart = 1f, AlphaEnd = 0f,
            Palette = _palette,
        };
    }

    protected override void Step(FrameContext ctx)
    {
        _ctx = ctx;
        var host = Host!;
        int n = Math.Clamp(_app.BarCount, 8, 64);
        if (_w != host.Width || _h != host.Height || _n != n) Resize(host.Width, host.Height, n);
        if (_gradKey != _app.ColorMode) BuildGradients(_app.ColorMode);

        FillTargets();
        Smooth();
        DetectBeat();
        _pulse *= MathF.Exp(-Dt * 5.5f);
        _sinceBeat += Dt;
        _particles!.Update(Dt);
    }

    private void Resize(int w, int h, int n)
    {
        _w = w; _h = h; _n = n;
        _target = new float[n]; _value = new float[n]; _peak = new float[n]; _peakVel = new float[n]; _peakHold = new float[n];
        _grad = new Pixel[n * Rows];
        _gradKey = null;
        _particles = new ParticleSystem(w, h, 480, new Random(_rng.Next()));
        _particles.Add(_sparks);
    }

    // ---------------------------------------------------------------- sources

    private void FillTargets()
    {
        var service = _app.AudioService;
        bool live = _app.AudioSource == "Microphone" && service is not null && service.HasRecentData();
        if (live)
        {
            Source = SourceKind.Live;
            int count = service!.CopyFrequencyBands(_bands);
            float gain = 0.4f * _app.Sensitivity;
            for (int i = 0; i < _n; i++)
            {
                int a = i * count / _n;
                int b = Math.Max(a + 1, (i + 1) * count / _n);
                float m = 0f;
                for (int k = a; k < b && k < count; k++) m = Math.Max(m, _bands[k]);
                float f = i / (float)Math.Max(1, _n - 1);
                _target[i] = Math.Clamp(m * gain * (1f + 0.6f * f), 0f, 1f);
            }
        }
        else if (_app.AutoGenerate)
        {
            Source = SourceKind.Demo;
            FillDemo();
        }
        else
        {
            Source = SourceKind.Idle;
            FillIdle();
        }
    }

    /// <summary>A looping 124 BPM groove: kick on the beat, snare on 2 and 4, hats on the off-beats, a wandering mid line and a bass wobble.</summary>
    private void FillDemo()
    {
        float beat = Time * (124f / 60f);
        float frac = beat - MathF.Floor(beat);
        int bar = (int)MathF.Floor(beat) & 3;
        float kick = MathF.Exp(-frac * 5.5f);
        float snare = (bar == 1 || bar == 3) ? MathF.Exp(-frac * 7f) : 0f;
        float eighth = beat * 2f; eighth -= MathF.Floor(eighth);
        float hat = MathF.Exp(-eighth * 9f) * ((int)MathF.Floor(beat * 2f) % 2 == 1 ? 1f : 0.5f);
        int slice = (int)(Time * 24f);
        for (int i = 0; i < _n; i++)
        {
            float f = i / (float)Math.Max(1, _n - 1);
            float bassLobe = kick * Gauss(f, 0.07f, 0.11f);
            float subWobble = 0.18f * Gauss(f, 0.05f, 0.1f) * (0.5f + 0.5f * FastTrig.Sin(Time * 0.5f));
            float snareLobe = snare * Gauss(f, 0.42f, 0.16f) * 0.85f;
            float lead = 0.32f * Gauss(f, 0.30f + 0.18f * FastTrig.Sin(Time * 0.11f), 0.09f) * (0.6f + 0.4f * FastTrig.Sin(Time * 0.9f));
            float hatLobe = hat * Gauss(f, 0.85f, 0.2f) * 0.7f;
            float body = 0.16f * (1f - f * 0.7f);
            float jitter = 0.78f + 0.22f * Kit.Hash(i, slice, 5);
            float v = (bassLobe * 1.05f + subWobble + snareLobe + lead + hatLobe + body) * jitter;
            _target[i] = Math.Clamp(v, 0f, 1f);
        }
    }

    private void FillIdle()
    {
        float breath = 0.5f + 0.5f * FastTrig.Sin(Time * 0.16f);
        for (int i = 0; i < _n; i++)
        {
            float f = i / (float)Math.Max(1, _n - 1);
            float travel = FastTrig.Sin(Time * 0.22f - f * 1.4f) * 0.5f + 0.5f;
            float shimmer = FastTrig.Sin(Time * 0.5f + f * 7f) * 0.5f + 0.5f;
            _target[i] = 0.05f + (0.10f + 0.10f * breath) * travel + 0.03f * shimmer;
        }
    }

    private static float Gauss(float x, float mu, float sigma)
    {
        float d = (x - mu) / sigma;
        return MathF.Exp(-d * d);
    }

    // ---------------------------------------------------------------- dynamics

    private void Smooth()
    {
        float s = _app.Smoothness;
        float attack = 1f - MathF.Exp(-Dt / (0.010f + 0.005f * s));
        float release = 1f - MathF.Exp(-Dt / (0.06f + 0.03f * s));
        for (int i = 0; i < _n; i++)
        {
            float t = _target[i], v = _value[i];
            v += (t - v) * (t > v ? attack : release);
            _value[i] = v;

            if (v >= _peak[i])
            {
                _peak[i] = v;
                _peakHold[i] = 0.45f;
                _peakVel[i] = 0f;
            }
            else if (_peakHold[i] > 0f)
            {
                _peakHold[i] -= Dt;
            }
            else
            {
                _peakVel[i] += 2.2f * Dt;               // gravity
                _peak[i] = Math.Max(v, _peak[i] - _peakVel[i] * Dt);
            }
        }
    }

    private void DetectBeat()
    {
        int nb = Math.Max(2, _n / 7);
        float bass = 0f;
        for (int i = 0; i < nb; i++) bass += _target[i];
        bass /= nb;
        _bassAvg += (bass - _bassAvg) * Math.Min(1f, Dt * 1.6f);
        bool rising = bass > _prevBass + 0.02f;
        _prevBass = bass;
        if (bass > _bassAvg * 1.3f + 0.08f && rising && _sinceBeat > 0.2f && Source != SourceKind.Idle)
        {
            _sinceBeat = 0f;
            _pulse = 1f;
            EmitBeatParticles();
        }
    }

    private void EmitBeatParticles()
    {
        if (_app.Style == "Radial")
        {
            _sparks.X = _w / 2f; _sparks.Y = _h / 2f; _sparks.Width = 0; _sparks.Height = 0;
            _sparks.Angle = 0f; _sparks.Spread = 360f; _sparks.GravityY = 0f;
            _sparks.SpeedMin = 30f; _sparks.SpeedMax = 120f; // faster so the burst reaches across the wide ring
            _particles!.Burst(_sparks, 14);
            _sparks.SpeedMin = 20f; _sparks.SpeedMax = 75f;
            return;
        }

        _sparks.Angle = -90f; _sparks.Spread = 80f; _sparks.GravityY = 85f;
        float pitch = _w / (float)_n;
        float top = BaselineY();
        // Fire sparks off the tallest few bars.
        for (int shot = 0; shot < 3; shot++)
        {
            int best = 0; float bv = -1f;
            for (int i = 0; i < _n; i++)
            {
                float v = _value[i] * (1f + 0.15f * Kit.Hash(i, shot, (int)(Time * 10f)));
                if (v > bv) { bv = v; best = i; }
            }
            _sparks.X = best * pitch + pitch * 0.5f;
            _sparks.Y = top - _value[best] * MaxHeight();
            _sparks.Width = 0; _sparks.Height = 0;
            _particles!.Burst(_sparks, 3 + (int)(bv * 4f));
            _value[best] *= 0.97f; // so the next shot picks another bar
        }
    }

    private float BaselineY() => _app.Style switch
    {
        "Bars" => _h - 1 - ReflectionRows,
        "Dots" => _h - 1,
        _ => _h / 2f,
    };

    private const int ReflectionRows = 7;

    private float MaxHeight() => _app.Style switch
    {
        "Bars" => _h - ReflectionRows - 1f,
        "Dots" => _h - 1f,
        _ => _h / 2f - 1f,
    };

    // ---------------------------------------------------------------- colour

    private void BuildGradients(string key)
    {
        _gradKey = key;
        ReadOnlySpan<(float, Pixel)> stops = _app.ColorMode switch
        {
            "Blue" => [(0f, new Pixel(15, 30, 190)), (0.55f, new Pixel(0, 140, 255)), (0.85f, new Pixel(70, 230, 255)), (1f, new Pixel(235, 252, 255))],
            "Green" => [(0f, new Pixel(0, 110, 30)), (0.5f, new Pixel(30, 235, 70)), (0.85f, new Pixel(150, 255, 120)), (1f, new Pixel(235, 255, 225))],
            "Red" => [(0f, new Pixel(140, 0, 25)), (0.5f, new Pixel(255, 40, 20)), (0.85f, new Pixel(255, 140, 70)), (1f, new Pixel(255, 235, 200))],
            "Cyan" => [(0f, new Pixel(0, 80, 150)), (0.5f, new Pixel(0, 215, 255)), (0.85f, new Pixel(120, 255, 255)), (1f, new Pixel(240, 255, 255))],
            "Heat" => [(0f, new Pixel(110, 0, 20)), (0.35f, new Pixel(255, 40, 0)), (0.65f, new Pixel(255, 170, 0)), (0.9f, new Pixel(255, 245, 90)), (1f, new Pixel(255, 255, 230))],
            "White" => [(0f, new Pixel(90, 95, 110)), (0.6f, new Pixel(215, 220, 235)), (1f, new Pixel(255, 255, 255))],
            _ => default,
        };

        var ramp = new ColorRamp();
        bool rainbow = stops.IsEmpty;
        if (!rainbow) ramp.Fill(stops);

        for (int i = 0; i < _n; i++)
        {
            float f = i / (float)Math.Max(1, _n - 1);
            for (int k = 0; k < Rows; k++)
            {
                float h = k / (float)(Rows - 1);
                Pixel c;
                if (rainbow)
                {
                    float sat = 1f - 0.5f * Kit.Smooth((h - 0.82f) / 0.18f);
                    c = Pixel.FromHsv(f * 290f, sat, 0.62f + 0.38f * Math.Min(1f, h * 1.6f));
                }
                else
                {
                    c = ramp.Sample(h);
                }
                _grad[i * Rows + k] = c;
            }
        }

        for (int p = 0; p < _palette.Length; p++)
        {
            int bar = (int)(p / (float)(_palette.Length - 1) * (_n - 1));
            _palette[p] = Pixel.Lerp(_grad[bar * Rows + Rows - 1], Pixel.White, 0.35f);
        }
    }

    private Pixel GradAt(int bar, int k) => _grad[bar * Rows + Math.Clamp(k, 0, Rows - 1)];

    // ---------------------------------------------------------------- drawing

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_w == 0 || frame.Width != _w || frame.Height != _h || _particles is null) return;

        switch (_app.Style)
        {
            case "Mirrored": DrawMirrored(frame); break;
            case "Wave": DrawWave(frame); break;
            case "Dots": DrawDots(frame); break;
            case "Radial": DrawRadial(frame); break;
            default: DrawBars(frame); break;
        }

        _particles.Render(frame);
        if (_app.Glow)
        {
            _glow.Strength = 0.5f + 0.75f * _pulse;
            _glow.Apply(frame, _ctx);
        }
    }

    private void BarGeometry(out int pitch, out int barW, out int xOff)
    {
        pitch = Math.Max(1, _w / _n);
        int gap = pitch >= 7 ? 2 : pitch >= 3 ? 1 : 0;
        barW = Math.Max(1, pitch - gap);
        xOff = (_w - pitch * _n + gap) / 2;
    }

    private void DrawBars(FrameBuffer frame)
    {
        BarGeometry(out int pitch, out int barW, out int xOff);
        int baseline = _h - 1 - ReflectionRows;
        float maxH = baseline + 1f;
        bool segments = barW >= 3;
        for (int i = 0; i < _n; i++)
        {
            int x0 = xOff + i * pitch;
            int hb = (int)(_value[i] * maxH + 0.5f);
            for (int k = 0; k < hb; k++)
            {
                var c = GradAt(i, (int)(k * (Rows - 1f) / maxH));
                if (segments && (k & 3) == 3) c = Kit.Scale(c, 0.55f);
                int y = baseline - k;
                for (int dx = 0; dx < barW; dx++) frame.SetPixel(x0 + dx, y, c);
            }

            // Reflection: a dim, fading mirror image under the baseline.
            for (int k = 1; k <= ReflectionRows; k++)
            {
                int srcK = hb - k;
                if (srcK < 0) break;
                var c = Kit.Scale(GradAt(i, (int)(srcK * (Rows - 1f) / maxH)), 0.30f * (1f - (k - 1f) / ReflectionRows));
                for (int dx = 0; dx < barW; dx++) frame.SetPixel(x0 + dx, baseline + k, c);
            }

            if (_app.PeakCaps)
            {
                int py = baseline - (int)(_peak[i] * maxH + 0.5f);
                if (_peak[i] > 0.03f && py < baseline - hb + 1)
                {
                    var cap = Pixel.Lerp(GradAt(i, Rows - 1), Pixel.White, 0.7f);
                    for (int dx = 0; dx < barW; dx++)
                    {
                        frame.SetPixel(x0 + dx, py, cap);
                        if (barW >= 3) frame.SetPixel(x0 + dx, py - 1, Kit.Scale(cap, 0.55f));
                    }
                }
            }
        }
    }

    private void DrawMirrored(FrameBuffer frame)
    {
        BarGeometry(out int pitch, out int barW, out int xOff);
        int mid = _h / 2;
        float maxH = _h / 2f - 1f;
        for (int i = 0; i < _n; i++)
        {
            int x0 = xOff + i * pitch;
            int hb = (int)(_value[i] * maxH + 0.5f);
            for (int k = 0; k < hb; k++)
            {
                var c = GradAt(i, (int)(k * (Rows - 1f) / maxH));
                var cb = Kit.Scale(c, 0.85f);
                for (int dx = 0; dx < barW; dx++)
                {
                    frame.SetPixel(x0 + dx, mid - 1 - k, c);
                    frame.SetPixel(x0 + dx, mid + k, cb);
                }
            }

            if (_app.PeakCaps && _peak[i] > 0.03f)
            {
                int pk = (int)(_peak[i] * maxH + 0.5f);
                var cap = Pixel.Lerp(GradAt(i, Rows - 1), Pixel.White, 0.7f);
                for (int dx = 0; dx < barW; dx++)
                {
                    frame.SetPixel(x0 + dx, mid - 1 - pk, cap);
                    frame.SetPixel(x0 + dx, mid + pk, cap);
                }
            }
        }

        // A thin bright spine that flares on the beat.
        var spine = Kit.Scale(GradAt(_n / 2, Rows - 1), 0.25f + 0.6f * _pulse);
        for (int x = 0; x < _w; x++) Kit.Add(frame, x, mid - 1, spine, 0.5f);
    }

    private void DrawWave(FrameBuffer frame)
    {
        float mid = _h / 2f;
        float maxH = _h / 2f - 2f;
        float pitch = _w / (float)_n;
        float prevTop = float.NaN;
        for (int x = 0; x < _w; x++)
        {
            float fx = (x + 0.5f) / pitch - 0.5f;
            int i0 = Math.Clamp((int)MathF.Floor(fx), 0, _n - 1), i1 = Math.Min(i0 + 1, _n - 1);
            float t = Kit.Smooth(fx - MathF.Floor(fx));
            float v = Kit.Lerp(_value[i0], _value[i1], t);
            float pk = Kit.Lerp(_peak[i0], _peak[i1], t);
            int bar = t < 0.5f ? i0 : i1;
            var line = GradAt(bar, Rows - 1 - (int)((1f - Math.Min(1f, v * 1.4f)) * 30f));
            float h = v * maxH;

            // Soft fill from the curve toward the centre line.
            int ih = (int)h;
            for (int k = 0; k <= ih; k++)
            {
                float a = 0.10f + 0.34f * (k / Math.Max(1f, h));
                var fill = Kit.Scale(line, a);
                Kit.Add(frame, x, (int)mid - 1 - k, fill, 1f);
                Kit.Add(frame, x, (int)mid + k, fill, 0.8f);
            }

            // Thick bright curve (joined to the previous column so steep edges stay connected).
            float top = mid - 1 - h;
            float from = float.IsNaN(prevTop) ? top : prevTop;
            int y0 = (int)MathF.Round(Math.Min(from, top)), y1 = (int)MathF.Round(Math.Max(from, top));
            for (int y = y0; y <= y1 + 1; y++)
            {
                Kit.Add(frame, x, y, line, 0.9f);
                Kit.Add(frame, x, (int)(2 * mid - 1 - y), line, 0.9f);
            }

            prevTop = top;
            if (_app.PeakCaps && pk > 0.05f && (x & 3) == 0)
            {
                int py = (int)(mid - 1 - pk * maxH);
                Kit.Add(frame, x, py - 1, 200f, 200f, 220f);
                Kit.Add(frame, x, (int)(2 * mid - 1 - (py - 1)), 160f, 160f, 190f);
            }
        }
    }

    private void DrawDots(FrameBuffer frame)
    {
        BarGeometry(out int pitch, out _, out int xOff);
        int dot = pitch >= 6 ? 3 : pitch >= 4 ? 2 : 1;
        int vstep = dot + 1;
        int rows = _h / vstep;
        for (int i = 0; i < _n; i++)
        {
            int x0 = xOff + i * pitch + (pitch - dot) / 2;
            int lit = (int)(_value[i] * rows + 0.5f);
            int peakRow = (int)(_peak[i] * rows + 0.5f);
            for (int r = 0; r < rows; r++)
            {
                int y0 = _h - 1 - r * vstep - (dot - 1);
                Pixel c;
                if (r < lit) c = GradAt(i, (int)(r * (Rows - 1f) / rows));
                else if (_app.PeakCaps && r == peakRow && _peak[i] > 0.03f) c = Pixel.Lerp(GradAt(i, Rows - 1), Pixel.White, 0.4f);
                else c = Kit.Scale(GradAt(i, 0), 0.07f);   // unlit LEDs stay faintly visible: the grid itself is part of the look
                for (int dy = 0; dy < dot; dy++)
                    for (int dx = 0; dx < dot; dx++)
                        frame.SetPixel(x0 + dx, y0 + dy, c);
            }
        }
    }

    /// <summary>
    /// A sunburst stretched over the whole panel: every bar is a ray from a small inner ellipse out towards the panel edge (the ellipse
    /// has the panel's own aspect, so at 256x64 it spans the full width instead of a flat disc in the middle). With Mirror the spectrum
    /// runs down the left and right halves symmetrically; without it the spectrum wraps once around the ring, bass at the top.
    /// </summary>
    private void DrawRadial(FrameBuffer frame)
    {
        float cx = (_w - 1) / 2f, cy = (_h - 1) / 2f;
        float ax = cx - 1f, ay = cy - 1f;     // semi-axes of the outer ellipse
        float s0 = 0.16f + 0.05f * _pulse;    // inner ellipse (fraction of the outer one)
        float maxLen = 1f - s0 - 0.04f;
        int rays = _n * 2;
        bool mirror = _app.Mirror;
        for (int ray = 0; ray < rays; ray++)
        {
            int i = mirror ? (ray < _n ? ray : rays - 1 - ray) : ray / 2;
            float turns = (ray + 0.5f) / rays - 0.25f;
            float ca = FastTrig.Cos(turns), sa = FastTrig.Sin(turns);
            float len = _value[i] * maxLen;
            float pxLen = len * MathF.Sqrt(ca * ca * ax * ax + sa * sa * ay * ay);
            int steps = (int)pxLen + 1;
            bool horizontal = MathF.Abs(ca * ax) > MathF.Abs(sa * ay);
            for (int st = 0; st <= steps; st++)
            {
                float t = st / (float)steps;
                float sc = s0 + len * t;
                var c = GradAt(i, (int)(t * (Rows - 1) * Math.Min(1f, _value[i] * 1.3f)));
                int px = (int)MathF.Round(cx + ca * ax * sc), py = (int)MathF.Round(cy + sa * ay * sc);
                frame.SetPixel(px, py, c);
                // Thicken the ray across its short direction so neighbouring rays leave no gaps.
                var side = Kit.Scale(c, 0.75f);
                if (horizontal) frame.SetPixel(px, py + 1, side);
                else frame.SetPixel(px + 1, py, side);
            }

            if (_app.PeakCaps && _peak[i] > 0.04f)
            {
                float sc = s0 + _peak[i] * maxLen + 0.02f;
                int px = (int)MathF.Round(cx + ca * ax * sc), py = (int)MathF.Round(cy + sa * ay * sc);
                var cap = Pixel.Lerp(GradAt(i, Rows - 1), Pixel.White, 0.7f);
                frame.SetPixel(px, py, cap);
                if (horizontal) frame.SetPixel(px, py + 1, cap); else frame.SetPixel(px + 1, py, cap);
            }
        }

        // Pulsing core ring.
        int ringSteps = 160;
        var core = Kit.Scale(GradAt(_n / 2, Rows / 2), 0.35f + 0.65f * _pulse);
        float rs = s0 - 0.04f;
        for (int s = 0; s < ringSteps; s++)
        {
            float a = s / (float)ringSteps;
            frame.SetPixel((int)MathF.Round(cx + FastTrig.Cos(a) * ax * rs), (int)MathF.Round(cy + FastTrig.Sin(a) * ay * rs), core);
        }
    }
}
