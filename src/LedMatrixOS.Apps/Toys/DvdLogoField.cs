using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Effects;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Toys;

/// <summary>
/// The DVD screensaver, lovingly overdone: a bold "DVD" + disc-swoosh logo that squashes into every wall, sprays sparks, leaves a ghost
/// trail and changes colour, and a corner hit sets off a celebration (flash, confetti rain, rainbow border, a bouncing "CORNER!" banner).
/// An optional gentle aim assist nudges the speed so a corner actually shows up now and then.
/// </summary>
public sealed class DvdLogoField : Node
{
    public const int LogoW = 40, LogoH = 27;
    private const int Seed = 20240101;
    private const int AssistAfterBounces = 9;
    private const float CornerTolerance = 1.5f;

    public static readonly string[] PaletteNames = ["classic", "neon", "sunset", "ocean", "candy", "aurora"];

    private static readonly Pixel[] Classic =
    [
        new(255, 40, 40), new(255, 220, 0), new(40, 255, 70), new(0, 220, 255), new(70, 90, 255), new(255, 50, 230), new(255, 130, 0),
    ];

    private static readonly Pixel[] Rainbow =
    [
        new(255, 40, 40), new(255, 190, 0), new(60, 255, 60), new(0, 220, 255), new(90, 90, 255), new(255, 60, 220),
    ];

    private static readonly Func<float, float> ElasticEase = Easing.OutElastic;
    private static readonly Func<float, float> BounceEase = Easing.OutBounce;
    private static readonly Func<float, float> QuadEase = Easing.OutQuad;
    private const string BannerText = "CORNER!";

    private readonly byte[] _mask = new byte[LogoW * LogoH];
    private readonly Random _rng = new(Seed);
    private readonly GlowEffect _glow = new() { Threshold = 150f, Strength = 0.85f, Radius = 2 };
    private readonly Tween<float> _sx = new(1f), _sy = new(1f), _flash = new(0f), _party = new(0f), _screenFlash = new(0f);
    private readonly Tween<float> _bannerDrop = new(-24f), _bannerAlpha = new(0f);
    private readonly TextRun[] _letters = new TextRun[BannerText.Length];
    private readonly Emitter[] _sparks = new Emitter[8];
    private Emitter _rain = null!;
    private Emitter _confetti = null!;
    private ParticleSystem _particles = null!;
    private TrailPlane _trail = null!;
    private Timeline? _celebration;
    private FixedStepper _stepper;
    private FrameContext _ctx;
    private int _w = 256, _h = 64;
    private bool _built;
    private float _time;

    private Pixel[] _pal = Classic;
    private string _paletteName = "classic";
    private int _colorIndex;
    private int _speed = 100;
    private bool _trails = true, _assist = true;

    private float _x, _y, _vx = 34f, _vy = 21f;
    private int _bouncesSinceCorner = AssistAfterBounces - 3;
    private bool _aimed;
    private float _cornerCooldown;

    public int Speed { get => _speed; set => _speed = Math.Clamp(value, 20, 400); }
    public bool Trails { get => _trails; set => _trails = value; }
    public bool Assist { get => _assist; set => _assist = value; }

    public string PaletteName
    {
        get => _paletteName;
        set
        {
            _paletteName = value;
            _pal = value == "classic" ? Classic : ToyPalettes.Get(value);
            _colorIndex %= _pal.Length;
            if (_built) BuildSparks();
        }
    }

    // Diagnostics for tests and the app's counter.
    public int Corners { get; private set; }
    public int Bounces { get; private set; }
    public float X => _x;
    public float Y => _y;
    public float VX => _vx;
    public float VY => _vy;
    public bool Celebrating => _party.Value > 0.01f || _party.IsRunning;
    public Pixel LogoColor => _pal[_colorIndex % _pal.Length];

    /// <summary>Raised on every corner hit with the running total.</summary>
    public event Action<int>? CornerHit;

