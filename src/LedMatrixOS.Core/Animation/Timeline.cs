namespace LedMatrixOS.Core.Animation;

/// <summary>
/// A composable, deterministic animation: delays, callbacks, tween steps, and sequences/parallels/repeats of those.
/// Time only moves when <see cref="Update"/> is called. A timeline starts on its first update and runs once;
/// build a new one to play again. Example: <c>Timeline.Sequence(fadeIn, Timeline.Delay(8.Seconds()), slideOut).Loop()</c>.
/// </summary>
public abstract class Timeline : IAnimation
{
    private static readonly TimeSpan Infinite = TimeSpan.MaxValue;

    private bool _started;
    private bool _cancelled;
    private bool _done;

    private Timeline()
    {
    }

    public bool IsFinished => _cancelled || _done;

    /// <summary>
    /// Plays the timelines one after another.
    /// </summary>
    public static Timeline Sequence(params Timeline[] items) => new SequenceNode(items);

    /// <summary>
    /// Plays the timelines at the same time; finishes when the longest one does.
    /// </summary>
    public static Timeline Parallel(params Timeline[] items) => new ParallelNode(items);

    public static Timeline Delay(TimeSpan duration) => new DelayNode(duration);

    /// <summary>
    /// Runs <paramref name="action"/> when playback reaches this step.
    /// </summary>
    public static Timeline Do(Action action) => new ActionNode(action);

    /// <summary>
    /// A step that tweens <paramref name="tween"/> to <paramref name="target"/> when playback reaches it.
    /// </summary>
    public static Timeline To<T>(Tween<T> tween, T target, TimeSpan duration, Func<float, float>? easing = null)
        => new TweenNode<T>(tween, target, duration, easing);

