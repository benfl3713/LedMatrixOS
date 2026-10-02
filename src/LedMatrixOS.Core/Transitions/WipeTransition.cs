using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Transitions;

/// <summary>
/// A hard edge sweeps across the screen revealing the new frame. The direction is the way the edge travels
/// (<see cref="MoveDirection.Right"/> reveals from the left side first).
/// </summary>
public sealed class WipeTransition(MoveDirection direction) : ITransition
{
    public string Name { get; } = "wipe-" + direction.ToString().ToLowerInvariant();

    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(500);

    public void Render(FrameBuffer from, FrameBuffer to, FrameBuffer output, float progress)
    {
        int w = output.Width, h = output.Height;
        progress = Math.Clamp(progress, 0f, 1f);
        output.CopyFrom(from);

        Rectangle revealed;
        switch (direction)
        {
            case MoveDirection.Right:
                revealed = new Rectangle(0, 0, (int)MathF.Round(progress * w), h);
                break;
            case MoveDirection.Left:
            {
                int n = (int)MathF.Round(progress * w);
                revealed = new Rectangle(w - n, 0, n, h);
                break;
            }
            case MoveDirection.Down:
                revealed = new Rectangle(0, 0, w, (int)MathF.Round(progress * h));
                break;
            default:
            {
                int n = (int)MathF.Round(progress * h);
                revealed = new Rectangle(0, h - n, w, n);
                break;
            }
        }

        if (revealed.Width <= 0 || revealed.Height <= 0) return;
        output.PushClip(revealed);
        output.CopyFrom(to);
        output.PopClip();
    }
}
