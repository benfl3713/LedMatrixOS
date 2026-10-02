namespace LedMatrixOS.Core;

public sealed record CrashInfo(string AppName, string Message);

/// <summary>
/// Tracks an app that threw while updating or rendering. While a crash is active the engine shows a crash card instead of the app,
/// then retries once <see cref="RetryAfter"/> has passed (a transient failure recovers on its own, a persistent one shows the card again).
/// </summary>
public sealed class CrashGuard
{
    private TimeSpan _retryAt;

    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(5);

    public CrashInfo? Current { get; private set; }

    public void Record(IMatrixApp app, Exception ex, TimeSpan now)
    {
        Current = new CrashInfo(app.Name, $"{ex.GetType().Name}: {ex.Message}");
        _retryAt = now + RetryAfter;
    }

    /// <summary>The crash to show at <paramref name="now"/>, or null when there is none or it is time to retry.</summary>
    public CrashInfo? Active(TimeSpan now)
    {
        if (Current != null && now >= _retryAt) Current = null;
        return Current;
    }

    public void Clear() => Current = null;
}
