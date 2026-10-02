using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Ambient;

/// <summary>
/// The living scene behind the clock. One node, six scenes (aurora, starfield with shooting stars, embers, waves,
/// a day and night sky that follows the hour, and a quiet minimal glow). Scenes are kept dim so the clock stays the hero.
/// Everything is drawn with the canvas API and the particle system; nothing allocates per frame.
/// </summary>
internal sealed class HomeBackdrop : Node
{
    private const int StarCount = 96;
    private const int CloudCount = 6;

    private readonly HomeState _s;
    private readonly float[] _starX = new float[StarCount], _starY = new float[StarCount], _starPhase = new float[StarCount], _starDepth = new float[StarCount];
    private readonly ParticleSystem _embers = new(256, 64, 300, new Random(42));
    private readonly Emitter _emberRise, _emberDust;
    private HomeMode _mode;
    private bool _emberWarm;
    private bool _first = true;

    public HomeBackdrop(HomeState state)
    {
        _s = state;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;

        for (int i = 0; i < StarCount; i++)
        {
            _starX[i] = Gfx.Hash(i * 3 + 1) * 256f;
            _starY[i] = Gfx.Hash(i * 3 + 2) * 62f;
            _starPhase[i] = Gfx.Hash(i * 3 + 3) * 6.283f;
            _starDepth[i] = i % 3; // 0 = far and dim, 2 = near and bright
        }

        _emberRise = _embers.Add(new Emitter
        {
            X = -4, Y = 66, Width = 264, Height = 0,
            Rate = 28f,
            LifetimeMin = 2.5f, LifetimeMax = 5f,
            SpeedMin = 6f, SpeedMax = 22f,
            Angle = -90f, Spread = 70f,
            GravityY = -3f, GravityX = 2f,
            Gradient = [Pixel.White, Pixel.White, Pixel.Black],
            AlphaStart = 0.95f, AlphaEnd = 0f,
            Size = 1f,
        });
        _emberDust = _embers.Add(new Emitter
        {
            X = 0, Y = 64, Width = 256, Height = 0,
            Rate = 5f,
            LifetimeMin = 4f, LifetimeMax = 7f,
            SpeedMin = 8f, SpeedMax = 14f,
            Angle = -90f, Spread = 30f,
            Gradient = [Pixel.White, Pixel.White],
            AlphaStart = 0.8f, AlphaEnd = 0f,
            Size = 2f,
        });
    }

    public HomeMode Mode => _mode;

    /// <summary>Switches scene with a quick fade out and back in. The very first call just sets it.</summary>
    public void RequestMode(HomeMode mode)
    {
        if (_first || Host is null)
        {
            _first = false;
            _mode = mode;
            _s.Mode = mode;
            return;
        }

        if (mode == _mode) return;
        AnimateOpacity(0f, TimeSpan.FromMilliseconds(260), Core.Animation.Easing.InQuad, () =>
        {
            _mode = mode;
            _s.Mode = mode;
            _emberWarm = false;
            AnimateOpacity(1f, TimeSpan.FromMilliseconds(700), Core.Animation.Easing.OutQuad);
        });
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_mode == HomeMode.Embers)
        {
            if (!_emberWarm)
            {
                // Pre-simulate so the first embers frame is already full of drifting sparks.
                _embers.Clear();
                for (int i = 0; i < 60; i++) _embers.Update(0.1f);
                _emberWarm = true;
            }

            var pal = _s.Pal;
            _emberRise.Gradient[0] = pal.A3;
            _emberRise.Gradient[1] = pal.A1;
            _emberRise.Gradient[2] = Gfx.Dim(pal.A2, 0.35f);
            _emberDust.Gradient[0] = pal.Star;
            _emberDust.Gradient[1] = pal.A3;
            _embers.Update(_s.Dt * _s.Speed);
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        switch (_mode)
        {
            case HomeMode.Starfield: DrawStarfield(frame); break;
            case HomeMode.Embers: DrawEmbers(frame); break;
            case HomeMode.Waves: DrawWaves(frame); break;
            case HomeMode.DaySky: DrawDaySky(frame); break;
            case HomeMode.Minimal: DrawMinimal(frame); break;
            default: DrawAurora(frame); break;
        }
    }

