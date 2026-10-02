using System.Globalization;
using System.Numerics;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Ambient;

/// <summary>Confetti layer drawn above everything else. Bursts are fired by the countdown view.</summary>
internal sealed class ConfettiLayer : Node
{
    private readonly ParticleSystem _sys = new(256, 64, 420, new Random(2026));
    private readonly Emitter _left, _right, _mid;
    private float _dt;

    public ConfettiLayer()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        _left = _sys.Add(Make(10f, 62f, -62f));
        _right = _sys.Add(Make(245f, 62f, -118f));
        _mid = _sys.Add(Make(128f, 66f, -90f));
        _mid.SpeedMin = 50f;
        _mid.SpeedMax = 120f;
        _mid.Spread = 150f;
    }

    private static Emitter Make(float x, float y, float angle)
    {
        var e = ParticlePresets.Confetti(x, y);
        e.Angle = angle;
        e.Spread = 70f;
        e.SpeedMin = 70f;
        e.SpeedMax = 140f;
        e.LifetimeMin = 1.8f;
        e.LifetimeMax = 3.2f;
        e.Size = 2f;
        e.AlphaEnd = 0.7f;
        return e;
    }

    public int Count => _sys.Count;

    public void Burst(int perEmitter)
    {
        _sys.Burst(_left, perEmitter);
        _sys.Burst(_right, perEmitter);
        _sys.Burst(_mid, perEmitter);
    }

    public void Clear() => _sys.Clear();

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_sys.Count > 0) _sys.Update((float)Math.Min(ctx.Delta.TotalSeconds, 0.1));
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_sys.Count > 0) _sys.Render(frame);
    }
}

/// <summary>
/// Background, progress ring, label row and the per-frame logic of the countdown: clock source, urgency colours, completion.
/// Owns the digit strips (children of the same panel) and drives them each frame.
/// </summary>
internal sealed class CountdownView : Node
{
    private const float RingCx = 31f, RingCy = 32f, RingOuter = 29.5f, RingInner = 23.5f;

    private readonly CountdownTimerApp _app;
    private readonly DigitStrip _strip, _days;
    private readonly ConfettiLayer _confetti;
    private readonly GlyphLine _label = new(), _small = new();
    private readonly BdfFontParser.BdfFont _tiny = Fonts.QuiteSmall, _smallFont = Fonts.Small;
    private readonly Tween<float> _flash = new(0f), _celebrate = new(0f);

    // Ring pixels with their angle (0 = 12 o'clock, clockwise, 0-1) and edge coverage.
    private readonly short[] _rx, _ry;
    private readonly float[] _ang, _cov;

    private TimeSpan _start, _now, _completedAt, _lastBurst;
    private bool _started, _complete;
    private int _appliedMinutes = -1;
    private string? _appliedTarget;
    private DateTimeOffset _target;
    private bool _hasTarget;
    private double _total = 1, _remaining;
    private string _labelText = "";
    private string _shownLabel = "";

