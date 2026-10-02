using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Clocks;

/// <summary>Colours and clock-driven timing shared by the nodes of the Animated Clock.</summary>
internal sealed class AnimatedFx
{
    public ClockPalette Palette { get; private set; } = ClockPalette.Aurora;
    public bool Rainbow { get; private set; }
    /// <summary>Seconds of the day, drives slow hue changes and wave motion.</summary>
    public float Time { get; set; }

    public static readonly string[] Names = ["Aurora", "Lava", "Cyber", "Ocean", "Rainbow"];

    public void SetPalette(string? name)
    {
        Rainbow = name == "Rainbow";
        Palette = name switch
        {
            "Lava" => ClockPalette.Ember,
            "Cyber" => ClockPalette.Neon,
            "Ocean" => ClockPalette.Ocean,
            _ => ClockPalette.Aurora,
        };
    }

    /// <summary>Ribbon / accent colour k (0..2).</summary>
    public Pixel Ribbon(int k)
    {
        if (Rainbow) return Pixel.FromHsv(Time * 22f + k * 75f, 0.9f, 1f);
        return k switch { 0 => Palette.G0, 1 => Palette.G1, _ => Palette.G2 };
    }

    /// <summary>A colour slid along the palette; <paramref name="phase"/> is 0..1.</summary>
    public Pixel Glow(float phase)
    {
        phase = Math.Clamp(phase, 0f, 1f);
        if (Rainbow) return Pixel.FromHsv(Time * 22f + phase * 150f, 0.85f, 1f);
        return phase < 0.5f ? Pixel.Lerp(Palette.G0, Palette.G1, phase * 2f) : Pixel.Lerp(Palette.G1, Palette.G2, phase * 2f - 1f);
    }
}

/// <summary>Updates <see cref="AnimatedFx.Time"/> from the clock every frame.</summary>
internal sealed class FxDriver : Node
{
    private readonly ClockState _state;
    private readonly AnimatedFx _fx;

    public FxDriver(ClockState state, AnimatedFx fx)
    {
        _state = state;
        _fx = fx;
        Width = 0;
        Height = 0;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _fx.Time = (float)(_state.SecondsOfDay % 100000.0);
    }
}

/// <summary>
/// Three layered light ribbons that flow across the panel. On every second tick a ripple runs out from the colon, kicking the
/// ribbons up into a travelling wave front that brightens as it passes. Allocation-free: one pass of BlendPixel per ribbon column.
/// </summary>
internal sealed class WaveField : Node
{
    private static readonly float[] Amp = [7f, 5f, 4f];
    private static readonly float[] Freq = [0.040f, 0.072f, 0.105f];
    private static readonly float[] Speed = [0.8f, -1.15f, 1.7f];
    private static readonly float[] Base = [34f, 29f, 41f];
    private static readonly float[] Thick = [11f, 8f, 6.5f];
    private static readonly float[] Strength = [0.7f, 0.7f, 0.85f];

    private readonly ClockState _state;
    private readonly AnimatedFx _fx;

    public WaveField(ClockState state, AnimatedFx fx)
    {
        _state = state;
        _fx = fx;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public float Intensity { get; set; } = 1f;

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float t = _fx.Time;
        float frac = _state.SecFrac;
        float front = Easing.OutCubic(Math.Min(1f, frac * 1.1f)) * 150f;
        float kick = (1f - frac) * (1f - frac);
        float cx = bounds.Width / 2f;

        // Faint base glow rising from the bottom so the panel is never flat black.
        var bg = _fx.Ribbon(1);
        for (int y = 0; y < bounds.Height; y++)
        {
            float a = 0.05f + 0.1f * (y / (float)bounds.Height) * (y / (float)bounds.Height);
            var c = new Pixel((byte)(bg.R * a), (byte)(bg.G * a), (byte)(bg.B * a));
            frame.Fill(new Rectangle(bounds.X, bounds.Y + y, bounds.Width, 1), c);
        }

        for (int k = 0; k < 3; k++)
        {
            var color = _fx.Ribbon(k);
            float thick = Thick[k];
            for (int x = 0; x < bounds.Width; x++)
            {
                float dist = MathF.Abs(x - cx);
                float d = dist - front;
                float ring = MathF.Exp(-d * d * 0.004f) * kick;
                float y = Base[k]
                          + Amp[k] * MathF.Sin(x * Freq[k] + t * Speed[k] + k * 2.1f)
                          + 2.5f * MathF.Sin(x * Freq[k] * 2.3f - t * Speed[k] * 1.3f)
                          - ring * 9f * MathF.Cos(d * 0.3f);
                float gain = Strength[k] * Intensity * (1f + ring * 0.9f);
                int y0 = (int)MathF.Ceiling(y - thick), y1 = (int)MathF.Floor(y + thick);
                for (int py = y0; py <= y1; py++)
                {
                    float u = 1f - MathF.Abs(py - y) / thick;
                    float a = u * u * gain;
                    if (a <= 0.01f) continue;
                    frame.BlendPixel(bounds.X + x, bounds.Y + py, color, Math.Min(a, 0.95f));
                }
            }
        }
    }
}