    // ---- Aurora: three drifting curtains of light over a faint star field ------------------------------------------------

    private void DrawAurora(FrameBuffer frame)
    {
        var p = _s.Pal;
        Gfx.VerticalGradient(frame, new Rectangle(0, 0, 256, 64), p.Sky0, p.Sky1);
        DrawStars(frame, p.Star, 0.55f, 0f);

        float t = _s.T * _s.Speed;
        for (int layer = 0; layer < 3; layer++)
        {
            var color = layer == 0 ? p.A1 : layer == 1 ? p.A2 : p.A3;
            float baseY = 26f + layer * 5f;
            float speed = 0.33f - layer * 0.07f;
            float k1 = 0.022f + layer * 0.006f, k2 = 0.061f + layer * 0.011f;
            float strength = layer == 0 ? 0.85f : layer == 1 ? 0.6f : 0.45f;

            for (int x = 0; x < 256; x += 1)
            {
                float y0 = baseY + 11f * MathF.Sin(x * k1 + t * speed + layer * 2f) + 6f * MathF.Sin(x * k2 - t * speed * 1.7f);
                // Vertical rays: brightness varies quickly along x, slowly in time, which gives the curtain its fibres.
                float ray = 0.55f + 0.45f * MathF.Sin(x * 0.37f + t * 0.8f + layer * 1.7f) * MathF.Sin(x * 0.11f - t * 0.31f);
                float swell = 0.65f + 0.35f * MathF.Sin(x * 0.015f + t * 0.21f + layer);
                int top = (int)MathF.Max(0f, y0 - 20f), bottom = (int)MathF.Min(63f, y0 + 14f);
                for (int y = top; y <= bottom; y++)
                {
                    float d = y - y0;
                    // Sharp bright lower edge, long soft fade upward.
                    float q = d < 0f ? 1f - d * d / 400f : 1f - d * d / 196f;
                    if (q <= 0f) continue;
                    Gfx.Add(frame, x, y, color, q * q * ray * swell * strength);
                }
            }
        }
    }

    // ---- Starfield: parallax layers, twinkle and the odd shooting star -------------------------------------------------------

    private void DrawStarfield(FrameBuffer frame)
    {
        var p = _s.Pal;
        Gfx.VerticalGradient(frame, new Rectangle(0, 0, 256, 64), Gfx.Mix(p.Sky0, Pixel.Black, 0.4f), Gfx.Mix(p.Sky1, p.Sky0, 0.5f));
        // A faint band of galaxy light slowly turning behind the stars.
        float t = _s.T * _s.Speed;
        Gfx.GlowEllipse(frame, 128f + 60f * MathF.Sin(t * 0.04f), 30f + 8f * MathF.Sin(t * 0.07f), 150f, 14f, Gfx.Dim(p.A3, 0.5f), 0.22f);
        Gfx.GlowEllipse(frame, 60f + 40f * MathF.Cos(t * 0.05f), 36f, 90f, 10f, Gfx.Dim(p.A2, 0.5f), 0.18f);
        DrawStars(frame, p.Star, 1f, _s.T * _s.Speed);
        DrawShootingStar(frame, p, _s.T);
    }