    public float MaxX => _w - LogoW;
    public float MaxY => _h - LogoH;

    /// <summary>Test hook: sets the logo state directly.</summary>
    public void Place(float x, float y, float vx, float vy)
    {
        _x = x; _y = y; _vx = vx; _vy = vy;
        _aimed = false;
        _cornerCooldown = 0f;
        if (_built) { _trail.Clear(); _particles.Clear(); }
    }

    /// <summary>Advances the simulation by <paramref name="seconds"/> of logo time in fixed steps (no rendering, no frame needed).</summary>
    public void Simulate(float seconds)
    {
        if (!_built) throw new InvalidOperationException("Not hosted yet");
        int n = (int)(seconds / FixedStepper.Step);
        for (int i = 0; i < n; i++) Step(FixedStepper.Step);
    }

    protected override void OnHostChanged()
    {
        if (Host is null || _built) return;
        _built = true;
        _w = Host.Width;
        _h = Host.Height;
        _particles = new ParticleSystem(_w, _h, 700, new Random(Seed + 1));
        _trail = new TrailPlane(_w, _h);
        BuildMask();
        BuildSparks();

        _confetti = ParticlePresets.Confetti(0, 0);
        _confetti.Size = 2f;
        _rain = ParticlePresets.Confetti(0, -2);
        _rain.X = 0; _rain.Width = _w - 1; _rain.Height = 0;
        _rain.Angle = 90f; _rain.Spread = 50f;
        _rain.SpeedMin = 10f; _rain.SpeedMax = 40f;
        _rain.LifetimeMin = 1.6f; _rain.LifetimeMax = 2.6f;
        _rain.GravityY = 45f;
        _rain.Size = 2f;
        _rain.Rate = 0f;
        _particles.Add(_rain);

        _x = 20f + _rng.NextSingle() * (_w - LogoW - 40f);
        _y = 4f + _rng.NextSingle() * (_h - LogoH - 8f);
        if (_rng.Next(2) == 0) _vx = -_vx;
        if (_rng.Next(2) == 0) _vy = -_vy;
        _colorIndex = _rng.Next(_pal.Length);
    }

    private void BuildSparks()
    {
        for (int i = 0; i < _sparks.Length; i++)
        {
            var c = _pal[i % _pal.Length];
            _sparks[i] = new Emitter
            {
                LifetimeMin = 0.3f, LifetimeMax = 0.8f,
                SpeedMin = 25f, SpeedMax = 90f,
                Spread = 100f,
                Gradient = [Pixel.White, c, c.WithBrightness(0.3f)],
                AlphaStart = 1f, AlphaEnd = 0f,
            };
        }
    }

    // "DVD" in a faux-bold 9x18 font over a disc swoosh with "VIDEO" knocked out, baked once into a coverage mask.
    private void BuildMask()
    {
        var big = new TextRun();
        big.Set(Fonts.Big, "DVD");
        var video = new TextRun();
        video.Set(Fonts.ExtraSmall, "VIDEO");
        int tx = (LogoW - big.Width - 1) / 2;
        for (int y = 0; y < LogoH; y++)
            for (int x = 0; x < LogoW; x++)
            {
                bool ink = big.Ink(x - tx, y) || big.Ink(x - tx - 1, y);
                // Disc swoosh: an ellipse across the bottom.
                float ex = (x + 0.5f - LogoW / 2f) / (LogoW / 2f - 0.5f), ey = (y + 0.5f - 22.5f) / 4.6f;
                if (ex * ex + ey * ey <= 1f)
                {
                    ink = true;
                    int vx = x - (LogoW - video.Width) / 2, vy = y - 19;
                    if (video.Ink(vx, vy)) ink = false;
                }
                _mask[y * LogoW + x] = ink ? (byte)255 : (byte)0;
            }
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _ctx = ctx;
        if (!_built) return;
        if (_letters[0] is null)
            for (int i = 0; i < _letters.Length; i++) { _letters[i] = new TextRun(); _letters[i].Set(Fonts.Big, BannerText[i].ToString()); }

        float dt = Math.Clamp((float)ctx.Delta.TotalSeconds, 0f, 0.1f);
        _time += dt;
        int steps = _stepper.Advance(dt);
        for (int i = 0; i < steps; i++) Step(FixedStepper.Step);
        _particles.Update(dt);

        if (_trails)
        {
            _trail.Decay(MathF.Pow(0.5f, Math.Max(dt, 0.001f) / 0.11f));
            StampGhost();
        }
        else _trail.Clear();
    }

