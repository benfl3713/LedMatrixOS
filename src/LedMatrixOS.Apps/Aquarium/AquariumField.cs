using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Aquarium;

/// <summary>
/// The whole aquarium as one node: a water gradient with light shafts, swaying seaweed, pixel fish that swim back and forth, and bubbles
/// rising from the sand (a pooled <see cref="ParticleSystem"/>). Everything is seeded, so a given time always draws the same picture,
/// and nothing allocates once the first frame has run.
/// </summary>
public sealed class AquariumField : Node
{
    public const int MaxFish = 24;
    private const int Seed = 2026;
    private const int SandHeight = 5;

    private sealed class Fish
    {
        public float X, Y, Speed, Phase, BobAmp, BobRate;
        public int Dir;                 // +1 swims right, -1 left
        public int Length;              // body length in pixels (3..9)
        public Pixel Body, Fin;
    }

    private sealed class Weed
    {
        public int X, Height;
        public float Phase;
        public Pixel Color;
    }

    private static readonly Pixel[] FishColors =
    [
        new(255, 140, 30), new(255, 210, 40), new(255, 90, 120), new(120, 220, 255), new(170, 120, 255), new(255, 255, 255), new(90, 230, 150),
    ];

    private readonly Fish[] _fish = new Fish[MaxFish];
    private readonly Weed[] _weeds = new Weed[22];
    private readonly Random _rng = new(Seed);
    private readonly Emitter _bubbles;
    private ParticleSystem? _particles;
    private int _w = 256, _h = 64;
    private float _time;

    private Pixel _top = new(10, 70, 130), _bottom = new(2, 20, 60);
    private int _count = 8;

    public AquariumField()
    {
        for (int i = 0; i < MaxFish; i++)
        {
            int length = 3 + _rng.Next(0, 7);
            var body = FishColors[_rng.Next(FishColors.Length)];
            _fish[i] = new Fish
            {
                X = _rng.Next(0, 256),
                Y = 6 + _rng.NextSingle() * 38,
                Speed = 6 + _rng.NextSingle() * 16 + (9 - length) * 1.5f,
                Phase = _rng.NextSingle() * 6.28f,
                BobAmp = 1 + _rng.NextSingle() * 2.5f,
                BobRate = 0.6f + _rng.NextSingle(),
                Dir = _rng.Next(2) == 0 ? -1 : 1,
                Length = length,
                Body = body,
                Fin = body.WithBrightness(0.65f),
            };
        }

        for (int i = 0; i < _weeds.Length; i++)
        {
            _weeds[i] = new Weed
            {
                X = 4 + i * 11 + _rng.Next(-3, 4),
                Height = 8 + _rng.Next(0, 16),
                Phase = _rng.NextSingle() * 6.28f,
                Color = new Pixel((byte)_rng.Next(10, 40), (byte)_rng.Next(110, 190), (byte)_rng.Next(40, 90)),
            };
        }

        _bubbles = new Emitter
        {
            Rate = 0f,
            LifetimeMin = 3f,
            LifetimeMax = 5f,
            SpeedMin = 9f,
            SpeedMax = 17f,
            Angle = 270f,
            Spread = 14f,
            GravityY = -2f,
            Gradient = [new Pixel(190, 235, 255)],
            AlphaStart = 0.85f,
            AlphaEnd = 0.1f,
        };
    }

    /// <summary>How many fish are swimming (0..<see cref="MaxFish"/>).</summary>
    public int FishCount { get => _count; set => _count = Math.Clamp(value, 0, MaxFish); }

    public void SetWater(Pixel top, Pixel bottom)
    {
        _top = top;
        _bottom = bottom;
    }

