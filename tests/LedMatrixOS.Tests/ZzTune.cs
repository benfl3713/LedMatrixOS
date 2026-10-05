using System.Reflection;
using LedMatrixOS.Apps.Alive;
using LedMatrixOS.Core;
using Xunit;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class ZzTune
{
    [Theory]
    [InlineData(0.078f, 0.061f, 0.35f)]
    [InlineData(0.062f, 0.0609f, 0.35f)]
    [InlineData(0.058f, 0.065f, 0.35f)]
    [InlineData(0.046f, 0.063f, 0.35f)]
    [InlineData(0.0545f, 0.062f, 0.35f)]
    [InlineData(0.0367f, 0.0649f, 0.35f)]
    [InlineData(0.029f, 0.057f, 0.35f)]
    public void Rd(float f, float k, float d)
    {
        var sim = new ReactionDiffusionSim();
        sim.Resize(256, 64);
        var c = new AliveContext(new Random(5));
        sim.Seed(c);
        typeof(ReactionDiffusionSim).GetField("_f", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(sim, f);
        typeof(ReactionDiffusionSim).GetField("_k", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(sim, k);
        for (int i = 0; i < 260; i++) sim.Step(0.033f, c);
        var fb = new FrameBuffer(256, 64);
        sim.Draw(fb, new SixLabors.ImageSharp.Rectangle(0, 0, 256, 64), c);
        Preview(fb, $"tune_rd_{f}_{k}");
    }
}
