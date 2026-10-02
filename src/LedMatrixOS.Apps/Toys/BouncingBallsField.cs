using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Effects;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Toys;

/// <summary>
/// The whole Bouncing Balls scene as one node: glossy balls with gravity (that occasionally tilts), ball-ball collisions, squash and stretch,
/// glowing motion trails and spark bursts, or a lava-lamp metaball variant. Physics runs on a fixed 120 Hz step.
/// </summary>
public sealed class BouncingBallsField : Node
{
    public const int MaxBalls = 24;
    private const int Seed = 1337;

    private static readonly Func<float, float> SquashEase = Easing.OutElastic;
    private static readonly Func<float, float> PopEase = Easing.OutBack;
    private static readonly Func<float, float> TiltEase = Easing.InOutCubic;

    private sealed class Ball
    {
        public bool Active;
        public float X, Y, VX, VY, R, Mass, PX, PY;
        public int Color;
        public float SquashAngle;
        public readonly Tween<float> Squash = new(0f);
        public readonly Tween<float> Scale = new(0f);
    }

    private sealed class Blob
    {
        public float X, Y, VX, VY, R, Heat, Phase;
    }

    private readonly Ball[] _balls = new Ball[MaxBalls];
    private readonly Blob[] _blobs = new Blob[MaxBalls];
    private readonly Random _rng = new(Seed);
    private readonly GlowEffect _glow = new() { Threshold = 150f, Strength = 0.9f, Radius = 2 };
    private readonly GlowEffect _lavaGlow = new() { Threshold = 120f, Strength = 0.8f, Radius = 3 };
    private readonly Tween<float> _gravityAngle = new((float)(Math.PI / 2));
    private readonly Emitter[] _bursts = new Emitter[6];
    private readonly Pixel[] _ramp = new Pixel[256];

    private ParticleSystem _particles = null!;
    private TrailPlane _trail = null!;
    private float[] _field = [];
    private FixedStepper _stepper;
    private int _w = 256, _h = 64;
    private int _respawnCursor;
    private float _kickTimer, _time;
    private bool _built;
    private Pixel[] _pal = ToyPalettes.Get("neon");
    private FrameContext _ctx;

    private string _style = "balls", _paletteName = "neon";
    private int _count = 10, _gravity = 60;
    private bool _trails = true;
    private int _builtCount = -1;
    private string _builtStyle = "";

    /// <summary>"balls" or "lava".</summary>
    public string Style { get => _style; set => _style = value; }
    public int Count { get => _count; set => _count = Math.Clamp(value, 1, MaxBalls); }
    /// <summary>0 (weightless) to 100.</summary>
    public int Gravity { get => _gravity; set => _gravity = Math.Clamp(value, 0, 100); }
    public bool Trails { get => _trails; set => _trails = value; }

    public string PaletteName
    {
        get => _paletteName;
        set
        {
            _paletteName = value;
            _pal = ToyPalettes.Get(value);
            BuildBursts();
        }
    }

    // Diagnostics for tests.
    public int ActiveBalls { get; private set; }
    public long Collisions { get; private set; }
    public long WallHits { get; private set; }

    public bool AllInBounds()
    {
        foreach (var b in _balls)
            if (b is { Active: true } && (b.X < b.R - 0.01f || b.X > _w - b.R + 0.01f || b.Y < b.R - 0.01f || b.Y > _h - b.R + 0.01f)) return false;
        return true;
    }

    /// <summary>Largest ball speed, pixels per second.</summary>
    public float MaxSpeed()
    {
        float m = 0;
        foreach (var b in _balls) if (b is { Active: true }) m = MathF.Max(m, MathF.Sqrt(b.VX * b.VX + b.VY * b.VY));
        return m;
    }

    public float MeanSpeed()
    {
        float s = 0; int n = 0;
        foreach (var b in _balls) if (b is { Active: true }) { s += MathF.Sqrt(b.VX * b.VX + b.VY * b.VY); n++; }
        return n == 0 ? 0 : s / n;
    }