    private void DrawStars(FrameBuffer frame, Pixel color, float brightness, float drift)
    {
        for (int i = 0; i < StarCount; i++)
        {
            float depth = _starDepth[i];
            float speed = 1.2f + depth * 2.6f; // pixels per second; nearer stars move faster
            float x = (_starX[i] - drift * speed) % 256f;
            if (x < 0f) x += 256f;
            float tw = 0.55f + 0.45f * MathF.Sin(_s.T * (0.8f + depth * 0.5f) + _starPhase[i]);
            float a = (0.28f + depth * 0.3f) * tw * brightness;
            int ix = (int)x;
            float fx = x - ix;
            int y = (int)_starY[i];
            frame.BlendPixel(ix, y, color, a * (1f - fx));
            frame.BlendPixel(ix + 1, y, color, a * fx);
            if (depth >= 2f && tw > 0.85f)
            {
                // The brightest stars get a tiny cross when they peak.
                frame.BlendPixel(ix - 1, y, color, a * 0.3f);
                frame.BlendPixel(ix + 2, y, color, a * 0.3f);
                frame.BlendPixel(ix, y - 1, color, a * 0.3f);
                frame.BlendPixel(ix, y + 1, color, a * 0.3f);
            }
        }
    }

    private static void DrawShootingStar(FrameBuffer frame, HomePalette p, float t)
    {
        const float period = 6.5f, life = 1.1f;
        int epoch = (int)(t / period);
        float local = t - epoch * period - Gfx.Hash(epoch * 7 + 1) * (period - life - 0.5f);
        if (local < 0f || local > life) return;

        float u = local / life;
        float sx = 40f + Gfx.Hash(epoch * 7 + 2) * 200f, sy = 2f + Gfx.Hash(epoch * 7 + 3) * 22f;
        float angle = (18f + Gfx.Hash(epoch * 7 + 4) * 24f) * MathF.PI / 180f;
        float dir = Gfx.Hash(epoch * 7 + 5) > 0.5f ? 1f : -1f;
        float dx = MathF.Cos(angle) * dir, dy = MathF.Sin(angle);
        float dist = 90f * Core.Animation.Easing.OutQuad(u);
        float fade = u < 0.15f ? u / 0.15f : 1f - Gfx.Smooth((u - 0.5f) * 2f > 0f ? (u - 0.5f) * 2f : 0f);
        for (int i = 0; i < 14; i++)
        {
            float back = i * 2.6f;
            float px = sx + dx * (dist - back), py = sy + dy * (dist - back);
            float a = (1f - i / 14f) * fade;
            var c = i < 2 ? Pixel.White : Gfx.Mix(p.Star, p.A2, i / 14f);
            frame.BlendPixel((int)MathF.Round(px), (int)MathF.Round(py), c, a);
            if (i < 4) frame.BlendPixel((int)MathF.Round(px), (int)MathF.Round(py) + 1, c, a * 0.4f);
        }
    }

    // ---- Embers: warm light pooling at the bottom, sparks rising ---------------------------------------------------------------

    private void DrawEmbers(FrameBuffer frame)
    {
        var p = _s.Pal;
        Gfx.VerticalGradient(frame, new Rectangle(0, 0, 256, 64), Gfx.Mix(p.Sky0, Pixel.Black, 0.5f), Gfx.Mix(p.Sky1, p.Sky0, 0.35f));
        float t = _s.T * _s.Speed;
        float breathe = 0.7f + 0.3f * MathF.Sin(t * 0.5f);
        Gfx.GlowEllipse(frame, 128f + 30f * MathF.Sin(t * 0.13f), 70f, 190f, 34f, Gfx.Dim(p.A1, 0.7f), 0.48f * breathe);
        Gfx.GlowEllipse(frame, 70f + 40f * MathF.Sin(t * 0.09f + 2f), 68f, 90f, 22f, Gfx.Dim(p.A2, 0.6f), 0.32f);
        _embers.Render(frame);
    }

    // ---- Waves: four layers of slow swell, back to front ---------------------------------------------------------------------------