    private void Step(float dt)
    {
        dt *= _speed / 100f;
        if (_cornerCooldown > 0f) _cornerCooldown -= dt;
        _x += _vx * dt;
        _y += _vy * dt;
        float mx = MaxX, my = MaxY;
        bool hitX = false, hitY = false;
        if (_x <= 0f) { _x = 0f; _vx = MathF.Abs(_vx); hitX = true; }
        else if (_x >= mx) { _x = mx; _vx = -MathF.Abs(_vx); hitX = true; }
        if (_y <= 0f) { _y = 0f; _vy = MathF.Abs(_vy); hitY = true; }
        else if (_y >= my) { _y = my; _vy = -MathF.Abs(_vy); hitY = true; }
        if (!hitX && !hitY) return;

        // A corner: both walls at once, or one wall while the other is within a pixel or two.
        bool nearY = _y < CornerTolerance || _y > my - CornerTolerance;
        bool nearX = _x < CornerTolerance || _x > mx - CornerTolerance;
        bool corner = _cornerCooldown <= 0f && ((hitX && nearY) || (hitY && nearX));

        Bounces++;
        _bouncesSinceCorner++;
        Impact(hitX, hitY);
        if (corner) OnCorner(); else if (_assist && !_aimed && _bouncesSinceCorner >= AssistAfterBounces) Aim();
    }

    private void Impact(bool hitX, bool hitY)
    {
        _colorIndex = (_colorIndex + 1 + _rng.Next(_pal.Length - 1)) % _pal.Length;
        _flash.Set(1f);
        Host?.Animator.Animate(_flash, 0f, TimeSpan.FromMilliseconds(260), QuadEase);
        float sx = hitX ? 0.68f : 1.16f, sy = hitY ? 0.68f : 1.16f;
        if (hitX && hitY) { sx = sy = 0.7f; }
        _sx.Set(sx); _sy.Set(sy);
        Host?.Animator.Animate(_sx, 1f, TimeSpan.FromMilliseconds(520), ElasticEase);
        Host?.Animator.Animate(_sy, 1f, TimeSpan.FromMilliseconds(520), ElasticEase);

        var e = _sparks[_colorIndex % _sparks.Length];
        if (hitX)
        {
            bool left = _x <= 0.5f;
            e.X = left ? 0f : _w - 1f; e.Y = _y; e.Width = 0; e.Height = LogoH;
            e.Angle = left ? 0f : 180f;
            _particles.Burst(e, 14);
        }
        if (hitY)
        {
            bool top = _y <= 0.5f;
            e.X = _x; e.Y = top ? 0f : _h - 1f; e.Width = LogoW; e.Height = 0;
            e.Angle = top ? 90f : -90f;
            _particles.Burst(e, 14);
        }
    }