    protected override void OnHostChanged()
    {
        if (Host is null) return;
        _w = Host.Width;
        _h = Host.Height;
        if (_built) return;
        _built = true;
        _particles = new ParticleSystem(_w, _h, 600, new Random(Seed + 1));
        _trail = new TrailPlane(_w, _h);
        _field = new float[_w * _h];
        for (int i = 0; i < MaxBalls; i++) { _balls[i] = new Ball(); _blobs[i] = new Blob(); }
        BuildBursts();

        // Gravity drifts: calm for a while, then swings to a new direction so the pile never settles into a boring heap.
        var tilt = Timeline.Sequence(
            Timeline.Delay(TimeSpan.FromSeconds(6)),
            Timeline.Do(Tilt),
            Timeline.Delay(TimeSpan.FromSeconds(3.5)),
            Timeline.Do(Untilt)).Loop();
        Host.Animator.Add(tilt);

        // One ball is retired and a fresh one pops in every so often.
        var respawn = Timeline.Sequence(Timeline.Delay(TimeSpan.FromSeconds(7)), Timeline.Do(Respawn)).Loop();
        Host.Animator.Add(respawn);
    }

    private void BuildBursts()
    {
        for (int i = 0; i < _bursts.Length; i++)
        {
            var c = _pal[i % _pal.Length];
            _bursts[i] = new Emitter
            {
                LifetimeMin = 0.35f, LifetimeMax = 0.8f,
                SpeedMin = 20f, SpeedMax = 85f,
                Spread = 90f,
                GravityY = 40f,
                Size = 1f,
                Gradient = [Pixel.White, c, c.WithBrightness(0.35f)],
                AlphaStart = 1f, AlphaEnd = 0f,
            };
        }
    }

    private void Tilt()
    {
        if (_style != "balls") return;
        // Left, right or up (screen angles: 0 = right, 90 degrees = down).
        float[] choices = TiltChoices;
        float target = choices[_rng.Next(choices.Length)];
        Host!.Animator.Animate(_gravityAngle, target, TimeSpan.FromMilliseconds(900), TiltEase);
    }

    private static readonly float[] TiltChoices = [0.35f, MathF.PI - 0.35f, -MathF.PI / 2f, 0.1f, MathF.PI - 0.1f];

    private void Untilt() => Host!.Animator.Animate(_gravityAngle, MathF.PI / 2f, TimeSpan.FromMilliseconds(1200), TiltEase);

    private void Respawn()
    {
        if (_style != "balls" || ActiveBalls == 0) return;
        _respawnCursor = (_respawnCursor + 1) % _count;
        var old = _balls[_respawnCursor];
        if (old.Active) Burst(old.X, old.Y, old.Color, 0, 360f, 10);
        Spawn(old, _respawnCursor);
    }

    private void Place(Ball b, int index)
    {
        b.R = 3f + _rng.NextSingle() * 3.6f;
        b.Mass = b.R * b.R;
        b.X = b.R + _rng.NextSingle() * (_w - 2 * b.R);
        b.Y = b.R + _rng.NextSingle() * (_h - 2 * b.R);
        float ang = _rng.NextSingle() * MathF.Tau;
        float sp = 50f + _rng.NextSingle() * 90f;
        b.VX = MathF.Cos(ang) * sp;
        b.VY = MathF.Sin(ang) * sp;
        b.PX = b.X; b.PY = b.Y;
        b.Color = index % _pal.Length;
        b.Squash.Set(0f);
        b.Scale.Set(0f);
    }

    private void Pop(Ball b)
    {
        b.Active = true;
        b.PX = b.X; b.PY = b.Y;
        Host?.Animator.Animate(b.Scale, 1f, TimeSpan.FromMilliseconds(520), PopEase);
        Burst(b.X, b.Y, b.Color, 0, 360f, 14);
    }

    private void Spawn(Ball b, int index)
    {
        Place(b, index);
        Pop(b);
    }

    private void Rebuild()
    {
        _builtCount = _count;
        _builtStyle = _style;
        _trail.Clear();
        _particles.Clear();
        _stepper.Reset();
        ActiveBalls = 0;
        for (int i = 0; i < MaxBalls; i++)
        {
            _balls[i].Active = false;
            _balls[i].Scale.Set(0f);
            _balls[i].Squash.Set(0f);
        }

        if (_style == "lava")
        {
            int n = Math.Clamp(_count / 2 + 3, 4, 12);
            for (int i = 0; i < n; i++)
            {
                var b = _blobs[i];
                b.R = 7f + _rng.NextSingle() * 7f;
                b.X = 30f + _rng.NextSingle() * (_w - 60f);
                b.Y = b.R + _rng.NextSingle() * (_h - 2 * b.R);
                b.VX = b.VY = 0;
                b.Heat = _rng.NextSingle();
                b.Phase = _rng.NextSingle() * MathF.Tau;
            }
            ActiveBalls = n;
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            // Pop them in one after another; a ball is inert until its turn.
            var ball = _balls[i];
            Place(ball, i);
            int idx = i;
            Host!.Animator.Add(Timeline.Sequence(
                Timeline.Delay(TimeSpan.FromMilliseconds(i * 90)),
                Timeline.Do(() => { if (idx < _count && _style == "balls") Pop(ball); })));
        }
        ActiveBalls = _count;
    }

