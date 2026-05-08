namespace LedMatrixOS.Engine;

/// <summary>
/// Holds per-ID factory functions for <see cref="IMatrixScene"/> instances.
/// Register all scenes at startup; <see cref="MatrixGame"/> uses this to
/// instantiate the requested scene on each activation.
/// </summary>
public sealed class SceneRegistry
{
    private readonly Dictionary<string, Func<IMatrixScene>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers <typeparamref name="TScene"/> using a parameterless constructor factory.
    /// The scene's <see cref="IMatrixScene.Id"/> is read from a temporary instance.
    /// </summary>
    public void Register<TScene>() where TScene : IMatrixScene, new()
    {
        var id = new TScene().Id;
        _factories[id] = static () => new TScene();
    }

    /// <summary>
    /// Registers a scene using a custom factory (useful when constructor injection is needed).
    /// </summary>
    public void Register(string id, Func<IMatrixScene> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[id] = factory;
    }

    /// <summary>Returns all registered scene IDs.</summary>
    public IEnumerable<string> RegisteredIds => _factories.Keys;

    internal bool TryCreate(string id, out IMatrixScene? scene)
    {
        if (_factories.TryGetValue(id, out var factory))
        {
            scene = factory();
            return true;
        }

        scene = null;
        return false;
    }
}
