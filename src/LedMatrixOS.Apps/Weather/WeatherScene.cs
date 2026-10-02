using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Weather;

internal enum SceneMode { Weather, Loading, Offline }

/// <summary>What the scene should look like. Glow (dawn/dusk strength) is quantised by the caller so the palette does not retarget every frame.</summary>
internal readonly record struct SceneMood(SceneMode Mode, WeatherKind Kind, bool Day, float Glow, int Intensity);

/// <summary>
/// The animated backdrop: sky gradient, sun or moon, stars, drifting clouds, rain, snow, fog and lightning.
/// Every look change is a tween (colours, cloud cover, precipitation), so a condition change melts from one scene into the next.
/// All state lives in preallocated arrays and a pooled <see cref="ParticleSystem"/>; nothing allocates per frame.
/// </summary>
internal sealed class WeatherScene : Node
{
    private readonly record struct CloudSpec(float BaseY, float Scale, float Speed, float X0, float Depth);

    private static readonly CloudSpec[] Clouds =
    [
        new(40, 1.7f, -2.5f, 52, 1.0f), new(24, 1.2f, -1.6f, 120, 0.7f), new(54, 2.0f, -3.4f, 10, 1.0f), new(15, 0.9f, -1.1f, 80, 0.4f),
        new(36, 1.4f, -2.0f, 150, 0.6f), new(60, 1.5f, -2.8f, 100, 0.9f), new(22, 1.8f, -1.8f, 20, 0.8f), new(47, 1.1f, -1.3f, 140, 0.5f),
    ];

    private const int MaxStreaks = 110;
    private const float HeroX = 31f, HeroY = 29f;

    private readonly Func<SceneMood> _mood;
    private readonly ParticleSystem _ps;
    private readonly Emitter _farRain, _snowSmall, _snowLarge, _glints, _splash;

    private readonly Tween<Pixel> _top = new(Pixel.Black), _bottom = new(Pixel.Black);
    private readonly Tween<float> _cover = new(0f), _dark = new(0f), _celestial = new(0f), _stars = new(0f), _rain = new(0f),
        _snow = new(0f), _fog = new(0f), _night = new(0f), _hero = new(1f);

    private readonly float[] _sx = new float[MaxStreaks], _sy = new float[MaxStreaks], _sv = new float[MaxStreaks];
    private readonly int[] _sl = new int[MaxStreaks];
    private readonly int[] _bolt = new int[14];
    private uint _rng = 12345;
    private int _boltStrike = -1;

    private SceneMood _applied;
    private bool _hasMood;
    private float _t;
    private float _flash;

    public WeatherScene(Func<SceneMood> mood, int width = 152)
    {
        _mood = mood;
        Width = width;
        HAlign = Align.Start;
        VAlign = Align.Stretch;
        ClipChildren = true;

        _ps = new ParticleSystem(width, 64, 700, new Random(7));
        _farRain = _ps.Add(ParticlePresets.Rain(width, 64));
        _farRain.SpeedMin = 55; _farRain.SpeedMax = 75; _farRain.Gradient = [new Pixel(70, 110, 215)]; _farRain.AlphaStart = _farRain.AlphaEnd = 0.7f;
        _farRain.Rate = 0;
        _snowSmall = _ps.Add(ParticlePresets.Snow(width, 64));
        _snowSmall.SpeedMin = 12; _snowSmall.SpeedMax = 22; _snowSmall.LifetimeMin = 3.4f; _snowSmall.LifetimeMax = 5f; _snowSmall.Spread = 60;
        _snowSmall.Rate = 0;
        _snowLarge = _ps.Add(ParticlePresets.Snow(width, 64));
        _snowLarge.SpeedMin = 9; _snowLarge.SpeedMax = 15; _snowLarge.LifetimeMin = 4.4f; _snowLarge.LifetimeMax = 6.5f; _snowLarge.Size = 2; _snowLarge.Spread = 50;
        _snowLarge.AlphaStart = _snowLarge.AlphaEnd = 1f; _snowLarge.Rate = 0;
        _glints = _ps.Add(ParticlePresets.Sparkles(40, 40));
        _glints.X = 11; _glints.Y = 9; _glints.Rate = 0; _glints.LifetimeMin = 0.35f; _glints.LifetimeMax = 0.9f;
        _splash = _ps.Add(new Emitter
        {
            Rate = 0, LifetimeMin = 0.18f, LifetimeMax = 0.32f, SpeedMin = 10, SpeedMax = 26, Angle = -90, Spread = 130, GravityY = 110,
            Gradient = [new Pixel(170, 205, 255)], AlphaStart = 0.9f, AlphaEnd = 0f,
        });

        for (int i = 0; i < MaxStreaks; i++) Respawn(i, spread: true);
    }

