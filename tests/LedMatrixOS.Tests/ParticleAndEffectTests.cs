using System.Diagnostics;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Effects;
using LedMatrixOS.Graphics.Particles;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

public class ParticleAndEffectTests(ITestOutputHelper output)
{
    private static readonly FrameContext Ctx = new(TimeSpan.Zero, TimeSpan.FromMilliseconds(16), 0);

    private static FrameBuffer Gradient(int w = 32, int h = 16)
    {
        var f = new FrameBuffer(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                f.SetPixel(x, y, new Pixel((byte)(x * 7), (byte)(y * 13), (byte)(x * y)));
        return f;
    }

    private static void AssertSame(FrameBuffer a, FrameBuffer b)
    {
        Assert.True(a.GetPixelsSpan().SequenceEqual(b.GetPixelsSpan()));
    }

    private static FrameBuffer Confetti(int seed, int steps = 20)
    {
        var sys = new ParticleSystem(256, 64, 256, new Random(seed));
        sys.Burst(sys.Add(ParticlePresets.Confetti(128, 50)), 120);
        for (int i = 0; i < steps; i++) sys.Update(1f / 30f);
        var frame = new FrameBuffer(256, 64);
        sys.Render(frame);
        return frame;
    }

    [Fact]
    public void Particles_SameSeed_AreIdentical()
    {
        AssertSame(Confetti(42), Confetti(42));
        Assert.False(Confetti(42).GetPixelsSpan().SequenceEqual(Confetti(43).GetPixelsSpan()));
    }

    [Fact]
    public void Particles_NeverExceedCapacity()
    {
        var sys = new ParticleSystem(64, 32, 10, new Random(1));
        var e = sys.Add(ParticlePresets.Sparkles(64, 32));
        e.Rate = 1000f;
        sys.Burst(e, 100);
        Assert.Equal(10, sys.Count);
        for (int i = 0; i < 20; i++)
        {
            sys.Update(0.05f);
            Assert.True(sys.Count <= sys.Capacity);
        }
    }

    [Fact]
    public void Particles_DieAtEndOfLife()
    {
        var sys = new ParticleSystem(64, 32, 50, new Random(1));
        var e = sys.Add(new Emitter { LifetimeMin = 1f, LifetimeMax = 1f });
        sys.Burst(e, 30);
        sys.Update(0.9f);
        Assert.Equal(30, sys.Count);
        sys.Update(0.2f);
        Assert.Equal(0, sys.Count);
    }

    [Fact]
    public void Particles_RateSpawnsExpectedCount()
    {
        var sys = new ParticleSystem(64, 32, 100, new Random(1));
        sys.Add(new Emitter { Rate = 10f, LifetimeMin = 10f, LifetimeMax = 10f });
        for (int i = 0; i < 10; i++) sys.Update(0.1f);
        Assert.InRange(sys.Count, 9, 10); // float accumulation may land one short
    }

    [Fact]
    public void Particles_BounceAndWrap_StayInBounds()
    {
        foreach (var mode in new[] { EdgeMode.Bounce, EdgeMode.Wrap })
        {
            var sys = new ParticleSystem(32, 16, 50, new Random(5));
            var e = sys.Add(new Emitter
            {
                X = 16, Y = 8, SpeedMin = 50f, SpeedMax = 100f, Spread = 360f,
                LifetimeMin = 5f, LifetimeMax = 5f, Edge = mode,
            });
            sys.Burst(e, 50);
            var frame = new FrameBuffer(32, 16);
            for (int i = 0; i < 60; i++) sys.Update(0.05f); // 3s: still alive, many edge hits
            Assert.Equal(50, sys.Count);
            sys.Render(frame);
            Assert.False(SnapshotHelper.IsBlank(frame));
        }
    }

    [Fact]
    public void Particles_BilinearSplat_SplitsAcrossPixels()
    {
        var sys = new ParticleSystem(8, 8, 1, new Random(1));
        var e = sys.Add(new Emitter { X = 3.5f, Y = 3f, LifetimeMin = 1f, LifetimeMax = 1f, AlphaEnd = 1f });
        sys.Burst(e, 1);
        var frame = new FrameBuffer(8, 8);
        sys.Render(frame);
        Assert.Equal(frame.GetPixel(3, 3), frame.GetPixel(4, 3));
        Assert.InRange(frame.GetPixel(3, 3).R, 126, 129);
        Assert.Equal(Pixel.Black, frame.GetPixel(5, 3));
    }

    [Fact]
    public void Glow_SpreadsBrightPixels_AndIgnoresDarkOnes()
    {
        var f = new FrameBuffer(16, 16);
        f.SetPixel(8, 8, Pixel.White);
        new GlowEffect { Threshold = 100f, Radius = 2, Strength = 1f }.Apply(f, Ctx);
        Assert.True(f.GetPixel(9, 8).R > 0);
        Assert.True(f.GetPixel(8, 10).R > 0);
        Assert.Equal(Pixel.Black, f.GetPixel(8, 12));

        var dark = new FrameBuffer(16, 16);
        dark.Clear(new Pixel(50, 50, 50));
        new GlowEffect { Threshold = 100f }.Apply(dark, Ctx);
        Assert.Equal(new Pixel(50, 50, 50), dark.GetPixel(5, 5));
    }

    [Fact]
    public void Snapshot_ParticleBurst()
    {
        SnapshotHelper.AssertMatchesSnapshot(Confetti(7), "particles-confetti-burst");
    }

    [Fact]
    public void Snapshot_Glow()
    {
        var frame = SnapshotHelper.Render(f =>
        {
            f.Fill(new SixLabors.ImageSharp.Rectangle(20, 20, 40, 24), new Pixel(255, 80, 40));
            f.Fill(new SixLabors.ImageSharp.Rectangle(120, 10, 4, 44), new Pixel(80, 220, 255));
            f.SetPixel(200, 32, Pixel.White);
        });
        new GlowEffect { Threshold = 120f, Radius = 3, Strength = 1.2f }.Apply(frame, Ctx);
        SnapshotHelper.AssertMatchesSnapshot(frame, "effects-glow");
    }
}
