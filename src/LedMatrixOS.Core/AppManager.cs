using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Core;

public sealed class AppManager
{
    private readonly IConfiguration _configuration;
    private readonly int _height;
    private readonly int _width;
    private readonly Dictionary<string, Type> _appsById = new(StringComparer.OrdinalIgnoreCase);
    private IMatrixApp? _activeApp;

    public IEnumerable<Type> Apps => _appsById.Values;
    public IMatrixApp? ActiveApp => _activeApp;

    public event EventHandler<IMatrixApp>? AppActivated;

    public AppManager(IConfiguration configuration, int height, int width)
    {
        _configuration = configuration;
        _height = height;
        _width = width;
    }

    public void Register(Type app)
    {
        // validate if app implements IMatrixApp
        if (!typeof(IMatrixApp).IsAssignableFrom(app)) throw new ArgumentException("Type must implement IMatrixApp", nameof(app));

        var instance = (IMatrixApp?)Activator.CreateInstance(app);
        if (instance == null) throw new InvalidOperationException("Failed to create instance of app");

        var id = instance.Id;

        _appsById[id] = app;
    }

    public async Task<bool> ActivateAsync(string id, CancellationToken cancellationToken)
    {
        if (!_appsById.TryGetValue(id, out var next)) return false;

        if (_activeApp != null)
        {
            try { await _activeApp.OnDeactivatedAsync(cancellationToken).ConfigureAwait(false); }
            catch { /* swallow app errors on deactivate */ }
        }

        var nextApp = (IMatrixApp?)Activator.CreateInstance(next);
        await nextApp.OnActivatedAsync((_height, _width), _configuration, cancellationToken).ConfigureAwait(false);
        _activeApp = nextApp;

        // Raise the AppActivated event
        AppActivated?.Invoke(this, nextApp);

        return true;
    }
}