    private void DrawWaves(FrameBuffer frame)
    {
        var p = _s.Pal;
        Gfx.VerticalGradient(frame, new Rectangle(0, 0, 256, 64), Gfx.Mix(p.Sky0, Pixel.Black, 0.2f), p.Sky1);
        DrawStars(frame, p.Star, 0.35f, 0f);
        float t = _s.T * _s.Speed;

        for (int layer = 0; layer < 4; layer++)
        {
            float baseY = 36f + layer * 7f;
            float amp = 3.2f + layer * 0.7f;
            float w1 = 0.45f + layer * 0.17f, w2 = 0.31f + layer * 0.11f;
            float k1 = 0.035f + layer * 0.008f, k2 = 0.09f - layer * 0.01f;
            float dir = layer % 2 == 0 ? 1f : -1f;
            var body = Gfx.Dim(Gfx.Mix(p.Sky1, layer % 2 == 0 ? p.A1 : p.A2, 0.30f + layer * 0.13f), 0.55f + layer * 0.11f);
            var crest = Gfx.Mix(body, p.A3, 0.7f);
            var foam = Gfx.Mix(crest, Pixel.White, 0.35f);

            for (int x = 0; x < 256; x++)
            {
                float y = baseY + amp * MathF.Sin(x * k1 + dir * t * w1 + layer) + amp * 0.45f * MathF.Sin(x * k2 - dir * t * w2);
                int iy = (int)MathF.Round(y);
                if (iy >= 64) continue;
                frame.SetPixel(x, iy, foam);
                frame.SetPixel(x, iy + 1, crest);
                if (iy + 2 < 64) frame.Fill(new Rectangle(x, iy + 2, 1, 64 - iy - 2), body);
            }
        }
    }

    // ---- Day / night sky ---------------------------------------------------------------------------------------------------------

    private static readonly float[] KeyHour = [0f, 4.5f, 6.5f, 9f, 12f, 16f, 18.5f, 20f, 22f, 24f];

    private static readonly Pixel[] KeyTop =
    [
        new(3, 5, 24), new(10, 12, 56), new(52, 66, 150), new(40, 120, 225), new(26, 104, 220), new(40, 112, 215), new(64, 46, 128), new(16, 12, 56), new(4, 6, 28), new(3, 5, 24),
    ];

    private static readonly Pixel[] KeyBottom =
    [
        new(10, 14, 50), new(64, 36, 96), new(255, 140, 92), new(150, 205, 250), new(110, 180, 250), new(150, 196, 245), new(255, 108, 60), new(112, 48, 88), new(12, 16, 54), new(10, 14, 50),
    ];

