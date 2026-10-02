namespace LedMatrixOS.Core.Data;

internal sealed class PollingLiveData<T> : ILiveData<T>
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task<T>> _fetch;
    private T? _value;
    private bool _isLoading;
    private Exception? _error;
    private DateTimeOffset? _lastUpdated;

    public PollingLiveData(TimeSpan interval, Func<CancellationToken, Task<T>> fetch)
    {
        _interval = interval;
        _fetch = fetch;
    }

    public T? Value { get { lock (_lock) return _value; } }
    public bool IsLoading { get { lock (_lock) return _isLoading; } }
    public Exception? Error { get { lock (_lock) return _error; } }
    public DateTimeOffset? LastUpdated { get { lock (_lock) return _lastUpdated; } }

    public event EventHandler? Changed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                Mutate(() => _isLoading = true);
                var result = await _fetch(cancellationToken).ConfigureAwait(false);
                Mutate(() =>
                {
                    _value = result;
                    _error = null;
                    _lastUpdated = DateTimeOffset.UtcNow;
                    _isLoading = false;
                });
                failures = 0;
                delay = _interval;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Mutate(() =>
                {
                    _error = ex;
                    _isLoading = false;
                });
                failures++;
                // Exponential backoff, but never wait longer than a normal poll
                var backoff = TimeSpan.FromTicks(InitialBackoff.Ticks << Math.Min(failures - 1, 10));
                delay = TimeSpan.FromTicks(Math.Min(Math.Min(backoff.Ticks, MaxBackoff.Ticks), _interval.Ticks));
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Mutate(Action change)
    {
        lock (_lock) change();
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Console.WriteLine(ex); }
    }
}
