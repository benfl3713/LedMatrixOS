namespace LedMatrixOS.Core.Transitions;

/// <summary>
/// Both frames move together. <see cref="MoveDirection.Up"/> pushes the old frame off the top while the
/// new one enters from the bottom; the other directions mirror that.
/// </summary>
public sealed class SlideTransition(MoveDirection direction) : ITransition
{
    public string Name { get; } = "slide-" + direction.ToString().ToLowerInvariant();

    public TimeSpan Duration { get; init; } =
        direction is MoveDirection.Up or MoveDirection.Down
            ? TimeSpan.FromMilliseconds(400)
            : TimeSpan.FromMilliseconds(600);

    public void Render(FrameBuffer from, FrameBuffer to, FrameBuffer output, float progress)
    {
        int w = output.Width, h = output.Height;
        switch (direction)
        {
            case MoveDirection.Up:
            {
                int o = Offset(progress, h);
                output.CopyFrom(from, 0, -o);
                output.CopyFrom(to, 0, h - o);
                break;
            }
            case MoveDirection.Down:
            {
                int o = Offset(progress, h);
                output.CopyFrom(from, 0, o);
                output.CopyFrom(to, 0, o - h);
                break;
            }
            case MoveDirection.Left:
            {
                int o = Offset(progress, w);
                output.CopyFrom(from, -o, 0);
                output.CopyFrom(to, w - o, 0);
                break;
            }
            default:
            {
                int o = Offset(progress, w);
                output.CopyFrom(from, o, 0);
                output.CopyFrom(to, o - w, 0);
                break;
            }
        }
    }

    private static int Offset(float progress, int size) =>
        (int)MathF.Round(Math.Clamp(progress, 0f, 1f) * size);
}
