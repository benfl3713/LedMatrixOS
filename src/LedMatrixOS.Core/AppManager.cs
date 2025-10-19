using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Core;

public sealed class AppManager
{
    private readonly IConfiguration _configuration;
    private readonly int _height;
    private readonly int _width;
    private readonly Dictionary<string, Type> _appsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettingsStorage? _settingsStorage;
    private IMatrixApp? _activeApp;

    public IEnumerable<Type> Apps => _appsById.Values;
    public IMatrixApp? ActiveApp => _activeApp;

    public event EventHandler<IMatrixApp>? AppActivated;

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

    public async Task<bool> ActivateAsync(string id, CancellationToken cancellationToken)
    {
        if (!_appsById.TryGetValue(id, out var next)) return false;

        // Save current app settings before switching
        if (_activeApp is IConfigurableApp currentConfigurable && _settingsStorage != null)
        {
            SaveCurrentAppSettings(currentConfigurable);
        }

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
        {
            RestoreAppSettings(nextConfigurable);
        }
        
        _activeApp = nextApp;

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
