namespace LedMatrixOS.Core.Transitions;

/// <summary>
/// Blends the outgoing frame into the incoming one. Implementations must be deterministic and must not
/// allocate per frame; they write every pixel of <c>output</c>.
/// </summary>
public interface ITransition
{
    string Name { get; }

    TimeSpan Duration { get; }

    /// <param name="progress">0 (only <paramref name="from"/>) to 1 (only <paramref name="to"/>), already eased.</param>
    void Render(FrameBuffer from, FrameBuffer to, FrameBuffer output, float progress);
}

public static class TransitionEasing
{
    public static float Linear(float t) => t;

    public static float OutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }
}

/// <summary>Direction of travel for slide and wipe transitions.</summary>
public enum MoveDirection
{
    Up,
    Down,
    Left,
    Right
}