    private void Burst(float x, float y, int color, float angleRad, float spread, int count)
    {
        var e = _bursts[color % _bursts.Length];
        e.X = x; e.Y = y;
        e.Angle = angleRad * (180f / MathF.PI);
        e.Spread = spread;
        _particles.Burst(e, count);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _ctx = ctx;
        if (!_built) return;
        if (_builtCount != _count || _builtStyle != _style) Rebuild();

        float dt = (float)ctx.Delta.TotalSeconds;
        _time += Math.Clamp(dt, 0f, 0.1f);
        int steps = _stepper.Advance(dt);
        for (int i = 0; i < steps; i++)
        {
            if (_style == "lava") StepLava(FixedStepper.Step);
            else StepBalls(FixedStepper.Step);
        }

        _particles.Update(MathF.Min(dt, 0.1f));
        if (_style == "balls") UpdateTrails(dt);
    }

    private void UpdateTrails(float dt)
    {
        if (!_trails) { _trail.Clear(); return; }
        _trail.Decay(MathF.Pow(0.5f, Math.Clamp(dt, 0.001f, 0.1f) / 0.07f));
        for (int i = 0; i < MaxBalls; i++)
        {
            var b = _balls[i];
            if (!b.Active) continue;
            var c = _pal[b.Color % _pal.Length];
            float r = b.R * MathF.Max(b.Scale.Value, 0f) * 0.72f;
            float dx = b.X - b.PX, dy = b.Y - b.PY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            int n = Math.Min(24, Math.Max(1, (int)(dist / MathF.Max(1f, r * 0.5f))));
            for (int k = 1; k <= n; k++)
            {
                float t = k / (float)n;
                _trail.Stamp(b.PX + dx * t, b.PY + dy * t, r, c, 0.65f);
            }
            b.PX = b.X; b.PY = b.Y;
        }
    }

    private void StepBalls(float dt)
    {
        float g = _gravity * 2.4f;
        float ga = _gravityAngle.Value;
        float gx = MathF.Cos(ga) * g, gy = MathF.Sin(ga) * g;
        float wallRest = _gravity > 0 ? 0.9f : 1f;

        for (int i = 0; i < MaxBalls; i++)
        {
            var b = _balls[i];
            if (!b.Active) continue;
            b.VX += gx * dt;
            b.VY += gy * dt;
            b.X += b.VX * dt;
            b.Y += b.VY * dt;

            if (b.X < b.R) { b.X = b.R; Wall(b, 1, 0, -b.VX, wallRest, true); }
            else if (b.X > _w - b.R) { b.X = _w - b.R; Wall(b, -1, 0, b.VX, wallRest, true); }
            if (b.Y < b.R) { b.Y = b.R; Wall(b, 0, 1, -b.VY, wallRest, false); }
            else if (b.Y > _h - b.R) { b.Y = _h - b.R; Wall(b, 0, -1, b.VY, wallRest, false); }
        }

        for (int i = 0; i < MaxBalls; i++)
        {
            var a = _balls[i];
            if (!a.Active) continue;
            for (int j = i + 1; j < MaxBalls; j++)
            {
                var b = _balls[j];
                if (!b.Active) continue;
                float dx = b.X - a.X, dy = b.Y - a.Y, rr = a.R + b.R;
                float d2 = dx * dx + dy * dy;
                if (d2 >= rr * rr) continue;
                float d = MathF.Sqrt(d2);
                float nx, ny;
                if (d < 0.0001f) { nx = 1; ny = 0; d = 0; } else { nx = dx / d; ny = dy / d; }
                float overlap = rr - d;
                float ia = 1f / a.Mass, ib = 1f / b.Mass;
                float sum = ia + ib;
                a.X -= nx * overlap * ia / sum; a.Y -= ny * overlap * ia / sum;
                b.X += nx * overlap * ib / sum; b.Y += ny * overlap * ib / sum;
                Clamp(a); Clamp(b);
                float vn = (b.VX - a.VX) * nx + (b.VY - a.VY) * ny;
                if (vn >= 0) continue;
                float imp = -(1f + 0.94f) * vn / sum;
                a.VX -= imp * nx * ia; a.VY -= imp * ny * ia;
                b.VX += imp * nx * ib; b.VY += imp * ny * ib;
                Collisions++;
                float speed = -vn;
                if (speed > 25f)
                {
                    Squish(a, nx, ny, speed);
                    Squish(b, -nx, -ny, speed);
                    float cx = a.X + nx * a.R, cy = a.Y + ny * a.R;
                    Burst(cx, cy, a.Color, 0, 360f, 2 + (int)Math.Min(speed / 25f, 8));
                    if (speed > 90f) Burst(cx, cy, b.Color, 0, 360f, 3);
                }
            }
        }

        // Keep energy sane: when everything has gone quiet, the floor thumps and the balls leap.
        _kickTimer += dt;
        if (_kickTimer > 0.5f)
        {
            _kickTimer = 0;
            if (_gravity > 0 && MeanSpeed() < 28f)
            {
                for (int i = 0; i < MaxBalls; i++)
                {
                    var b = _balls[i];
                    if (!b.Active) continue;
                    b.VX += (_rng.NextSingle() - 0.5f) * 140f;
                    b.VY -= 120f + _rng.NextSingle() * 90f;
                    Squish(b, 0, -1, 90f);
                    Burst(b.X, b.Y + b.R, b.Color, -MathF.PI / 2, 120f, 4);
                }
            }
        }
    }

