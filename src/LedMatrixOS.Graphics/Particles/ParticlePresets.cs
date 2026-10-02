using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Particles;

/// <summary>
/// Ready-made emitters for a frame of the given size. Add one to a <see cref="ParticleSystem"/>.
/// </summary>
public static class ParticlePresets
{
    /// <summary>Colourful pieces that fly up from a point and fall. Rate 0: fire it with <see cref="ParticleSystem.Burst"/>.</summary>
    public static Emitter Confetti(float x, float y) => new()
    {
        X = x, Y = y,
        Rate = 0f,
        LifetimeMin = 1.5f, LifetimeMax = 2.5f,
        SpeedMin = 30f, SpeedMax = 90f,
        Angle = -90f, Spread = 100f,
        GravityY = 60f,
        Palette = [new Pixel(255, 60, 60), new Pixel(255, 200, 40), new Pixel(60, 220, 90), new Pixel(60, 140, 255), new Pixel(220, 80, 255)],
        AlphaStart = 1f, AlphaEnd = 0.6f,
    };

    /// <summary>Short white-to-gold twinkles appearing anywhere in the frame.</summary>
    public static Emitter Sparkles(int width, int height) => new()
    {
        X = 0, Y = 0, Width = width - 1, Height = height - 1,
        Rate = 20f,
        LifetimeMin = 0.4f, LifetimeMax = 1f,
        Gradient = [Pixel.White, new Pixel(255, 210, 90)],
        AlphaStart = 1f, AlphaEnd = 0f,
    };

    /// <summary>Fast bluish drops slanting down from above the frame.</summary>
    public static Emitter Rain(int width, int height) => new()
    {
        X = 0, Y = -2, Width = width + height / 4f, Height = 0,
        Rate = width * 0.4f,
        LifetimeMin = 0.6f, LifetimeMax = 0.9f,
        SpeedMin = 90f, SpeedMax = 120f,
        Angle = 80f, Spread = 4f,
        Gradient = [new Pixel(90, 140, 255)],
        AlphaStart = 0.8f, AlphaEnd = 0.8f,
    };

    /// <summary>Slow drifting white flakes falling from above the frame.</summary>
    public static Emitter Snow(int width, int height) => new()
    {
        X = 0, Y = -2, Width = width - 1, Height = 0,
        Rate = width * 0.15f,
        LifetimeMin = 3f, LifetimeMax = 5f,
        SpeedMin = 8f, SpeedMax = 16f,
        Angle = 90f, Spread = 40f,
        Gradient = [Pixel.White],
        AlphaStart = 0.9f, AlphaEnd = 0.9f,
    };

    /// <summary>Orange-to-red sparks rising from the bottom edge and cooling off.</summary>
    public static Emitter Embers(int width, int height) => new()
    {
        X = 0, Y = height - 1, Width = width - 1, Height = 0,
        Rate = width * 0.1f,
        LifetimeMin = 1.5f, LifetimeMax = 3f,
        SpeedMin = 6f, SpeedMax = 20f,
        Angle = -90f, Spread = 50f,
        GravityY = -4f,
        Gradient = [new Pixel(255, 220, 80), new Pixel(255, 110, 20), new Pixel(150, 20, 0)],
        AlphaStart = 1f, AlphaEnd = 0f,
    };
}
