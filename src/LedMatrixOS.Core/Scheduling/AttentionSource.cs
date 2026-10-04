namespace LedMatrixOS.Core.Scheduling;

/// <summary>
/// Something that knows whether a real-world condition holds right now, so schedule rules can react to it
/// (a line is disrupted, music is playing, ...). Apps or services implement this and register it in DI as
/// <see cref="IAttentionSource"/>. Implementations must be cheap and thread-safe: the schedule polls them every second
/// and should answer from cached data rather than doing I/O.
/// </summary>
public interface IAttentionSource
{
    /// <summary>The condition kind this source answers, e.g. "spotify_playing" or "line_disrupted".</summary>
    string Kind { get; }

    /// <summary>
    /// True if the condition holds. <paramref name="argument"/> is whatever follows the first colon in the rule's
    /// condition ("victoria" for "line_disrupted:victoria"; "light.lamp=on" for "ha_state:light.lamp=on"), or null.
    /// </summary>
    bool IsActive(string? argument);
}

/// <summary>
/// Evaluates rule condition strings against the registered sources. A condition with no matching source, a source
/// that throws, or a condition that does not parse counts as not met, so a rule can never fire on missing data.
/// </summary>
public sealed class AttentionEvaluator
{
    private readonly Dictionary<string, IAttentionSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    public AttentionEvaluator(IEnumerable<IAttentionSource>? sources = null)
    {
        if (sources == null) return;
        foreach (var source in sources) _sources[source.Kind] = source;
    }

    /// <summary>True when a source is registered for this kind.</summary>
    public bool HasSource(string kind) => _sources.ContainsKey(kind);

    /// <summary>A null or blank condition is always met.</summary>
    public bool Evaluate(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        if (!AttentionCondition.TryParse(condition, out var parsed, out _)) return false;
        if (!_sources.TryGetValue(parsed.Kind, out var source)) return false;
        try { return source.IsActive(parsed.Argument); }
        catch { return false; }
    }
}