    private void Clamp(Ball b)
    {
        b.X = Math.Clamp(b.X, b.R, _w - b.R);
        b.Y = Math.Clamp(b.Y, b.R, _h - b.R);
    }

    private void Wall(Ball b, float nx, float ny, float vAlongInto, float rest, bool xAxis)
    {
        // vAlongInto > 0 means the ball was moving into the wall.
        if (vAlongInto <= 0) return;
        if (xAxis) b.VX = -b.VX * rest; else b.VY = -b.VY * rest;
        // Kill tiny rebounds so a ball can rest on the floor without jitter.
        if (_gravity > 0 && vAlongInto * rest < 6f) { if (xAxis) b.VX = 0; else b.VY = 0; }
        WallHits++;
        if (vAlongInto > 30f)
        {
            Squish(b, nx, ny, vAlongInto);
            Burst(b.X - nx * b.R, b.Y - ny * b.R, b.Color, MathF.Atan2(ny, nx), 80f, 2 + (int)Math.Min(vAlongInto / 30f, 10));
        }
    }

    private void Squish(Ball b, float nx, float ny, float speed)
    {
        float s = Math.Clamp(speed / 170f, 0f, 1f) * 0.55f;
        if (s < 0.06f) return;
        b.SquashAngle = MathF.Atan2(ny, nx);
        b.Squash.Set(s);
        Host?.Animator.Animate(b.Squash, 0f, TimeSpan.FromMilliseconds(560), SquashEase);
    }