    internal (float X, float Y, int Dir) FishAt(int i) => (_fish[i].X, _fish[i].Y, _fish[i].Dir);

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_particles is null)
        {
            _w = Host?.Width ?? 256;
            _h = Host?.Height ?? 64;
            _particles = new ParticleSystem(_w, _h, 80, new Random(Seed + 1));
            _bubbles.X = 0;
            _bubbles.Width = _w;
            _bubbles.Y = _h - SandHeight;
            _bubbles.Height = 1;
            _particles.Add(_bubbles);
        }

        float dt = Math.Min((float)ctx.Delta.TotalSeconds, 0.1f);
        _time += dt;
        _bubbles.Rate = 3f + _count * 0.5f;
        _particles.Update(dt);

        for (int i = 0; i < _count; i++)
        {
            var f = _fish[i];
            f.X += f.Dir * f.Speed * dt;
            float margin = f.Length + 3;
            if (f.Dir > 0 && f.X > _w + margin) f.X = -margin;
            else if (f.Dir < 0 && f.X < -margin) f.X = _w + margin;
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var particles = _particles;
        if (particles is null) return;

        int ox = bounds.X, oy = bounds.Y;
        int sandTop = bounds.Bottom - SandHeight;

        // Water, with a few slow light shafts from the surface.
        frame.FillLinearGradient(new Rectangle(ox, oy, bounds.Width, bounds.Height), _top, _bottom, vertical: true);
        int depth = sandTop - oy;
        for (int s = 0; s < 5; s++)
        {
            int baseX = ox + 20 + s * 52 + (int)(MathF.Sin(_time * 0.2f + s * 1.7f) * 8);
            for (int y = 0; y < depth; y++)
            {
                float fade = 1f - y / (float)depth;
                int x = baseX + y / 3;
                for (int w = 0; w < 4; w++) frame.BlendPixel(x + w, oy + y, Pixel.White, 0.06f * fade);
            }
        }

        // Sand
        for (int y = sandTop; y < bounds.Bottom; y++)
            frame.Fill(new Rectangle(ox, y, bounds.Width, 1), y == sandTop ? new Pixel(196, 170, 110) : new Pixel(150, 126, 80));
        for (int x = ox + 3; x < bounds.Right; x += 7) frame.SetPixel(x, sandTop + 1 + x % 3, new Pixel(120, 98, 62));

        // Seaweed: each strand is a column of pixels whose offset grows with height.
        foreach (var weed in _weeds)
        {
            for (int h = 0; h < weed.Height; h++)
            {
                float sway = MathF.Sin(_time * 1.3f + weed.Phase + h * 0.25f) * (h * 0.12f);
                var color = (h & 1) == 0 ? weed.Color : weed.Color.WithBrightness(0.75f);
                int x = ox + weed.X + (int)MathF.Round(sway);
                int y = sandTop - 1 - h;
                frame.SetPixel(x, y, color);
                if (h % 5 == 4) frame.SetPixel(x + 1, y, color);
            }
        }

        for (int i = 0; i < _count; i++) DrawFish(frame, _fish[i], ox, oy);

        particles.Render(frame);
    }

    private void DrawFish(FrameBuffer frame, Fish f, int ox, int oy)
    {
        int cx = ox + (int)f.X;
        int cy = oy + (int)MathF.Round(f.Y + MathF.Sin(_time * f.BobRate + f.Phase) * f.BobAmp);
        int d = f.Dir;
        int len = f.Length;
        int half = Math.Max(1, len / 4);

        // Body: a lens, widest in the middle. i runs from the tail (0) to the nose (len-1).
        for (int i = 0; i < len; i++)
        {
            float t = (i + 0.5f) / len;
            int thick = (int)MathF.Round(half * MathF.Sin(t * MathF.PI) * 1.3f);
            int x = d > 0 ? cx + i : cx - i;
            for (int dy = -thick; dy <= thick; dy++)
                frame.SetPixel(x, cy + dy, dy > 0 ? f.Body.WithBrightness(0.8f) : f.Body);
        }

        // Tail fin flaring behind the body, flapping.
        int flap = (int)MathF.Round(MathF.Sin(_time * 7f + f.Phase));
        int tailX = d > 0 ? cx - 1 : cx + 1;
        frame.SetPixel(tailX, cy + flap, f.Fin);
        frame.SetPixel(tailX - d, cy - 1 + flap, f.Fin);
        frame.SetPixel(tailX - d, cy + 1 + flap, f.Fin);
        if (len >= 6)
        {
            frame.SetPixel(tailX - 2 * d, cy - 2 + flap, f.Fin);
            frame.SetPixel(tailX - 2 * d, cy + 2 + flap, f.Fin);
            frame.SetPixel(d > 0 ? cx + len / 2 : cx - len / 2, cy - half - 1, f.Fin);   // dorsal fin
        }

        if (len >= 4) frame.SetPixel(d > 0 ? cx + len - 2 : cx - len + 2, cy - (half > 1 ? 1 : 0), Pixel.White);   // eye
    }
}
