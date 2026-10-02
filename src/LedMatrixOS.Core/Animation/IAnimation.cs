namespace LedMatrixOS.Core.Animation;

/// <summary>
/// Something that advances with time and eventually finishes. Implemented by <see cref="Tween{T}"/> and <see cref="Timeline"/>.
/// </summary>
public interface IAnimation
{
    bool IsFinished { get; }

    /// <summary>
    /// Advances by <paramref name="delta"/> and returns the part of it left unused because the animation finished part-way.
    /// </summary>
    TimeSpan Update(TimeSpan delta);

    void Cancel();
}