    private void StepLava(float dt)
    {
        int n = ActiveBalls;
        for (int i = 0; i < n; i++)
        {
            var b = _blobs[i];
            // Heat rises near the bottom and drains near the top; hot blobs float, cool ones sink.
            if (b.Y > _h - b.R - 6f) b.Heat += dt * 0.55f;
            else if (b.Y < b.R + 6f) b.Heat -= dt * 0.55f;
            b.Heat = Math.Clamp(b.Heat, 0f, 1f);
            float lift = (b.Heat - 0.5f) * 16f;
            b.VY += -lift * dt * 2.2f;
            b.VX += MathF.Sin(_time * 0.35f + b.Phase) * 4f * dt;
            b.VX *= 1f - 0.4f * dt;
            b.VY *= 1f - 0.7f * dt;
            b.X += b.VX * dt;
            b.Y += b.VY * dt;
            if (b.X < b.R * 0.4f) { b.X = b.R * 0.4f; b.VX = MathF.Abs(b.VX); }
            else if (b.X > _w - b.R * 0.4f) { b.X = _w - b.R * 0.4f; b.VX = -MathF.Abs(b.VX); }
            if (b.Y < b.R * 0.7f) { b.Y = b.R * 0.7f; b.VY = MathF.Abs(b.VY) * 0.3f; }
            else if (b.Y > _h - b.R * 0.7f) { b.Y = _h - b.R * 0.7f; b.VY = -MathF.Abs(b.VY) * 0.3f; }
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (!_built) { frame.Fill(bounds, Pixel.Black); return; }
        if (_style == "lava") RenderLava(frame, bounds);
        else RenderBalls(frame, bounds);
    }

    private void RenderBalls(FrameBuffer frame, Rectangle bounds)
    {
        frame.Fill(bounds, Pixel.Black);

        if (_trails) _trail.AddTo(frame);

        for (int i = 0; i < MaxBalls; i++)
        {
            var b = _balls[i];
            if (!b.Active) continue;
            float scale = MathF.Max(b.Scale.Value, 0f);
            if (scale < 0.02f) continue;
            float r = b.R * scale;
            float sq = b.Squash.Value;
            float speed = MathF.Sqrt(b.VX * b.VX + b.VY * b.VY);
            float vStretch = Math.Min(speed / 520f, 0.28f);
            float ra, rb, angle;
            if (MathF.Abs(sq) > vStretch * 0.8f)
            {
                // Impact: flatten along the normal, bulge across it; the elastic overshoot makes it wobble back.
                ra = r * Math.Clamp(1f - sq, 0.45f, 1.5f);
                rb = r * r / ra;
                angle = b.SquashAngle;
            }
            else
            {
                ra = r * (1f + vStretch);
                rb = r * r / ra;
                angle = MathF.Atan2(b.VY, b.VX);
            }
            ToyGfx.ShadedEllipse(frame, b.X, b.Y, ra, rb, angle, _pal[b.Color % _pal.Length]);
        }

        _particles.Render(frame);
        _glow.Apply(frame, _ctx);
    }

    private void RenderLava(FrameBuffer frame, Rectangle bounds)
    {
        // Colour ramp that slowly drifts through the palette.
        float shift = _time * 0.025f;
        var c0 = ToyPalettes.Sample(_pal, shift);
        var c1 = ToyPalettes.Sample(_pal, shift + 0.2f);
        var c2 = Pixel.Lerp(ToyPalettes.Sample(_pal, shift + 0.45f), Pixel.White, 0.45f);
        for (int i = 0; i < 256; i++)
        {
            float f = i / 255f * 1.7f;
            Pixel c;
            if (f < 0.1f) c = Pixel.Black;
            else if (f < 0.5f) { float u = (f - 0.1f) / 0.4f; c = c0.WithBrightness(0.42f * u * u); }
            else
            {
                float u = Math.Clamp((f - 0.5f) / 1.1f, 0f, 1f);
                c = u < 0.5f ? Pixel.Lerp(c0, c1, u * 2f) : Pixel.Lerp(c1, c2, (u - 0.5f) * 2f);
                // A dark seam just inside the surface makes the blob read as a glossy volume.
                if (f < 0.58f) c = c.WithBrightness(0.72f + (f - 0.5f) * 3.5f);
            }
            _ramp[i] = c;
        }

        var field = _field;
        Array.Clear(field);
        int w = _w, h = _h;
        for (int bi = 0; bi < ActiveBalls; bi++)
        {
            var b = _blobs[bi];
            float R = b.R * 2.4f, R2 = R * R;
            int x0 = Math.Max(0, (int)(b.X - R)), x1 = Math.Min(w - 1, (int)(b.X + R) + 1);
            int y0 = Math.Max(0, (int)(b.Y - R)), y1 = Math.Min(h - 1, (int)(b.Y + R) + 1);
            for (int y = y0; y <= y1; y++)
            {
                float dy = y - b.Y;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - b.X;
                    float d2 = dx * dx * 0.82f + dy * dy; // blobs are slightly wider than tall: they wobble like wax
                    if (d2 >= R2) continue;
                    float t = 1f - d2 / R2;
                    field[y * w + x] += t * t * t;
                }
            }
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = (int)(field[y * w + x] * (255f / 1.7f));
                frame.SetPixel(bounds.X + x, bounds.Y + y, _ramp[idx > 255 ? 255 : idx]);
            }

        _lavaGlow.Apply(frame, _ctx);
    }
}
