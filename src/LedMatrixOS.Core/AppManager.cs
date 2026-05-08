using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Core;

public sealed class AppManager
{
    private readonly IConfiguration _configuration;
    private readonly int _height;
    private readonly int _width;
    private readonly Dictionary<string, Type> _appsById = new(StringComparer.OrdinalIgnoreCase);

    // Scene (MonoGame) registrations — parallel path to the legacy IMatrixApp system.
    // Types here implement IMatrixScene (in LedMatrixOS.Engine) but are stored as
    // plain Type to keep LedMatrixOS.Core free of a MonoGame dependency.
    private readonly Dictionary<string, Type> _scenesById = new(StringComparer.OrdinalIgnoreCase);

    private readonly AppSettingsStorage? _settingsStorage;
    private IMatrixApp? _activeApp;
    private string? _activeSceneId;

    /// <summary>All registered app/scene types (legacy IMatrixApp + new IMatrixScene).</summary>
    public IEnumerable<Type> Apps => _appsById.Values.Concat(_scenesById.Values);

    public IMatrixApp? ActiveApp => _activeApp;

    /// <summary>ID of the currently active MonoGame scene, or <c>null</c> if a legacy app is active.</summary>
    public string? ActiveSceneId => _activeSceneId;

    /// <summary>Raised by the legacy render engine when an IMatrixApp is activated.</summary>
    public event EventHandler<IMatrixApp>? AppActivated;

    /// <summary>
    /// Raised when a MonoGame scene should be made active.
    /// The <see cref="MatrixGame"/> subscribes and handles instantiation on the game thread.
    /// </summary>
    public event EventHandler<string>? SceneActivationRequested;

    public AppManager(IConfiguration configuration, int height, int width, AppSettingsStorage? settingsStorage = null)
    {
        _configuration = configuration;
        _height = height;
        _width = width;
        _settingsStorage = settingsStorage;
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

    /// <summary>
    /// Registers a MonoGame scene type by providing the ID explicitly.
    /// Use this overload when the scene type requires constructor parameters.
    /// </summary>
    public void RegisterScene(string id, Type sceneType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(sceneType);
        _scenesById[id] = sceneType;
    }

    /// <summary>
    /// Registers a MonoGame scene type.
    /// The type must have public <c>Id</c> and <c>Name</c> string properties and a
    /// public parameterless constructor.
    /// </summary>
    public void RegisterScene(Type sceneType)
    {
        var idProp = sceneType.GetProperty("Id")
            ?? throw new ArgumentException($"Scene type '{sceneType.Name}' must expose a public 'Id' property.", nameof(sceneType));

        var instance = Activator.CreateInstance(sceneType)
            ?? throw new InvalidOperationException($"Failed to create instance of scene type '{sceneType.Name}'.");

        var id = idProp.GetValue(instance) as string
            ?? throw new InvalidOperationException($"Scene '{sceneType.Name}'.Id returned null.");

        _scenesById[id] = sceneType;
    }

    public async Task<bool> ActivateAsync(string id, CancellationToken cancellationToken)
    {
        // ── MonoGame scene path ────────────────────────────────────────────────
        if (_scenesById.ContainsKey(id))
        {
            // Save and deactivate any currently running legacy app
            if (_activeApp is IConfigurableApp currentConfigurable && _settingsStorage != null)
                SaveCurrentAppSettings(currentConfigurable);

            if (_activeApp != null)
            {
                try { await _activeApp.OnDeactivatedAsync(cancellationToken).ConfigureAwait(false); }
                catch { /* swallow */ }
                _activeApp = null;
            }

            _activeSceneId = id;
            SceneActivationRequested?.Invoke(this, id);
            return true;
        }

        // ── Legacy IMatrixApp path ─────────────────────────────────────────────
        if (!_appsById.TryGetValue(id, out var next)) return false;

        // Save current app settings before switching
        if (_activeApp is IConfigurableApp legacyConfigurable && _settingsStorage != null)
            SaveCurrentAppSettings(legacyConfigurable);

        // Create the new app instance first
        var nextApp = (IMatrixApp?)Activator.CreateInstance(next);
        if (nextApp == null) return false;

        // Raise the AppActivated event BEFORE switching, so RenderEngine can capture the old frame
        AppActivated?.Invoke(this, nextApp);

        if (_activeApp != null)
        {
            try { await _activeApp.OnDeactivatedAsync(cancellationToken).ConfigureAwait(false); }
            catch { /* swallow app errors on deactivate */ }
        }

        await nextApp.OnActivatedAsync((_height, _width), _configuration, cancellationToken).ConfigureAwait(false);

        // Restore settings for the new app
        if (nextApp is IConfigurableApp nextConfigurable && _settingsStorage != null)
            RestoreAppSettings(nextConfigurable);

        _activeApp = nextApp;
        _activeSceneId = null;

        return true;
    }

    public void UpdateCurrentAppSetting(string key, object value)
    {
        if (_activeApp is IConfigurableApp configurableApp && _settingsStorage != null)
        {
            configurableApp.UpdateSetting(key, value);
            _settingsStorage.UpdateAppSetting(_activeApp.Id, key, value);
        }
    }

    private void SaveCurrentAppSettings(IConfigurableApp app)
    {
        var settings = app.GetSettings().ToDictionary(s => s.Key, s => s.CurrentValue);
        _settingsStorage!.SaveAppSettings(app.Id, settings);
    }

    private void RestoreAppSettings(IConfigurableApp app)
    {
        var savedSettings = _settingsStorage!.GetAppSettings(app.Id);
        if (savedSettings != null)
        {
            foreach (var (key, value) in savedSettings)
            {
                try
                {
                    app.UpdateSetting(key, value);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to restore setting '{key}' for app '{app.Id}': {ex.Message}");
                }
            }
        }
    }
}