/// <summary>A particle system drawn as a node. Time comes from the frame delta, the random generator is seeded, so renders are deterministic.</summary>
internal sealed class ParticleLayer : Node
{
    public ParticleLayer(ParticleSystem system)
    {
        System = system;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public ParticleSystem System { get; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        System.Update((float)Math.Min(ctx.Delta.TotalSeconds, 0.1));
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds) => System.Render(frame);
}

/// <summary>
/// A glowing "neon tube" digit: near-white core, a halo in the palette colour that pulses on each second tick, a dark underlay for
/// contrast against the waves, and a gentle travelling bob so the line of digits breathes.
/// </summary>
internal sealed class NeonDigit : GlyphDigit
{
    private readonly AnimatedFx _fx;
    private readonly ClockState _state;
    private readonly int _index;

    public NeonDigit(GlyphAtlas atlas, Func<int> source, AnimatedFx fx, ClockState state, int index) : base(atlas, source)
    {
        _fx = fx;
        _state = state;
        _index = index;
        BackingAlpha = 0.55f;
    }

    public float BobAmount { get; set; } = 1.6f;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        float bob = MathF.Sin(_fx.Time * 1.6f + _index * 0.9f) * BobAmount;
        Position = new System.Numerics.Vector2(0, bob);
    }

    protected override void ResolveColors()
    {
        float phase = 0.5f + 0.5f * MathF.Sin(_fx.Time * 0.25f + _index * 0.55f);
        var glow = _fx.Glow(phase);
        float frac = _state.SecFrac;
        float pulse = (1f - frac) * (1f - frac);
        Halo = glow;
        HaloAlpha = 0.5f + 0.4f * pulse;
        FillGradient(Pixel.Lerp(glow, Pixel.White, 0.72f), Pixel.Lerp(glow, Pixel.White, 0.32f + 0.25f * pulse));
    }
}

/// <summary>Spawns a spark burst a moment after a digit starts to change (when the new digit lands).</summary>
internal sealed class SparkBursts
{
    private const int Slots = 8;
    private readonly float[] _due = new float[Slots];
    private readonly int[] _x = new int[Slots], _y = new int[Slots], _w = new int[Slots];
    private readonly ParticleSystem _system;
    private readonly Emitter _emitter;
    private readonly AnimatedFx _fx;

    public SparkBursts(ParticleSystem system, AnimatedFx fx)
    {
        _system = system;
        _fx = fx;
        _emitter = new Emitter
        {
            Rate = 0f,
            LifetimeMin = 0.5f, LifetimeMax = 1.1f,
            SpeedMin = 22f, SpeedMax = 75f,
            Angle = -90f, Spread = 150f,
            GravityY = 85f,
            Size = 1f,
            AlphaStart = 1f, AlphaEnd = 0f,
            Gradient = [Pixel.White, new Pixel(255, 220, 140), new Pixel(255, 90, 40)],
        };
        for (int i = 0; i < Slots; i++) _due[i] = -1f;
    }

    public void Recolor()
    {
        if (_fx.Rainbow) _emitter.Palette = [Pixel.FromHsv(0, .8f, 1), Pixel.FromHsv(60, .8f, 1), Pixel.FromHsv(140, .8f, 1), Pixel.FromHsv(200, .8f, 1), Pixel.FromHsv(280, .8f, 1)];
        else
        {
            _emitter.Palette = null;
            _emitter.Gradient = [Pixel.White, Pixel.Lerp(_fx.Palette.G0, Pixel.White, 0.4f), _fx.Palette.G1, _fx.Palette.G2];
        }
    }

    public void Queue(Rectangle digitBounds, float delaySeconds)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (_due[i] >= 0f) continue;
            _due[i] = delaySeconds;
            _x[i] = digitBounds.X;
            _y[i] = digitBounds.Bottom;
            _w[i] = digitBounds.Width;
            return;
        }
    }

    public void Update(float dt)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (_due[i] < 0f) continue;
            _due[i] -= dt;
            if (_due[i] > 0f) continue;
            _due[i] = -1f;
            _emitter.X = _x[i] + 3;
            _emitter.Width = Math.Max(0, _w[i] - 6);
            _emitter.Y = _y[i] - 8;
            _emitter.Height = 4;
            _system.Burst(_emitter, 14);
        }
    }
}

/// <summary>Runs the queued bursts; place before the particle layer that draws them.</summary>
internal sealed class BurstDriver : Node
{
    private readonly SparkBursts _bursts;

    public BurstDriver(SparkBursts bursts)
    {
        _bursts = bursts;
        Width = 0;
        Height = 0;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _bursts.Update((float)Math.Min(ctx.Delta.TotalSeconds, 0.1));
    }
}