    /// <summary>
    /// Plays this timeline <paramref name="times"/> times in a row.
    /// </summary>
    public Timeline Repeat(int times)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);
        return new RepeatNode(this, times, false);
    }

    public Timeline Loop() => new RepeatNode(this, -1, false);

    /// <summary>
    /// Plays this timeline <paramref name="plays"/> times, alternating forward and reversed (so 2 is there and back).
    /// Reversed tween steps return to the value they started from; callbacks fire again in both directions.
    /// </summary>
    public Timeline Yoyo(int plays = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plays, 1);
        return new RepeatNode(this, plays, true);
    }

    public Timeline YoyoLoop() => new RepeatNode(this, -1, true);

    /// <summary>
    /// Advances by <paramref name="delta"/> and returns the part of it left over once the timeline finished.
    /// </summary>
    public TimeSpan Update(TimeSpan delta)
    {
        if (IsFinished) return delta;
        if (!_started)
        {
            _started = true;
            Start(false);
        }

        return Step(delta);
    }

    /// <summary>
    /// Stops the timeline (and any tween it is currently driving) where it is.
    /// </summary>
    public void Cancel()
    {
        if (IsFinished) return;
        _cancelled = true;
        Abort();
    }

    // Length of one play; Infinite for endless loops.
    private protected abstract TimeSpan Duration { get; }

    // Marks the node as running; reverse plays it backwards (used by yoyo).
    private protected abstract void Begin(bool reverse);

    // Consumes delta and returns the unused part. Sets _done when finished.
    private protected abstract TimeSpan Advance(TimeSpan delta);

    private protected virtual void Abort()
    {
    }

    private void Start(bool reverse)
    {
        _done = false;
        Begin(reverse);
    }

    private TimeSpan Step(TimeSpan delta) => _done ? delta : Advance(delta);

    private static TimeSpan Add(TimeSpan a, TimeSpan b)
        => a == Infinite || b == Infinite ? Infinite : a + b;

    private static TimeSpan Multiply(TimeSpan a, int times)
        => a == Infinite || a.Ticks > Infinite.Ticks / times ? Infinite : new TimeSpan(a.Ticks * times);

    private sealed class DelayNode : Timeline
    {
        private readonly TimeSpan _duration;
        private TimeSpan _elapsed;

        public DelayNode(TimeSpan duration) => _duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;

        private protected override TimeSpan Duration => _duration;

        private protected override void Begin(bool reverse)
        {
            _elapsed = TimeSpan.Zero;
            _done = _duration <= TimeSpan.Zero;
        }

        private protected override TimeSpan Advance(TimeSpan delta)
        {
            _elapsed += delta;
            if (_elapsed < _duration) return TimeSpan.Zero;
            _done = true;
            return _elapsed - _duration;
        }
    }

    private sealed class ActionNode : Timeline
    {
        private readonly Action _action;

        public ActionNode(Action action) => _action = action;

        private protected override TimeSpan Duration => TimeSpan.Zero;

        private protected override void Begin(bool reverse)
        {
        }

        private protected override TimeSpan Advance(TimeSpan delta)
        {
            _action();
            _done = true;
            return delta;
        }
    }

    private sealed class TweenNode<T> : Timeline
    {
        private readonly Tween<T> _tween;
        private readonly T _target;
        private readonly TimeSpan _duration;
        private readonly Func<float, float>? _easing;
        private T _start;

        public TweenNode(Tween<T> tween, T target, TimeSpan duration, Func<float, float>? easing)
        {
            _tween = tween;
            _target = target;
            _duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
            _easing = easing;
            _start = tween.Value;
        }

        private protected override TimeSpan Duration => _duration;

        private protected override void Begin(bool reverse)
        {
            if (reverse)
            {
                _tween.To(_start, _duration, _easing);
            }
            else
            {
                _start = _tween.Value;
                _tween.To(_target, _duration, _easing);
            }

            _done = !_tween.IsRunning;
        }

        private protected override TimeSpan Advance(TimeSpan delta)
        {
            var left = _tween.Update(delta);
            if (_tween.IsRunning) return TimeSpan.Zero;
            _done = true;
            return left;
        }

        private protected override void Abort() => _tween.Cancel();
    }

    private sealed class SequenceNode : Timeline
    {
        private readonly Timeline[] _children;
        private bool _reverse;
        private int _index;

        public SequenceNode(Timeline[] children) => _children = children;

        private protected override TimeSpan Duration
        {
            get
            {
                var total = TimeSpan.Zero;
                for (var i = 0; i < _children.Length; i++) total = Add(total, _children[i].Duration);
                return total;
            }
        }

        private Timeline Current => _children[_reverse ? _children.Length - 1 - _index : _index];

        private protected override void Begin(bool reverse)
        {
            _reverse = reverse;
            _index = 0;
            _done = _children.Length == 0;
            if (!_done) Current.Start(reverse);
        }

        private protected override TimeSpan Advance(TimeSpan delta)
        {
            while (true)
            {
                var child = Current;
                delta = child.Step(delta);
                if (!child._done) return TimeSpan.Zero;
                if (++_index >= _children.Length)
                {
                    _done = true;
                    return delta;
                }

                Current.Start(_reverse);
            }
        }

        private protected override void Abort()
        {
            if (!_done && _index < _children.Length) Current.Abort();
        }
    }

    private sealed class ParallelNode : Timeline
    {
        private readonly Timeline[] _children;
        private readonly TimeSpan[] _waits;
        private readonly bool[] _running;
        private bool _reverse;

        public ParallelNode(Timeline[] children)
        {
            _children = children;
            _waits = new TimeSpan[children.Length];
            _running = new bool[children.Length];
        }

        private protected override TimeSpan Duration
        {
            get
            {
                var longest = TimeSpan.Zero;
                for (var i = 0; i < _children.Length; i++)
                {
                    var d = _children[i].Duration;
                    if (d > longest) longest = d;
                }

                return longest;
            }
        }

        private protected override void Begin(bool reverse)
        {
            _reverse = reverse;
            _done = _children.Length == 0;
            var longest = Duration;
            for (var i = 0; i < _children.Length; i++)
            {
                _running[i] = false;
                // Reversed, children finish together at the start, so shorter ones begin later.
                _waits[i] = reverse && longest != Infinite ? longest - _children[i].Duration : TimeSpan.Zero;
            }
        }

        private protected override TimeSpan Advance(TimeSpan delta)
        {
            var allDone = true;
            var minLeft = Infinite;
            for (var i = 0; i < _children.Length; i++)
            {
                var child = _children[i];
                if (_running[i] && child._done) continue;

                var d = delta;
                if (!_running[i])
                {
                    if (_waits[i] > d)
                    {
                        _waits[i] -= d;
                        allDone = false;
                        continue;
                    }

                    d -= _waits[i];
                    _waits[i] = TimeSpan.Zero;
                    _running[i] = true;
                    child.Start(_reverse);
                }

                var left = child.Step(d);
                if (!child._done) allDone = false;
                else if (left < minLeft) minLeft = left;
            }

            if (!allDone) return TimeSpan.Zero;
            _done = true;
            return minLeft;
        }

        private protected override void Abort()
        {
            for (var i = 0; i < _children.Length; i++)
                if (_running[i] && !_children[i]._done) _children[i].Abort();
        }
    }

    private sealed class RepeatNode : Timeline
    {
        private readonly Timeline _child;
        private readonly int _count; // negative = forever
        private readonly bool _yoyo;
        private bool _reverse;
        private int _iteration;

        public RepeatNode(Timeline child, int count, bool yoyo)
        {
            _child = child;
            _count = count;
            _yoyo = yoyo;
        }

        private protected override TimeSpan Duration
            => _count < 0 ? Infinite : Multiply(_child.Duration, _count);

        private protected override void Begin(bool reverse)
        {
            _reverse = reverse;
            _iteration = 0;
            _child.Start(reverse);
        }

        private protected override TimeSpan Advance(TimeSpan delta)
        {
            while (true)
            {
                delta = _child.Step(delta);
                if (!_child._done) return TimeSpan.Zero;

                _iteration++;
                // A forever loop of zero-length steps would never consume time, so end it.
                if (_count >= 0 ? _iteration >= _count : _child.Duration <= TimeSpan.Zero)
                {
                    _done = true;
                    return delta;
                }

                _child.Start(_reverse ^ (_yoyo && (_iteration & 1) == 1));
            }
        }

        private protected override void Abort()
        {
            if (!_child._done) _child.Abort();
        }
    }
}
