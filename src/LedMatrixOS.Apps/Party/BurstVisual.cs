using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Particles;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Party;

/// <summary>Fireworks-style particle bursts that go off on every beat, over a dark background that flashes faintly with the pulse.</summary>
internal sealed class BurstVisual : VisualNode
{
    private readonly BeatSignal _beat;
    private readonly Random _rng;
    private readonly Pixel[] _palette = new Pixel[12];
    private readonly Emitter _burst;
    private ParticleSystem? _particles;
    private int _w, _h, _shot;

    public BurstVisual(BeatSignal beat, int seed)
    {
        _beat = beat;
        _rng = new Random(seed);
        for (int i = 0; i < _palette.Length; i++) _palette[i] = Pixel.FromHsv(i * 30f, 0.85f, 1f);
        _burst = new Emitter
        {
            Rate = 0f,
            LifetimeMin = 0.9f, LifetimeMax = 1.8f,
            SpeedMin = 14f, SpeedMax = 90f,
            Size = 2f,
            Angle = 0f, Spread = 360f,
            GravityY = 40f,
            AlphaStart = 1f, AlphaEnd = 0f,
            Palette = _palette,
        };
    }

    /// <summary>Live particle count (tests).</summary>
    public int ParticleCount => _particles?.Count ?? 0;

    protected override void Step(FrameContext ctx)
    {
        var host = Host!;
        if (_w != host.Width || _h != host.Height)
        {
            _w = host.Width;
            _h = host.Height;
            _particles = new ParticleSystem(_w, _h, 900, new Random(_rng.Next()));
        }

        if (_beat.Beat)
        {
            _shot++;
            // Rotate the hue window each burst so consecutive bursts differ, and aim at the upper two thirds of the display.
            int rot = _shot * 5;
            for (int i = 0; i < _palette.Length; i++) _palette[i] = Pixel.FromHsv((rot * 30f + i * 12f) % 360f, 0.9f, 1f);
            _burst.X = _w * (0.1f + 0.8f * _rng.NextSingle());
            _burst.Y = _h * (0.2f + 0.45f * _rng.NextSingle());
            _burst.Width = 0;
            _burst.Height = 0;
            _particles!.Burst(_burst, 70 + (int)(_beat.Level * 60f));
        }

        _particles?.Update(Dt);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_particles is null) return;
        int glow = (int)(_beat.Pulse * 22f);
        frame.Clear(new Pixel((byte)(glow / 2), (byte)0, (byte)glow));
        _particles.Render(frame);
    }
}