    private void DrawDaySky(FrameBuffer frame)
    {
        float h = _s.Hour;
        int seg = 0;
        while (seg < KeyHour.Length - 2 && h >= KeyHour[seg + 1]) seg++;
        float f = Gfx.Saturate((h - KeyHour[seg]) / (KeyHour[seg + 1] - KeyHour[seg]));
        f = Gfx.Smooth(f);
        var top = Pixel.Lerp(KeyTop[seg], KeyTop[seg + 1], f);
        var bottom = Pixel.Lerp(KeyBottom[seg], KeyBottom[seg + 1], f);
        // Keep the daytime sky well below full brightness so a white clock still reads.
        const float dayDim = 0.62f;
        Gfx.VerticalGradient(frame, new Rectangle(0, 0, 256, 64), Gfx.Dim(top, dayDim), Gfx.Dim(bottom, dayDim));

        float night = 1f - Gfx.Smooth(Gfx.Saturate((h - 4.5f) / 2f)) + Gfx.Smooth(Gfx.Saturate((h - 19f) / 2.5f));
        night = Gfx.Saturate(night);
        if (night > 0.02f) DrawStars(frame, _s.Pal.Star, night * 0.9f, _s.T * _s.Speed * 0.3f);

        // Sun rides an arc from 06:00 to 18:00, moon from 18:00 to 06:00.
        float sunT = (h - 6f) / 12f;
        if (sunT is > -0.08f and < 1.08f)
        {
            float sx = 14f + Gfx.Saturate(sunT) * 228f;
            float sy = 58f - MathF.Sin(Gfx.Saturate(sunT) * MathF.PI) * 44f;
            float low = 1f - MathF.Sin(Gfx.Saturate(sunT) * MathF.PI);
            var warm = Gfx.Mix(new Pixel(255, 244, 205), new Pixel(255, 120, 40), low);
            Gfx.GlowDisc(frame, sx, sy, 22f, warm, 0.55f);
            Gfx.GlowDisc(frame, sx, sy, 9f, Gfx.Mix(warm, Pixel.White, 0.4f), 0.8f);
            Gfx.Disc(frame, (int)sx, (int)sy, 3, Gfx.Mix(warm, Pixel.White, 0.6f));
        }

        float moonT = (h >= 18f ? h - 18f : h + 6f) / 12f;
        if (night > 0.3f)
        {
            float mx = 14f + Gfx.Saturate(moonT) * 228f;
            float my = 58f - MathF.Sin(Gfx.Saturate(moonT) * MathF.PI) * 42f;
            Gfx.GlowDisc(frame, mx, my, 14f, new Pixel(120, 140, 200), 0.3f * night);
            Gfx.Disc(frame, (int)mx, (int)my, 4, new Pixel(224, 232, 250));
            frame.SetPixel((int)mx - 1, (int)my - 1, new Pixel(176, 188, 214));
            frame.SetPixel((int)mx + 1, (int)my + 1, new Pixel(190, 200, 224));
        }

        // Clouds by day, a few very faint ones at night.
        float cloudAlpha = 0.22f * (1f - night * 0.65f);
        for (int i = 0; i < CloudCount; i++)
        {
            float speed = 2.5f + i * 0.9f;
            float cx = (Gfx.Hash(i + 90) * 330f + _s.T * _s.Speed * speed) % 330f - 40f;
            float cy = 8f + Gfx.Hash(i + 120) * 30f;
            float rx = 22f + Gfx.Hash(i + 150) * 16f, ry = 3.5f + Gfx.Hash(i + 180) * 3f;
            var tint = Gfx.Mix(Pixel.White, bottom, 0.45f + night * 0.3f);
            BlendEllipse(frame, cx, cy, rx, ry, tint, cloudAlpha);
            BlendEllipse(frame, cx - rx * 0.35f, cy - ry * 0.6f, rx * 0.55f, ry * 0.9f, tint, cloudAlpha * 0.9f);
        }
    }

    private static void BlendEllipse(FrameBuffer frame, float cx, float cy, float rx, float ry, Pixel color, float alpha)
    {
        int x0 = (int)MathF.Floor(cx - rx), x1 = (int)MathF.Ceiling(cx + rx);
        int y0 = (int)MathF.Floor(cy - ry), y1 = (int)MathF.Ceiling(cy + ry);
        float ix = 1f / (rx * rx), iy = 1f / (ry * ry);
        for (int y = Math.Max(y0, 0); y <= Math.Min(y1, 63); y++)
        {
            float dy = y - cy;
            for (int x = Math.Max(x0, 0); x <= Math.Min(x1, 255); x++)
            {
                float dx = x - cx;
                float q = 1f - (dx * dx * ix + dy * dy * iy);
                if (q <= 0f) continue;
                frame.BlendPixel(x, y, color, MathF.Min(1f, q * 1.6f) * alpha);
            }
        }
    }

    // ---- Minimal: deep gradient and a slow breathing glow behind the clock -------------------------------------------------------

    private void DrawMinimal(FrameBuffer frame)
    {
        var p = _s.Pal;
        Gfx.VerticalGradient(frame, new Rectangle(0, 0, 256, 64), Gfx.Dim(p.Sky0, 0.8f), Gfx.Mix(p.Sky0, p.Sky1, 0.55f));
        float t = _s.T * _s.Speed;
        float breathe = 0.5f + 0.5f * MathF.Sin(t * 0.55f);
        Gfx.GlowEllipse(frame, 78f, 32f, 120f, 30f, Gfx.Dim(p.A1, 0.7f), 0.20f + 0.12f * breathe);
        Gfx.GlowEllipse(frame, 200f + 10f * MathF.Sin(t * 0.2f), 34f, 70f, 22f, Gfx.Dim(p.A2, 0.7f), 0.12f + 0.08f * (1f - breathe));
        DrawStars(frame, p.Star, 0.25f, 0f);
    }
}
