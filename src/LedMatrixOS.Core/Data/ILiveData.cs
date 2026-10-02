namespace LedMatrixOS.Core.Data;

/// <summary>
/// A value that is refreshed in the background. All members are safe to read from any thread.
/// </summary>
public interface ILiveData<T>
{
    /// <summary>Latest successfully fetched value, or default until the first fetch succeeds.</summary>
    T? Value { get; }
    /// <summary>True while a fetch is in flight.</summary>
    bool IsLoading { get; }
    /// <summary>Error from the most recent fetch, cleared on success.</summary>
    Exception? Error { get; }
    /// <summary>When the last successful fetch completed, or null if none has.</summary>
    DateTimeOffset? LastUpdated { get; }
    /// <summary>Raised (on a background thread) whenever any of the above changes.</summary>
    event EventHandler? Changed;
}
