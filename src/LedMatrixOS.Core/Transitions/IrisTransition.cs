namespace LedMatrixOS.Core.Transitions;

/// <summary>A circle grows from the centre revealing the new frame.</summary>
public sealed class IrisTransition : ITransition
{
    public string Name => "iris";

    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(600);

    public void Render(FrameBuffer from, FrameBuffer to, FrameBuffer output, float progress)
    {
        float cx = output.Width / 2f, cy = output.Height / 2f;
        float r = Math.Clamp(progress, 0f, 1f) * MathF.Sqrt(cx * cx + cy * cy);
        float r2 = r * r;
        for (int y = 0; y < output.Height; y++)
        {
            float dy = y + 0.5f - cy;
            for (int x = 0; x < output.Width; x++)
            {
                float dx = x + 0.5f - cx;
                output.SetPixel(x, y, r > 0f && dx * dx + dy * dy <= r2 ? to.GetPixel(x, y) : from.GetPixel(x, y));
            }
        }
    }
}
