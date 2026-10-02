namespace LedMatrixOS.Core.Transitions;

public sealed class CrossfadeTransition : ITransition
{
    public string Name => "crossfade";

    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(500);

    public void Render(FrameBuffer from, FrameBuffer to, FrameBuffer output, float progress)
    {
        for (int y = 0; y < output.Height; y++)
            for (int x = 0; x < output.Width; x++)
                output.SetPixel(x, y, Pixel.Lerp(from.GetPixel(x, y), to.GetPixel(x, y), progress));
    }
}