    /// <summary>Plays the entrance: the sun or moon pops in with a little overshoot.</summary>
    public void Pop()
    {
        _hero.Set(0f);
        Host?.Animator.Animate(_hero, 1f, TimeSpan.FromMilliseconds(900), Easing.OutBack);
    }

    public bool IsLightning => _flash > 0.01f;
    public Pixel SkyTop => _top.Value;
    public Pixel SkyBottom => _bottom.Value;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _t = (float)ctx.Time.TotalSeconds;
        float dt = Math.Min((float)ctx.Delta.TotalSeconds, 0.1f);

        var mood = _mood();
        if (!_hasMood || mood != _applied) Apply(mood, first: !_hasMood);

        float rain = _rain.Value;
        _farRain.Rate = rain * 55f;
        _snowSmall.Rate = _snow.Value * 38f;
        _snowLarge.Rate = _snow.Value * 9f;
        _glints.Rate = _applied.Mode == SceneMode.Weather && _applied.Day ? _celestial.Value * 4f : 0f;
        _ps.Update(dt);

        int active = (int)MathF.Round(rain * MaxStreaks);
        for (int i = 0; i < active; i++)
        {
            _sy[i] += _sv[i] * dt;
            _sx[i] += _sv[i] * 0.16f * dt;
            if (_sy[i] >= 63 && _sy[i] - _sv[i] * dt < 63)
            {
                _splash.X = _sx[i];
                _splash.Y = 62;
                _ps.Burst(_splash, 2);
            }
            if (_sy[i] - _sl[i] > 64) Respawn(i, spread: false);
        }

