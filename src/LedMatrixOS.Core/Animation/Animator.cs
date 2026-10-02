namespace LedMatrixOS.Core.Animation;

/// <summary>
/// Owns an app's running tweens and timelines, advances them each frame and drops finished ones.
/// Not thread-safe: use it from the render thread only.
/// </summary>
public sealed class Animator
{
    private static readonly Predicate<IAnimation> IsFinishedPredicate = static a => a.IsFinished;

    private readonly List<IAnimation> _active = new();

    public int Count => _active.Count;

    /// <summary>
    /// Starts updating <paramref name="animation"/> each frame. Adding the same instance twice has no effect.
    /// </summary>
    public T Add<T>(T animation) where T : IAnimation
    {
        if (!_active.Contains(animation)) _active.Add(animation);
        return animation;
    }

    /// <summary>
    /// Animates <paramref name="tween"/> to <paramref name="target"/> and makes sure this animator is updating it.
    /// </summary>
    public Tween<T> Animate<T>(Tween<T> tween, T target, TimeSpan duration, Func<float, float>? easing = null, Action? onComplete = null)
    {
        tween.To(target, duration, easing, onComplete);
        if (tween.IsRunning) Add(tween);
        return tween;
    }

    /// <summary>
    /// Cancels and removes everything.
    /// </summary>
    public void Clear()
    {
        for (var i = 0; i < _active.Count; i++) _active[i].Cancel();
        _active.Clear();
    }

    public void Update(FrameContext ctx)
    {
        // Callbacks may Add or Clear, so re-check the bounds each step.
        var count = _active.Count;
        for (var i = 0; i < count && i < _active.Count; i++)
            _active[i].Update(ctx.Delta);

        _active.RemoveAll(IsFinishedPredicate);
    }
}
