using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Ambient;

/// <summary>Paints the whole panel for <see cref="SolidColorApp"/>.</summary>
internal sealed class MoodField : Node
{
    private readonly SolidColorApp _app;
    private readonly ParticleSystem _sparkles = new(256, 64, 240, new Random(7));
    private readonly Emitter _twinkle;
    private float _r, _g, _b;
    private bool _primed;
    private double _t;
    private float _dt;

    public MoodField(SolidColorApp app)
    {
        _app = app;
        _twinkle = _sparkles.Add(new Emitter
        {
            X = 0, Y = 0, Width = 255, Height = 63,
            Rate = 0f,
            LifetimeMin = 0.5f, LifetimeMax = 1.4f,
            Gradient = [Pixel.White, Pixel.White],
            AlphaStart = 1f, AlphaEnd = 0f,
            Size = 2f,
        });
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        var look = _app.Current;
        _dt = (float)Math.Min(ctx.Delta.TotalSeconds, 0.1);
        _t = ctx.Time.TotalSeconds;

        // Fade to a newly chosen colour (about 150 ms time constant); the first frame snaps so a fresh start is exact.
        if (!_primed)
        {
            _r = look.Color.R; _g = look.Color.G; _b = look.Color.B;
            _primed = true;
        }
        else
        {
            float k = 1f - MathF.Exp(-_dt / 0.15f);
            _r += (look.Color.R - _r) * k;
            _g += (look.Color.G - _g) * k;
            _b += (look.Color.B - _b) * k;
            if (MathF.Abs(look.Color.R - _r) < 0.5f) _r = look.Color.R;
            if (MathF.Abs(look.Color.G - _g) < 0.5f) _g = look.Color.G;
            if (MathF.Abs(look.Color.B - _b) < 0.5f) _b = look.Color.B;
        }

        if (look.Mode == "Sparkle")
        {
            _twinkle.Gradient[0] = Pixel.White;
            _twinkle.Gradient[1] = Pixel.Lerp(Current(), Pixel.White, 0.55f);
            _twinkle.Rate = 75f * look.Speed;
            _sparkles.Update(_dt);
        }
        else if (_sparkles.Count > 0)
        {
            _sparkles.Clear();
        }
    }

    private Pixel Current() => new((byte)(_r + 0.5f), (byte)(_g + 0.5f), (byte)(_b + 0.5f));

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var look = _app.Current;
        var c = Current();
        float lvl = look.Brightness;
        float t = (float)_t, sp = look.Speed;