    public CountdownView(CountdownTimerApp app, DigitStrip strip, DigitStrip days, ConfettiLayer confetti)
    {
        _app = app;
        _strip = strip;
        _days = days;
        _confetti = confetti;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;

        var xs = new List<short>();
        var ys = new List<short>();
        var angs = new List<float>();
        var covs = new List<float>();
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float dx = x - RingCx, dy = y - RingCy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float cov = Gfx.Saturate(MathF.Min(RingOuter - d, d - RingInner) + 0.5f);
                if (cov <= 0f) continue;
                float a = MathF.Atan2(dx, -dy) / (2f * MathF.PI);
                if (a < 0f) a += 1f;
                xs.Add((short)x); ys.Add((short)y); angs.Add(a); covs.Add(cov);
            }
        }
        _rx = xs.ToArray(); _ry = ys.ToArray(); _ang = angs.ToArray(); _cov = covs.ToArray();
    }

    public bool IsComplete => _complete;
    public double Remaining => _remaining;
    public int ConfettiCount => _confetti.Count;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        var host = Host!;
        _now = ctx.Time;
        var wall = host.Time.GetLocalNow();

        string target = (_app.Target ?? "").Trim();
        if (!_started || _appliedMinutes != _app.DurationMinutes || !string.Equals(target, _appliedTarget, StringComparison.Ordinal))
        {
            Restart(host, ctx.Time, wall, target);
        }

        _remaining = _hasTarget ? (_target - wall).TotalSeconds : _total - (ctx.Time - _start).TotalSeconds;
        if (_remaining <= 0.0)
        {
            _remaining = 0.0;
            if (!_complete) Complete(host, ctx.Time);
            if (_app.AutoRestart && !_hasTarget && ctx.Time - _completedAt > TimeSpan.FromSeconds(9))
                Restart(host, ctx.Time, wall, target);
            else if (_app.Celebrate && ctx.Time - _lastBurst > TimeSpan.FromMilliseconds(2400))
            {
                _lastBurst = ctx.Time;
                _confetti.Burst(26);
            }
        }

        int secs = (int)Math.Ceiling(_remaining);
        UpdateStrips(secs);
        ApplyPaint(ctx);
    }

    private void Restart(UiHost host, TimeSpan frameTime, DateTimeOffset wall, string target)
    {
        _started = true;
        _complete = false;
        _appliedMinutes = _app.DurationMinutes;
        _appliedTarget = target;
        _start = frameTime;
        _flash.Set(0f);
        _celebrate.Set(0f);
        _confetti.Clear();
        _hasTarget = CountdownTimerApp.TryParseTarget(target, wall, out _target);
        _total = _hasTarget ? Math.Max(1.0, (_target - wall).TotalSeconds) : Math.Max(1, _app.DurationMinutes) * 60.0;
        _labelText = (_app.Label ?? "").Trim().ToUpperInvariant();
    }

    private void Complete(UiHost host, TimeSpan frameTime)
    {
        _complete = true;
        _completedAt = frameTime;
        _lastBurst = frameTime;
        _flash.Set(1f);
        host.Animator.Animate(_flash, 0f, TimeSpan.FromMilliseconds(700), Easing.OutCubic);
        host.Animator.Animate(_celebrate, 1f, TimeSpan.FromMilliseconds(500), Easing.OutBack);
        if (_app.Celebrate) _confetti.Burst(90);
    }

    private void UpdateStrips(int secs)
    {
        int daysLeft = secs / 86400;
        bool showHours = secs >= 3600;
        string pattern = showHours ? "dd:dd:dd" : "dd:dd";
        bool hasLabel = _complete || _labelText.Length > 0;
        int scale = showHours || hasLabel ? 2 : 3;

        _strip.Pattern = pattern;
        _strip.Scale = scale;
        int rest = daysLeft > 0 ? secs % 86400 : secs;
        if (showHours)
        {
            _strip.SetTwo(0, rest / 3600);
            _strip.SetTwo(2, rest / 60 % 60);
            _strip.SetTwo(4, rest % 60);
        }
        else
        {
            _strip.SetTwo(0, rest / 60);
            _strip.SetTwo(2, rest % 60);
        }

        _days.Visible = daysLeft > 0;
        if (daysLeft > 0) _days.SetTwo(0, daysLeft);
    }

    private (Pixel main, float warn, float crit) Colours(Pixel baseColor)
    {
        float warn = Gfx.Saturate((float)((60.0 - _remaining) / 50.0));
        float crit = _remaining <= 10.0 && !_complete ? 1f : 0f;
        var c = Gfx.HueMix(baseColor, new Pixel(255, 170, 20), warn);
        c = Gfx.HueMix(c, new Pixel(255, 40, 30), crit);
        return (c, warn, crit);
    }

    private void ApplyPaint(FrameContext ctx)
    {
        var baseColor = NamedColors.Resolve(_app.TextColor, new Pixel(30, 235, 255));
        var (main, _, crit) = Colours(baseColor);
        float t = (float)ctx.Time.TotalSeconds;
        float f = (float)(_remaining - Math.Floor(_remaining)); // 1 right after a tick, falling to 0

        TextPaint paint;
        if (_complete)
        {
            paint = TextPaint.Rainbow(t * 140f, 1.6f, 0.85f, 1f);
            paint.Alpha = MathF.Sin(t * 9f) > 0f ? 1f : 0.85f;
        }
        else
        {
            paint = TextPaint.Vertical(Gfx.Mix(main, Pixel.White, 0.5f), main);
        }
        paint.Shadow = true;
        paint.ShadowColor = Gfx.Dim(main, 0.12f);
        paint.Bold = true;
        paint.Glow = crit > 0f ? 0.18f + 0.2f * f : 0.1f;
        _strip.Paint = paint;
        _strip.ColonAlpha = _complete ? 1f : 0.3f + 0.7f * (1f - f) * (1f - f);
        // Last ten seconds: every tick kicks the digits up a few pixels.
        _strip.Lift = crit > 0f ? 3f * f * f * f : 0f;

        bool hasLabel = _complete || _labelText.Length > 0;
        int w = _strip.TotalWidth;
        _strip.Position = new Vector2(68f + (182f - w) / 2f, hasLabel ? 8f : 0f);

        var dp = TextPaint.Solid(Gfx.Mix(main, Pixel.White, 0.6f));
        dp.Shadow = true;
        dp.Bold = true;
        _days.Paint = dp;
        _days.Position = new Vector2(RingCx - _days.TotalWidth / 2f, -4f);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var baseColor = NamedColors.Resolve(_app.TextColor, new Pixel(30, 235, 255));
        var (main, warn, crit) = Colours(baseColor);
        float t = (float)_now.TotalSeconds;
        float f = (float)(_remaining - Math.Floor(_remaining));
        var bg = NamedColors.Background(_app.BackgroundColor);

        frame.Fill(bounds, bg);

        // Soft light behind the digits that follows the urgency colour and throbs once a second near the end.
        float throb = crit > 0f ? 0.5f + 0.5f * f : 0.35f + 0.1f * MathF.Sin(t * 1.2f);
        if (_complete) main = Pixel.FromHsv(t * 140f, 0.8f, 1f);
        Gfx.GlowEllipse(frame, 160f, 32f, 120f, 34f, Gfx.Dim(main, 0.5f), 0.34f * throb);

        DrawRing(frame, main, f, t);
        DrawLabel(frame, main, t);

        // Red heartbeat frame for the final ten seconds.
        if (crit > 0f) DrawFrameGlow(frame, new Pixel(255, 30, 20), f * f * 0.6f);
        if (_flash.Value > 0.01f) DrawFrameGlow(frame, Pixel.White, _flash.Value * 0.9f, true);
    }

    private static void DrawFrameGlow(FrameBuffer frame, Pixel c, float a, bool fullFill = false)
    {
        if (fullFill)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 256; x++)
                    frame.BlendPixel(x, y, c, a * 0.5f);
            return;
        }
        for (int i = 0; i < 5; i++)
        {
            float k = a * (1f - i / 5f);
            for (int x = 0; x < 256; x++) { frame.BlendPixel(x, i, c, k); frame.BlendPixel(x, 63 - i, c, k); }
            for (int y = 0; y < 64; y++) { frame.BlendPixel(i, y, c, k); frame.BlendPixel(255 - i, y, c, k); }
        }
    }

    private void DrawRing(FrameBuffer frame, Pixel main, float f, float t)
    {
        float progress = _complete ? 1f : Gfx.Saturate((float)(_remaining / _total));
        var track = Gfx.Dim(main, 0.13f);
        for (int i = 0; i < _rx.Length; i++)
        {
            float a = _ang[i];
            Pixel c;
            if (_complete)
            {
                c = Pixel.FromHsv(t * 140f + a * 360f, 0.85f, 1f);
            }
            else if (a <= progress)
            {
                // Brightest at the leading edge so the arc seems to chase the time away.
                float lead = Gfx.Saturate(1f - (progress - a) * 5f);
                c = Gfx.Mix(Gfx.Dim(main, 0.75f), Pixel.White, lead * 0.55f);
            }
            else c = track;
            frame.BlendPixel(_rx[i], _ry[i], c, _cov[i]);
        }

        if (!_complete && progress > 0.003f)
        {
            float ang = progress * 2f * MathF.PI;
            float hx = RingCx + MathF.Sin(ang) * 26.5f, hy = RingCy - MathF.Cos(ang) * 26.5f;
            Gfx.GlowDisc(frame, hx, hy, 6f, Gfx.Mix(main, Pixel.White, 0.4f), 0.55f);
        }

        // A ripple leaves the middle every second (every tick is a heartbeat), or a label for the day count.
        if (_days.Visible)
        {
            _small.Set(_tiny, "DAYS");
            var dp = TextPaint.Solid(Gfx.Dim(main, 0.9f));
            dp.Shadow = false;
            _small.Draw(frame, (int)(RingCx - _small.Width / 2f) + 1, 41, 1, dp);
        }
        else
        {
            float r = 2f + (1f - f) * 14f;
            Gfx.GlowDisc(frame, RingCx, RingCy, 4f, main, 0.9f);
            if (!_complete) Gfx.GlowDisc(frame, RingCx, RingCy, r, main, 0.35f * f);
        }
    }

    private void DrawLabel(FrameBuffer frame, Pixel main, float t)
    {
        bool done = _complete;
        string text = done ? "TIME'S UP!" : _labelText;
        if (text.Length == 0) return;

        if (!string.Equals(text, _shownLabel, StringComparison.Ordinal))
        {
            _shownLabel = text;
            _label.Set(_smallFont, _smallFont.TruncateWithEllipsis(text, 182));
        }

        var p = done
            ? TextPaint.Rainbow(t * 200f, 4f, 0.8f, 1f)
            : TextPaint.Solid(Gfx.Mix(main, Pixel.White, 0.25f));
        p.Shadow = true;
        p.Bold = true;
        if (done)
        {
            p.WaveAmp = 2f * _celebrate.Value;
            p.WavePhase = t * 8f;
            p.WaveStep = 0.7f;
        }
        int x = 68 + (182 - _label.Width) / 2;
        float slide = done ? (1f - _celebrate.Value) * 10f : 0f;
        _label.Draw(frame, x, 3 + (int)slide, 1, p);
    }
}

/// <summary>Helpers kept next to the view so the app file stays about settings.</summary>
internal static class CountdownParsing
{
    public static bool TryParse(string? text, DateTimeOffset now, out DateTimeOffset target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        bool hasDate = text.Contains('-') || text.Contains('/') || text.Contains('T') || text.Contains(' ');
        if (!hasDate && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var tod) && tod >= TimeSpan.Zero && tod < TimeSpan.FromDays(1))
        {
            var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset) + tod;
            target = today > now ? today : today.AddDays(1);
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            target = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), now.Offset);
            return true;
        }
        return false;
    }
}
