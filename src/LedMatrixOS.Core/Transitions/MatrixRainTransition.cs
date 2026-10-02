namespace LedMatrixOS.Core.Transitions;

/// <summary>
/// Each column starts at its own (hashed) moment and a bright head falls down it, leaving the new frame behind.
/// </summary>
public sealed class MatrixRainTransition : ITransition
{
    private static readonly Pixel Head = new(190, 255, 190);
    private const float MaxDelay = 0.5f;

    public string Name => "matrix-rain";

    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(900);

    public void Render(FrameBuffer from, FrameBuffer to, FrameBuffer output, float progress)
    {
        int h = output.Height;
        for (int x = 0; x < output.Width; x++)
        {
            float delay = DissolveTransition.Threshold(x, 0) * MaxDelay;
            float local = Math.Clamp((progress - delay) / (1f - MaxDelay), 0f, 1f);
            int revealed = (int)(local * h);
            for (int y = 0; y < h; y++)
                output.SetPixel(x, y, y < revealed ? to.GetPixel(x, y) : from.GetPixel(x, y));
            if (local > 0f && revealed < h) output.SetPixel(x, revealed, Head);
        }
    }
}