        _flash = _applied is { Mode: SceneMode.Weather, Kind: WeatherKind.Thunderstorm } ? FlashAt(_t) : 0f;
        if (_flash > 0.01f) BuildBolt((int)MathF.Floor((_t + 2.1f) / 6.8f));
    }

    private void Apply(SceneMood m, bool first)
    {
        var (top, bottom) = Palette(m);
        float cover, dark, cel, stars, rain, snow, fog;
        switch (m.Mode)
        {
            case SceneMode.Loading: (cover, dark, cel, stars, rain, snow, fog) = (1.5f, 0.2f, 0f, 0f, 0f, 0f, 0f); break;
            case SceneMode.Offline: (cover, dark, cel, stars, rain, snow, fog) = (2.2f, 0.7f, 0f, 0f, 0f, 0f, 0f); break;
            default:
                float k = (m.Intensity - 1) * 0.5f;
                (cover, dark, cel, stars, rain, snow, fog) = m.Kind switch
                {
                    WeatherKind.Clear => (0f, 0f, 1f, 1f, 0f, 0f, 0f),
                    WeatherKind.PartlyCloudy => (3.2f, 0.05f, 0.95f, 0.6f, 0f, 0f, 0f),
                    WeatherKind.Cloudy => (7f, 0.35f, 0.3f, 0.15f, 0f, 0f, 0f),
                    WeatherKind.Fog => (1.6f, 0.2f, 0.25f, 0f, 0f, 0f, 1f),
                    WeatherKind.Drizzle => (6f, 0.5f, 0.1f, 0f, 0.2f + 0.08f * k, 0f, 0f),
                    WeatherKind.Rain => (7f, 0.75f, 0f, 0f, 0.42f + 0.28f * k, 0f, 0f),
                    WeatherKind.Snow => (6f, 0.25f, 0f, 0f, 0f, 0.55f + 0.3f * k, 0f),
                    _ => (8f, 1f, 0f, 0f, 0.9f, 0f, 0f),
                };
                break;
        }

        float night = m.Day ? 0f : 1f;
        if (first)
        {
            _top.Set(top); _bottom.Set(bottom);
            _cover.Set(cover); _dark.Set(dark); _celestial.Set(cel); _stars.Set(stars); _rain.Set(rain); _snow.Set(snow); _fog.Set(fog); _night.Set(night);
            _hasMood = true;
            _applied = m;
            _flash = 0f;
            _glints.Rate = 0f;
            _snowSmall.Rate = snow * 38f;
            _snowLarge.Rate = snow * 9f;
            for (int i = 0; i < 100; i++) _ps.Update(1f / 30f); // start with the air already full
            return;
        }

        _applied = m;
        var d = TimeSpan.FromSeconds(1.6);
        var host = Host!.Animator;
        host.Animate(_top, top, d, Easing.InOutSine);
        host.Animate(_bottom, bottom, d, Easing.InOutSine);
        host.Animate(_cover, cover, d, Easing.InOutSine);
        host.Animate(_dark, dark, d, Easing.InOutSine);
        host.Animate(_celestial, cel, d, Easing.InOutSine);
        host.Animate(_stars, stars, d, Easing.InOutSine);
        host.Animate(_rain, rain, d, Easing.InOutSine);
        host.Animate(_snow, snow, d, Easing.InOutSine);
        host.Animate(_fog, fog, d, Easing.InOutSine);
        host.Animate(_night, night, d, Easing.InOutSine);
    }

    private static (Pixel Top, Pixel Bottom) Palette(SceneMood m)
    {
        Pixel top, bottom;
        if (m.Mode == SceneMode.Loading) return (new Pixel(12, 22, 52), new Pixel(38, 58, 108));
        if (m.Mode == SceneMode.Offline) return (new Pixel(26, 28, 38), new Pixel(58, 62, 76));

        (top, bottom) = m.Kind switch
        {
            WeatherKind.Clear => (new Pixel(8, 70, 190), new Pixel(70, 160, 235)),
            WeatherKind.PartlyCloudy => (new Pixel(20, 85, 185), new Pixel(95, 165, 225)),
            WeatherKind.Cloudy => (new Pixel(70, 85, 112), new Pixel(130, 146, 168)),
            WeatherKind.Fog => (new Pixel(110, 118, 130), new Pixel(160, 166, 172)),
            WeatherKind.Drizzle => (new Pixel(40, 60, 98), new Pixel(88, 112, 144)),
            WeatherKind.Rain => (new Pixel(22, 35, 68), new Pixel(55, 76, 112)),
            WeatherKind.Snow => (new Pixel(60, 76, 108), new Pixel(138, 152, 178)),
            _ => (new Pixel(20, 12, 42), new Pixel(56, 42, 88)),
        };

        if (!m.Day)
        {
            top = new Pixel((byte)(top.R * 0.12f), (byte)(top.G * 0.16f), (byte)(top.B * 0.45f));
            bottom = new Pixel((byte)(bottom.R * 0.14f), (byte)(bottom.G * 0.2f), (byte)(bottom.B * 0.5f));
        }

        if (m.Glow > 0f)
        {
            bottom = Pixel.Lerp(bottom, new Pixel(255, 112, 40), m.Glow * 0.7f);
            top = Pixel.Lerp(top, new Pixel(120, 40, 120), m.Glow * 0.35f);
        }
        return (top, bottom);
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        int ox = b.X, oy = b.Y, w = b.Width, h = b.Height;
        var top = _top.Value;
        var bottom = _bottom.Value;

        for (int y = 0; y < h; y++) f.Fill(new Rectangle(ox, oy + y, w, 1), Pixel.Lerp(top, bottom, y / (float)(h - 1)));

        if (_applied.Mode == SceneMode.Loading) { DrawSpinner(f, ox, oy); DrawScrim(f, b); return; }

        DrawStars(f, ox, oy, w);
        if (_applied.Mode == SceneMode.Weather) DrawCelestial(f, ox, oy);
        DrawFog(f, ox, oy, w, h);
        DrawClouds(f, ox, oy, w, h, top, bottom);
        if (_applied.Mode == SceneMode.Offline) DrawOfflineBadge(f, ox, oy);

        // Precipitation sits in front of the clouds. The particle pool draws in frame coordinates, so the scene must sit at the origin.
        DrawStreaks(f, ox, oy);
        _ps.Render(f);
        if (_flash > 0.01f) DrawLightning(f, ox, oy, w, h);
        DrawScrim(f, b);
    }

    private void DrawStars(FrameBuffer f, int ox, int oy, int w)
    {
        float vis = _stars.Value * _night.Value;
        if (vis <= 0.02f) return;
        for (int i = 0; i < 48; i++)
        {
            int x = (int)(Hash(i, 1) % (uint)w), y = (int)(Hash(i, 2) % 44u);
            float dx = x - HeroX, dy = y - HeroY;
            if (dx * dx + dy * dy < 200f) continue;
            float tw = MathF.Sin(_t * (0.7f + i % 5 * 0.35f) + Hash(i, 3) % 100 * 0.1f);
            float a = vis * (0.25f + 0.75f * tw * tw);
            f.BlendPixel(ox + x, oy + y, new Pixel(215, 225, 255), a);
            if (i % 7 == 0 && a > 0.6f)
            {
                f.BlendPixel(ox + x - 1, oy + y, new Pixel(215, 225, 255), a * 0.4f);
                f.BlendPixel(ox + x + 1, oy + y, new Pixel(215, 225, 255), a * 0.4f);
                f.BlendPixel(ox + x, oy + y - 1, new Pixel(215, 225, 255), a * 0.4f);
                f.BlendPixel(ox + x, oy + y + 1, new Pixel(215, 225, 255), a * 0.4f);
            }
        }
    }

    private void DrawCelestial(FrameBuffer f, int ox, int oy)
    {
        float vis = _celestial.Value;
        float r = 9.5f * _hero.Value;
        if (vis <= 0.02f || r <= 0.5f) return;

        bool day = _applied.Day;
        var glow = day ? new Pixel(255, 205, 80) : new Pixel(140, 165, 255);
        float pulse = 1f + 0.12f * MathF.Sin(_t * 1.3f);
        int gr = 27;
        for (int y = -gr; y <= gr; y++)
            for (int x = -gr; x <= gr; x++)
            {
                float d = MathF.Sqrt(x * x + y * y) / gr;
                if (d >= 1f) continue;
                float a = (1f - d) * (1f - d) * 0.6f * vis * pulse;
                f.BlendPixel(ox + (int)HeroX + x, oy + (int)HeroY + y, glow, a);
            }

        if (day) { if (vis > 0.6f) WeatherArt.Sun(f, ox + HeroX, oy + HeroY, r, _t, new Pixel(255, 247, 170), new Pixel(255, 175, 25), vis > 0.6f); }
        else if (vis > 0.5f) WeatherArt.Moon(f, ox + HeroX, oy + HeroY, r, new Pixel(245, 245, 220), new Pixel(205, 205, 185));
    }

    private void DrawClouds(FrameBuffer f, int ox, int oy, int w, int h, Pixel skyTop, Pixel skyBottom)
    {
        float cover = _cover.Value, dark = _dark.Value, night = _night.Value;
        var lightTop = Pixel.Lerp(new Pixel(244, 247, 252), new Pixel(100, 108, 130), dark);
        var lightBottom = Pixel.Lerp(new Pixel(176, 188, 208), new Pixel(54, 60, 80), dark);
        if (night > 0f)
        {
            lightTop = Pixel.Lerp(lightTop, new Pixel((byte)(lightTop.R * 0.3f), (byte)(lightTop.G * 0.33f), (byte)(lightTop.B * 0.5f)), night);
            lightBottom = Pixel.Lerp(lightBottom, new Pixel((byte)(lightBottom.R * 0.28f), (byte)(lightBottom.G * 0.3f), (byte)(lightBottom.B * 0.45f)), night);
        }

        float span = w + 70f;
        for (int i = 0; i < Clouds.Length; i++)
        {
            float a = Math.Clamp(cover - i, 0f, 1f);
            if (a <= 0.02f) continue;
            var c = Clouds[i];
            float x = ((c.X0 + c.Speed * _t) % span + span) % span - 35f;
            // Far clouds melt into the sky; fading in is the same blend with a growing share of cloud.
            float mix = a * (0.5f + 0.5f * c.Depth);
            var sky = Pixel.Lerp(skyTop, skyBottom, c.BaseY / h);
            var topC = Pixel.Lerp(sky, lightTop, mix);
            var botC = Pixel.Lerp(sky, lightBottom, mix);
            WeatherArt.Cloud(f, ox + x, oy + c.BaseY, c.Scale, topC, botC);
        }
    }

    private void DrawFog(FrameBuffer f, int ox, int oy, int w, int h)
    {
        float amt = _fog.Value;
        if (amt <= 0.02f) return;
        for (int band = 0; band < 5; band++)
        {
            int cy = 8 + band * 12;
            float phase = band * 1.7f + _t * (0.25f + band * 0.07f) * (band % 2 == 0 ? 1f : -1f);
            for (int x = 0; x < w; x++)
            {
                float a = amt * 0.32f * (0.55f + 0.45f * MathF.Sin(x * 0.06f + phase));
                for (int dy = -4; dy <= 4; dy++)
                    f.BlendPixel(ox + x, oy + cy + dy, new Pixel(205, 210, 215), a * (1f - MathF.Abs(dy) / 5f));
            }
        }
    }

    private void DrawStreaks(FrameBuffer f, int ox, int oy)
    {
        int active = (int)MathF.Round(_rain.Value * MaxStreaks);
        var head = new Pixel(175, 205, 255);
        for (int i = 0; i < active; i++)
            for (int k = 0; k < _sl[i]; k++)
                f.BlendPixel(ox + (int)MathF.Round(_sx[i] - k * 0.16f), oy + (int)MathF.Round(_sy[i]) - k, head, k == 0 ? 0.95f : 0.6f * (1f - k / (float)_sl[i]));
    }

    private void DrawLightning(FrameBuffer f, int ox, int oy, int w, int h)
    {
        float flash = _flash;
        var tint = new Pixel(190, 205, 255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                f.BlendPixel(ox + x, oy + y, tint, 0.5f * flash);

        for (int i = 0; i + 3 < _bolt.Length; i += 2)
        {
            int x0 = ox + _bolt[i], y0 = oy + _bolt[i + 1], x1 = ox + _bolt[i + 2], y1 = oy + _bolt[i + 3];
            f.DrawLine(x0 - 1, y0, x1 - 1, y1, new Pixel((byte)(120 * flash), (byte)(130 * flash), (byte)(200 * flash)));
            f.DrawLine(x0 + 1, y0, x1 + 1, y1, new Pixel((byte)(120 * flash), (byte)(130 * flash), (byte)(200 * flash)));
            f.DrawLine(x0, y0, x1, y1, new Pixel(255, 252, 225));
            f.DrawLine(x0 + 1, y0, x1 + 1, y1, new Pixel(255, 252, 225));
        }
    }

    // Left edge of the text column fades the scene down so white digits stay legible over bright clouds and sun.
    private void DrawScrim(FrameBuffer f, Rectangle b)
    {
        const int from = 62, to = 80;
        for (int x = from; x < b.Width; x++)
        {
            float a = 0.46f * Math.Clamp((x - from) / (float)(to - from), 0f, 1f);
            for (int y = 0; y < b.Height; y++) f.BlendPixel(b.X + x, b.Y + y, Pixel.Black, a);
        }
    }

    private void DrawSpinner(FrameBuffer f, int ox, int oy)
    {
        float phase = _t * 9f;
        for (int i = 0; i < 12; i++)
        {
            float ang = i * MathF.Tau / 12f - MathF.PI / 2f;
            float tail = ((phase - i) % 12f + 12f) % 12f;
            float a = Math.Clamp(1f - tail / 9f, 0.12f, 1f);
            int x = ox + (int)MathF.Round(HeroX + MathF.Cos(ang) * 13f), y = oy + (int)MathF.Round(HeroY + MathF.Sin(ang) * 13f);
            f.FillRect(new Rectangle(x - 1, y - 1, 3, 3), Pixel.Black.Blend(new Pixel(150, 200, 255), a));
        }
    }

    private void DrawOfflineBadge(FrameBuffer f, int ox, int oy)
    {
        float pulse = 0.65f + 0.35f * MathF.Sin(_t * 3f);
        var amber = new Pixel(255, 176, 0).WithBrightness(pulse);
        int cx = ox + 44, tipY = oy + 20;
        for (int row = 0; row < 15; row++)
            f.Fill(new Rectangle(cx - row / 2 - (row > 0 ? 1 : 0), tipY + row, row + (row > 0 ? 3 : 1), 1), amber);
        f.Fill(new Rectangle(cx - 1, tipY + 6, 2, 5), Pixel.Black);
        f.Fill(new Rectangle(cx - 1, tipY + 12, 2, 2), Pixel.Black);
    }

    private static float FlashAt(float t)
    {
        float p = (t + 2.1f) % 6.8f;
        if (p < 0.07f) return 1f;
        if (p < 0.16f) return 0.12f;
        if (p < 0.30f) return 0.8f * (1f - (p - 0.16f) / 0.14f);
        return 0f;
    }

    private void BuildBolt(int strike)
    {
        if (strike == _boltStrike) return;
        _boltStrike = strike;
        int x = 20 + (int)(Hash(strike, 11) % 100u), y = 0;
        for (int i = 0; i + 1 < _bolt.Length; i += 2)
        {
            _bolt[i] = x;
            _bolt[i + 1] = y;
            x += (int)(Hash(strike, 20 + i) % 11u) - 5;
            y += 7;
        }
    }

    private void Respawn(int i, bool spread)
    {
        _rng = _rng * 1664525u + 1013904223u;
        _sx[i] = (_rng >> 8) % (uint)(Width ?? 152) - 10;
        _rng = _rng * 1664525u + 1013904223u;
        _sy[i] = spread ? (_rng >> 8) % 64u : -(float)((_rng >> 8) % 24u);
        _rng = _rng * 1664525u + 1013904223u;
        _sv[i] = 95f + (_rng >> 8) % 55u;
        _sl[i] = 3 + (int)((_rng >> 20) % 3u);
    }

    private static uint Hash(int a, int b)
    {
        uint h = (uint)a * 374761393u + (uint)b * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
}