        switch (look.Mode)
        {
            case "Breathing":
            {
                // Ease in and out like a slow breath, never fully dark so the colour stays readable.
                float phase = (t * sp / 7f) % 1f;
                float x = 0.5f - 0.5f * MathF.Cos(phase * MathF.PI * 2f);
                float level = 0.22f + 0.78f * Gfx.Smooth(x);
                frame.Fill(bounds, c.WithBrightness(level * lvl));
                break;
            }
            case "Gradient Drift":
                DrawDrift(frame, bounds, c, look, t);
                break;
            case "Color Cycle":
                DrawCycle(frame, bounds, c, look, t);
                break;
            case "Sparkle":
            {
                float breathe = 0.5f + 0.5f * MathF.Sin(t * 0.6f * sp);
                frame.Fill(bounds, c.WithBrightness((0.45f + 0.15f * breathe) * lvl));
                _sparkles.Render(frame);
                if (lvl < 1f) DimTo(frame, bounds, lvl);
                break;
            }
            case "Candle":
                DrawCandle(frame, bounds, c, t, sp, lvl);
                break;
            case "Aurora":
                DrawAurora(frame, bounds, c, look, t);
                break;
            default:
                frame.Fill(bounds, c.WithBrightness(lvl));
                break;
        }
    }

    private static void DimTo(FrameBuffer frame, Rectangle bounds, float lvl)
    {
        // Sparkle draws particles over the fill; only the overlay needs scaling when the master brightness is below 100%.
        // Re-scaling the full frame keeps the code simple and runs only when the user dims the panel.
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
                frame.SetPixel(x, y, frame.GetPixel(x, y).WithBrightness(lvl));
    }

    private static void BaseHsv(Pixel c, out float h, out float s, out float v)
    {
        Gfx.ToHsv(c, out h, out s, out v);
        if (s < 0.25f) s = 0.9f; // a grey colour has no hue to drift, so give it some
        if (v < 0.35f) v = 0.35f;
    }

    private static void DrawDrift(FrameBuffer frame, Rectangle bounds, Pixel c, SolidColorApp.Look look, float t)
    {
        BaseHsv(c, out var h, out var s, out var v);
        float spread = look.Spread;
        for (int x = 0; x < bounds.Width; x++)
        {
            float u = x / (float)bounds.Width;
            float wave = MathF.Sin((u * 1.3f + t * 0.05f * look.Speed) * MathF.PI * 2f);
            float hue = h + spread * wave;
            float val = v * (0.82f + 0.18f * MathF.Sin((u * 2.1f - t * 0.08f * look.Speed) * MathF.PI * 2f));
            frame.Fill(new Rectangle(bounds.X + x, bounds.Y, 1, bounds.Height), Gfx.Hsv(hue, s, val * look.Brightness));
        }
    }

    private static void DrawCycle(FrameBuffer frame, Rectangle bounds, Pixel c, SolidColorApp.Look look, float t)
    {
        BaseHsv(c, out var h, out var s, out var v);
        float hue = h + t * 10f * look.Speed;
        for (int y = 0; y < bounds.Height; y++)
        {
            // A slight vertical tilt of hue keeps the cycle from feeling like a flat flash.
            float tilt = (y / (float)bounds.Height - 0.5f) * look.Spread * 0.35f;
            frame.Fill(new Rectangle(bounds.X, bounds.Y + y, bounds.Width, 1), Gfx.Hsv(hue + tilt, s, v * look.Brightness));
        }
    }

    private static void DrawCandle(FrameBuffer frame, Rectangle bounds, Pixel c, float t, float sp, float lvl)
    {
        float n = Gfx.Noise1(t * 7f * sp) * 0.5f + Gfx.Noise1(t * 17f * sp, 11) * 0.3f + Gfx.Noise1(t * 3f * sp, 5) * 0.2f;
        float flicker = 0.62f + 0.38f * n;
        for (int y = 0; y < bounds.Height; y++)
        {
            // A little warmer and brighter towards the bottom, like a flame pooling light.
            float rowBias = 0.78f + 0.22f * (y / (float)(bounds.Height - 1));
            var color = Pixel.Lerp(c, new Pixel(255, 150, 40), 0.18f * (y / (float)bounds.Height));
            frame.Fill(new Rectangle(bounds.X, bounds.Y + y, bounds.Width, 1), color.WithBrightness(flicker * rowBias * lvl));
        }
    }

    private static void DrawAurora(FrameBuffer frame, Rectangle bounds, Pixel c, SolidColorApp.Look look, float t)
    {
        BaseHsv(c, out var h, out var s, out var v);
        float sp = look.Speed;
        for (int x = 0; x < bounds.Width; x++)
        {
            float u = x / (float)bounds.Width;
            float y0 = 28f + 12f * MathF.Sin(u * 7f + t * 0.35f * sp) + 7f * MathF.Sin(u * 17f - t * 0.23f * sp);
            float hue = h + look.Spread * MathF.Sin(u * 5f + t * 0.17f * sp);
            float pulse = 0.65f + 0.35f * MathF.Sin(u * 31f + t * 0.7f * sp);
            for (int y = 0; y < bounds.Height; y++)
            {
                float d = (y - y0) / 20f;
                float q = 1f - d * d;
                float band = q > 0f ? q * q : 0f;
                float val = v * (0.03f + 0.97f * band * pulse) * look.Brightness;
                frame.SetPixel(bounds.X + x, bounds.Y + y, Gfx.Hsv(hue + band * 20f, s, val));
            }
        }
    }
}
