namespace LedMatrixOS.Core.Transitions;

/// <summary>
/// Pixels flip to the new frame in a fixed pseudo-random order (a hashed per-pixel threshold).
/// </summary>
public sealed class DissolveTransition : ITransition
{
    public string Name => "dissolve";

    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(700);

    public void Render(FrameBuffer from, FrameBuffer to, FrameBuffer output, float progress)
    {
        for (int y = 0; y < output.Height; y++)
            for (int x = 0; x < output.Width; x++)
                output.SetPixel(x, y, Threshold(x, y) < progress ? to.GetPixel(x, y) : from.GetPixel(x, y));
    }

    /// <summary>Deterministic value in [0, 1) for a pixel (or column when y is 0).</summary>
    internal static float Threshold(int x, int y)
    {
        uint h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u;
        h ^= h >> 15; h *= 0x2C1B3C6Du;
        h ^= h >> 12; h *= 0x297A2D39u;
        h ^= h >> 15;
        return (h >> 8) / 16777216f;
    }
}
