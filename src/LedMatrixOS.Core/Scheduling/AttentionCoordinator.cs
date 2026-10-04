namespace LedMatrixOS.Core.Scheduling;

/// <summary>
/// An attention source that only does work (polling, connections) while some schedule rule references its condition kind.
/// <see cref="AttentionCoordinator"/> tells it which arguments are referenced; an empty collection means "stop everything".
/// </summary>
public interface ILazyAttentionSource : IAttentionSource
{
    /// <summary>
    /// The arguments of every rule condition of this source's <see cref="IAttentionSource.Kind"/> (null for kinds without an
    /// argument). Called again whenever the schedule is reloaded. Must not block or throw.
    /// </summary>
    void SetReferenced(IReadOnlyCollection<string?> arguments);
}

/// <summary>
/// Keeps lazy attention sources in step with the schedule: after each (re)load of the schedule it hands every source the
/// condition arguments that rules currently reference, so nothing is polled unless a rule needs it.
/// </summary>
public sealed class AttentionCoordinator : IDisposable
{
    private readonly ScheduleService _schedule;
    private readonly ILazyAttentionSource[] _sources;
    private bool _started;

    public AttentionCoordinator(ScheduleService schedule, IEnumerable<IAttentionSource> sources)
    {
        _schedule = schedule;
        _sources = sources.OfType<ILazyAttentionSource>().ToArray();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _schedule.Changed += Sync;
        Sync();
    }

    /// <summary>Recomputes the referenced conditions and passes them to the sources.</summary>
    public void Sync()
    {
        IReadOnlyList<AttentionCondition> conditions;
        lock (_schedule.Gate) conditions = _schedule.ReferencedConditions();

        foreach (var source in _sources)
        {
            var args = conditions
                .Where(c => string.Equals(c.Kind, source.Kind, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Argument)
                .ToList();
            try { source.SetReferenced(args); }
            catch { /* a misbehaving source must never break a schedule reload */ }
        }
    }

    public void Dispose()
    {
        if (_started) _schedule.Changed -= Sync;
        _started = false;
        foreach (var source in _sources)
        {
            try { source.SetReferenced(Array.Empty<string?>()); } catch { }
        }
    }
}