    private void OnCorner()
    {
        Corners++;
        _cornerCooldown = 0.4f; // the second wall of the same corner must not count again
        _bouncesSinceCorner = 0;
        _aimed = false;

        // Burst of confetti from the corner we hit, fired inwards.
        bool left = _x < _w / 2f, top = _y < _h / 2f;
        _confetti.X = left ? 0f : _w - 1f;
        _confetti.Y = top ? 0f : _h - 1f;
        _confetti.Angle = (left ? 0f : 180f) + (top ? 35f : -35f);
        _confetti.Spread = 70f;
        _confetti.SpeedMin = 50f; _confetti.SpeedMax = 170f;
        _confetti.GravityY = 55f;
        _particles.Burst(_confetti, 140);

        _party.Set(1f);
        _screenFlash.Set(1f);
        _bannerAlpha.Set(1f);
        _bannerDrop.Set(-24f);
        if (Host is { } host)
        {
            _celebration?.Cancel();
            host.Animator.Animate(_screenFlash, 0f, TimeSpan.FromMilliseconds(450), QuadEase);
            _celebration = Timeline.Sequence(
                Timeline.Do(() => _rain.Rate = 110f),
                Timeline.To(_bannerDrop, 22f, TimeSpan.FromMilliseconds(750), BounceEase),
                Timeline.Delay(TimeSpan.FromMilliseconds(1300)),
                Timeline.Do(() => _rain.Rate = 0f),
                Timeline.Parallel(
                    Timeline.To(_bannerAlpha, 0f, TimeSpan.FromMilliseconds(500)),
                    Timeline.To(_party, 0f, TimeSpan.FromMilliseconds(900))));
            host.Animator.Add(_celebration);
        }

        CornerHit?.Invoke(Corners);
    }

    // Pick, among the next few wall arrivals on each axis, the pair that lines up best and trim the y speed (by at most ~25%) so both land together.
    private void Aim()
    {
        float mx = MaxX, my = MaxY;
        float ax = MathF.Abs(_vx), ay = MathF.Abs(_vy);
        float dx0 = _vx > 0 ? mx - _x : _x, dy0 = _vy > 0 ? my - _y : _y;
        float bestErr = float.MaxValue, bestAy = 0;
        for (int k = 1; k <= 5; k++)
        {
            float time = (dx0 + (k - 1) * mx) / ax;
            if (time < 2f) continue;
            for (int j = 0; j <= 6; j++)
            {
                float wantAy = (dy0 + j * my) / time;
                float ratio = wantAy / ay;
                if (ratio < 0.78f || ratio > 1.28f) continue;
                float err = MathF.Abs(MathF.Log(ratio));
                if (err < bestErr) { bestErr = err; bestAy = wantAy; }
            }
        }

        if (bestAy <= 0) return;
        _vy = MathF.Sign(_vy) * bestAy;
        _aimed = true;
    }

    private void StampGhost()
    {
        var c = LogoColor;
        int ox = (int)MathF.Round(_x), oy = (int)MathF.Round(_y);
        for (int y = 0; y < LogoH; y++)
            for (int x = 0; x < LogoW; x++)
            {
                byte m = _mask[y * LogoW + x];
                if (m != 0) _trail.StampPixel(ox + x, oy + y, c, 0.5f);
            }
    }

    private Pixel CurrentColor()
    {
        var c = LogoColor;
        if (_party.Value > 0.01f) c = ToyPalettes.Sample(Rainbow, _time * 1.6f);
        float flash = Math.Clamp(_flash.Value, 0f, 1f);
        return flash > 0.01f ? Pixel.Lerp(c, Pixel.White, flash * 0.75f) : c;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        frame.Fill(bounds, Pixel.Black);
        if (!_built) return;

        if (_trails) _trail.AddTo(frame);
        DrawLogo(frame, CurrentColor());
        _particles.Render(frame);

        float party = Math.Clamp(_party.Value, 0f, 1f);
        if (party > 0.01f) DrawBorder(frame, party);
        if (_bannerAlpha.Value > 0.01f) DrawBanner(frame);

        float sf = _screenFlash.Value;
        if (sf > 0.01f) AddFlash(frame, sf);
        _glow.Apply(frame, _ctx);
    }

