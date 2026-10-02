using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Particles;

/// <summary>
/// What a particle does when it leaves the frame.
/// </summary>
public enum EdgeMode
{
    /// <summary>Keep flying; it only dies at the end of its lifetime (lets rain start above the frame).</summary>
    None,
    /// <summary>Reappear on the opposite edge.</summary>
    Wrap,
    /// <summary>Reflect off the edges, keeping all of its speed.</summary>
    Bounce,
}

/// <summary>
/// Configuration for spawning particles. Position is the top-left of the spawn area; a zero-size area is a point.
/// Angles are in degrees with 0 pointing right and 90 pointing down (screen space).
/// </summary>
public sealed class Emitter
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    /// <summary>Particles spawned per second by <see cref="ParticleSystem.Update"/>. Use <see cref="ParticleSystem.Burst"/> for one-shots.</summary>
    public float Rate { get; set; }
    public bool Enabled { get; set; } = true;

    public float LifetimeMin { get; set; } = 1f;
    public float LifetimeMax { get; set; } = 1f;

    public float SpeedMin { get; set; }
    public float SpeedMax { get; set; }
    public float Angle { get; set; }
    /// <summary>Total cone width in degrees, centred on <see cref="Angle"/>.</summary>
    public float Spread { get; set; }

    /// <summary>Acceleration in pixels per second squared.</summary>
    public float GravityX { get; set; }
    public float GravityY { get; set; }

    /// <summary>Diameter in pixels. At 1 or below the particle is splatted bilinearly; above, it is a nearest-pixel square.</summary>
    public float Size { get; set; } = 1f;

    /// <summary>Colour stops spread evenly over the lifetime. Ignored when <see cref="Palette"/> is set.</summary>
    public Pixel[] Gradient { get; set; } = [Pixel.White];
    /// <summary>If set, each particle picks one of these colours at spawn and keeps it.</summary>
    public Pixel[]? Palette { get; set; }

    /// <summary>Opacity at birth and at end of life (linear in between).</summary>
    public float AlphaStart { get; set; } = 1f;
    public float AlphaEnd { get; set; }

    public EdgeMode Edge { get; set; }

    internal float Accumulator;

    internal Pixel Evaluate(float t)
    {
        var g = Gradient;
        if (g.Length == 1) return g[0];
        float f = Math.Clamp(t, 0f, 1f) * (g.Length - 1);
        int i = Math.Min((int)f, g.Length - 2);
        return Pixel.Lerp(g[i], g[i + 1], f - i);
    }
}
