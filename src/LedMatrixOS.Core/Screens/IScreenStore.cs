namespace LedMatrixOS.Core.Screens;

/// <summary>Where the running app finds screen definitions. Persistence is a separate concern (see the implementations).</summary>
public interface IScreenStore
{
    /// <summary>The screen with this id, or null.</summary>
    ScreenDefinition? TryGet(string id);

    /// <summary>A snapshot of every screen.</summary>
    IReadOnlyList<ScreenDefinition> All { get; }

    /// <summary>Raised after screens are added, replaced or removed.</summary>
    event Action? Changed;
}

/// <summary>Thread-safe in-memory <see cref="IScreenStore"/>.</summary>
public sealed class ScreenStore : IScreenStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ScreenDefinition> _screens = new(StringComparer.Ordinal);

    public event Action? Changed;

    public ScreenDefinition? TryGet(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return _screens.GetValueOrDefault(id);
    }

    public IReadOnlyList<ScreenDefinition> All
    {
        get { lock (_lock) return _screens.Values.ToArray(); }
    }

    /// <summary>Adds the screen, or replaces the one with the same id.</summary>
    public void Replace(ScreenDefinition screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        lock (_lock) _screens[screen.Id] = screen;
        Changed?.Invoke();
    }

    /// <summary>Replaces the whole set.</summary>
    public void ReplaceAll(IEnumerable<ScreenDefinition> screens)
    {
        lock (_lock)
        {
            _screens.Clear();
            foreach (var s in screens) _screens[s.Id] = s;
        }
        Changed?.Invoke();
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_lock) removed = _screens.Remove(id);
        if (removed) Changed?.Invoke();
        return removed;
    }
}
