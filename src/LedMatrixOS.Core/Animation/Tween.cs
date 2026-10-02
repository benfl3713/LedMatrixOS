namespace LedMatrixOS.Core.Animation;

/// <summary>
/// A value that animates toward a target over a duration. Time only moves when <see cref="Update"/> is called.
/// </summary>
public sealed class Tween<T> : IAnimation
{
    private readonly Func<T, T, float, T> _lerp;
    private T _from;
    private long _durationTicks;
    private long _elapsedTicks;
    private Func<float, float> _easing = Easing.Linear;
    private Action? _onComplete;

    public Tween(T initial)
        : this(initial, Lerper<T>.Lerp ?? throw new NotSupportedException($"No built-in interpolation for {typeof(T)}; pass a lerp function."))
    {
    }

    public Tween(T initial, Func<T, T, float, T> lerp)
    {
        _lerp = lerp;
        _from = initial;
        Value = initial;
        Target = initial;
    }

    public T Value { get; private set; }
    public T Target { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsFinished => !IsRunning;

    /// <summary>
    /// Animates from the current value to <paramref name="target"/>. Calling it mid-flight retargets from where the value is now, with no jump.
    /// A non-positive duration jumps straight to the target. A previous callback that has not fired is discarded.
    /// </summary>
    public Tween<T> To(T target, TimeSpan duration, Func<float, float>? easing = null, Action? onComplete = null)
    {
        _from = Value;
        Target = target;
        _durationTicks = duration.Ticks;
        _elapsedTicks = 0;
        _easing = easing ?? Easing.Linear;
        _onComplete = onComplete;

        if (_durationTicks <= 0)
        {
            Complete();
            return this;
        }

        IsRunning = true;
        return this;
    }

    /// <summary>
    /// Stops animating and sets the value immediately. The completion callback is dropped.
    /// </summary>
    public void Set(T value)
    {
        IsRunning = false;
        _onComplete = null;
        Value = value;
        Target = value;
        _from = value;
    }

    /// <summary>
    /// Stops animating, leaving the value where it is. The completion callback is dropped.
    /// </summary>
    public void Cancel()
    {
        IsRunning = false;
        _onComplete = null;
    }

    public TimeSpan Update(TimeSpan delta)
    {
        if (!IsRunning) return delta;

        _elapsedTicks += delta.Ticks;
        if (_elapsedTicks >= _durationTicks)
        {
            var leftover = new TimeSpan(_elapsedTicks - _durationTicks);
            Complete();
            return leftover;
        }

        var p = (float)((double)_elapsedTicks / _durationTicks);
        Value = _lerp(_from, Target, _easing(p));
        return TimeSpan.Zero;
    }

    private void Complete()
    {
        IsRunning = false;
        Value = Target;
        var callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }
}
