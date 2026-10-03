using System.Collections.Concurrent;
using LedMatrixOS.Core.Scheduling;

namespace LedMatrixOS.Apps.Attention;

/// <summary>
/// Base for attention sources backed by a remote poll. It polls only while some rule references it (see
/// <see cref="ILazyAttentionSource"/>), only for the referenced arguments, and answers <see cref="IsActive"/> from a cache.
/// Any failure (offline, bad id, parse error) drops that argument from the cache so the condition reads false; a cached
/// answer older than three poll intervals is treated as unknown (false) too. Nothing here logs request URLs, headers or tokens.
/// </summary>
public abstract class PollingAttentionSource : ILazyAttentionSource, IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly ConcurrentDictionary<string, (bool Active, DateTimeOffset At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private HashSet<string> _args = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    protected PollingAttentionSource(TimeProvider time, TimeSpan interval)
    {
        _time = time;
        _interval = interval;
    }

    public abstract string Kind { get; }

    /// <summary>False when the source cannot work (for example no credentials configured); it then never polls.</summary>
    protected virtual bool CanPoll => true;

    /// <summary>Fetches the answer for one referenced argument. Throw on any failure.</summary>
    protected abstract Task<bool> QueryAsync(string argument, CancellationToken ct);

    /// <summary>True while the background poll loop is running.</summary>
    public bool IsPolling { get { lock (_gate) return _cts != null; } }

    /// <summary>The arguments currently being polled.</summary>
    public IReadOnlyCollection<string> PolledArguments { get { lock (_gate) return _args.ToArray(); } }

    /// <summary>Number of completed refresh passes (for tests and diagnostics).</summary>
    public int Refreshes => Volatile.Read(ref _refreshes);
    private int _refreshes;

    public bool IsActive(string? argument)
    {
        if (argument is null || !_cache.TryGetValue(argument, out var entry)) return false;
        return entry.Active && _time.GetUtcNow() - entry.At <= _interval * 3;
    }

    public void SetReferenced(IReadOnlyCollection<string?> arguments)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (CanPoll)
            foreach (var a in arguments) wanted.Add(a ?? "");

        lock (_gate)
        {
            if (wanted.SetEquals(_args) && (_cts != null) == (wanted.Count > 0)) return;

            _cts?.Cancel();
            _cts = null;
            _args = wanted;

            foreach (var key in _cache.Keys.ToArray())
                if (!wanted.Contains(key)) _cache.TryRemove(key, out _);

            if (wanted.Count == 0) return;
            var cts = _cts = new CancellationTokenSource();
            var snapshot = wanted.ToArray();
            _ = Task.Run(() => LoopAsync(snapshot, cts.Token));
        }
    }

    /// <summary>One pass over the given arguments. Public for tests; the loop calls it on a timer.</summary>
    internal async Task RefreshAsync(IEnumerable<string> arguments, CancellationToken ct)
    {
        foreach (var arg in arguments)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                bool active = await QueryAsync(arg, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested) _cache[arg] = (active, _time.GetUtcNow());
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                _cache.TryRemove(arg, out _);
            }
        }
        Interlocked.Increment(ref _refreshes);
    }

    private async Task LoopAsync(string[] arguments, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await RefreshAsync(arguments, ct).ConfigureAwait(false);
                await Task.Delay(_interval, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* never escape the background task */ }
    }

    public void Dispose() => SetReferenced(Array.Empty<string?>());
}