    private void DrawLogo(FrameBuffer frame, Pixel color)
    {
        float sx = Math.Max(_sx.Value, 0.2f), sy = Math.Max(_sy.Value, 0.2f);
        float cx = _x + LogoW / 2f, cy = _y + LogoH / 2f;
        float hw = LogoW * sx / 2f, hh = LogoH * sy / 2f;
        int x0 = (int)MathF.Floor(cx - hw) - 1, x1 = (int)MathF.Ceiling(cx + hw) + 1;
        int y0 = (int)MathF.Floor(cy - hh) - 1, y1 = (int)MathF.Ceiling(cy + hh) + 1;
        for (int y = y0; y <= y1; y++)
        {
            float v = (y + 0.5f - cy) / sy + LogoH / 2f - 0.5f;
            float shade = 1.12f - 0.3f * Math.Clamp(v / LogoH, 0f, 1f);
            var rowColor = color.WithBrightness(shade);
            for (int x = x0; x <= x1; x++)
            {
                float u = (x + 0.5f - cx) / sx + LogoW / 2f - 0.5f;
                float a = Sample(u, v);
                if (a <= 0.02f) continue;
                if (a >= 0.98f) frame.SetPixel(x, y, rowColor);
                else frame.BlendPixel(x, y, rowColor, a);
            }
        }
    }

    private float Sample(float u, float v)
    {
        int iu = (int)MathF.Floor(u), iv = (int)MathF.Floor(v);
        float fu = u - iu, fv = v - iv;
        float a = At(iu, iv), b = At(iu + 1, iv), c = At(iu, iv + 1), d = At(iu + 1, iv + 1);
        return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
    }

    private float At(int x, int y) => (uint)x < LogoW && (uint)y < LogoH ? _mask[y * LogoW + x] * (1f / 255f) : 0f;

    private void DrawBorder(FrameBuffer frame, float party)
    {
        // A rainbow that chases around the panel edge.
        int w = frame.Width, h = frame.Height, perim = 2 * (w + h) - 4;
        for (int i = 0; i < perim; i++)
        {
            int x, y;
            if (i < w) { x = i; y = 0; }
            else if (i < w + h - 1) { x = w - 1; y = i - w + 1; }
            else if (i < 2 * w + h - 2) { x = w - 1 - (i - (w + h - 2)); y = h - 1; }
            else { x = 0; y = h - 1 - (i - (2 * w + h - 3)); }
            var c = ToyPalettes.Sample(Rainbow, i / 90f - _time * 1.2f);
            ToyGfx.AddPixel(frame, x, y, c, party);
            ToyGfx.AddPixel(frame, x == 0 ? 1 : x == w - 1 ? w - 2 : x, y == 0 ? 1 : y == h - 1 ? h - 2 : y, c, party * 0.5f);
        }
    }

    private void DrawBanner(FrameBuffer frame)
    {
        int total = 0;
        foreach (var l in _letters) total += l.Width + 2;
        int x = (frame.Width - total) / 2;
        int y = (int)MathF.Round(_bannerDrop.Value);
        float alpha = Math.Clamp(_bannerAlpha.Value, 0f, 1f);
        for (int i = 0; i < _letters.Length; i++)
        {
            int wob = (int)MathF.Round(MathF.Sin(_time * 9f + i * 0.8f) * 2f);
            var c = ToyPalettes.Sample(Rainbow, _time * 1.5f + i / (float)_letters.Length);
            if (alpha < 0.999f) c = c.WithBrightness(alpha);
            _letters[i].Draw(frame, x, y + wob, c, outline: new Pixel(0, 0, 0).WithBrightness(alpha));
            _letters[i].Draw(frame, x + 1, y + wob, c);
            x += _letters[i].Width + 2;
        }
    }

    private static void AddFlash(FrameBuffer frame, float k)
    {
        int c = (int)(255 * Math.Clamp(k, 0f, 1f) * 0.3f);
        var span = frame.GetPixelsSpan();
        int w = frame.Width;
        for (int i = 0; i < span.Length; i++)
        {
            var p = span[i];
            frame.SetPixel(i % w, i / w, new Pixel((byte)Math.Min(255, p.R + c), (byte)Math.Min(255, p.G + c), (byte)Math.Min(255, p.B + c)));
        }
    }
}
