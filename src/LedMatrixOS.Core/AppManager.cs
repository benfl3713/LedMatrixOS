using LedMatrixOS.Core.Overlays;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Core;

/// <summary>Static description of a registered app, available without activating it.</summary>
public sealed record AppInfo(string Id, string Name, bool HasSettings);

public enum SettingsStatus { Ok, NotFound, NotConfigurable }

public sealed record SettingsLookup(SettingsStatus Status, IReadOnlyList<AppSetting> Settings);

public sealed record SettingsUpdateResult(SettingsStatus Status, IReadOnlyList<string> RejectedKeys);

public sealed class AppManager
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly int _height;
    private readonly int _width;
    private readonly Dictionary<string, Type> _appsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AppInfo> _infoById = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettingsStorage? _settingsStorage;
    private IMatrixApp? _activeApp;

    public IEnumerable<Type> Apps => _appsById.Values;
    public IEnumerable<AppInfo> AppInfos => _infoById.Values;
    public IMatrixApp? ActiveApp => _activeApp;

    /// <summary>Handed to apps on activation so they can raise overlays (the engine's overlay manager).</summary>
    public IOverlayService? Overlays { get; set; }

    public event EventHandler<IMatrixApp>? AppActivated;

    public AppManager(IServiceProvider services, IConfiguration configuration, int height, int width, AppSettingsStorage? settingsStorage = null)
    {
        _services = services;
        _configuration = configuration;
        _height = height;
        _width = width;
        _settingsStorage = settingsStorage;
    }

    public void Register(Type app)
    {
        // validate if app implements IMatrixApp
        if (!typeof(IMatrixApp).IsAssignableFrom(app)) throw new ArgumentException("Type must implement IMatrixApp", nameof(app));

        var instance = Create(app);
        var id = instance.Id;

        _appsById[id] = app;
        _infoById[id] = new AppInfo(id, instance.Name, instance is IConfigurableApp);
    }

    // Apps are built through DI so their constructors can take services (HttpClient factory, AudioDataService, ...)
    private IMatrixApp Create(Type app) => (IMatrixApp)ActivatorUtilities.CreateInstance(_services, app);

    public async Task<bool> ActivateAsync(string id, CancellationToken cancellationToken)
    {
        if (!_appsById.TryGetValue(id, out var next)) return false;

        // Save current app settings before switching
        if (_activeApp is IConfigurableApp currentConfigurable && _settingsStorage != null)
        {
            SaveCurrentAppSettings(currentConfigurable);
        }

        // Create the new app instance first
        var nextApp = Create(next);
        if (nextApp is MatrixAppBase overlayAware) overlayAware.OverlayService = Overlays;
        
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

    /// <summary>
    /// Reads an app's settings whether or not it is active. For an inactive app a short-lived instance is built
    /// (never activated) and given the persisted values, so the active app and its lifecycle are untouched.
    /// </summary>
    public SettingsLookup GetSettings(string id)
    {
        if (!_appsById.TryGetValue(id, out var type)) return new SettingsLookup(SettingsStatus.NotFound, Array.Empty<AppSetting>());

        if (_activeApp != null && string.Equals(_activeApp.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            return _activeApp is IConfigurableApp active
                ? new SettingsLookup(SettingsStatus.Ok, active.GetSettings().ToList())
                : new SettingsLookup(SettingsStatus.NotConfigurable, Array.Empty<AppSetting>());
        }

        var instance = Create(type);
        try
        {
            if (instance is not IConfigurableApp configurable)
                return new SettingsLookup(SettingsStatus.NotConfigurable, Array.Empty<AppSetting>());
            if (_settingsStorage != null) RestoreAppSettings(configurable);
            return new SettingsLookup(SettingsStatus.Ok, configurable.GetSettings().ToList());
        }
        finally { (instance as IDisposable)?.Dispose(); }
    }

    /// <summary>
    /// Applies and persists settings for any registered app. Persisted values are restored the next time the app is
    /// activated. Returns the keys that were not recognised.
    /// </summary>
    public SettingsUpdateResult UpdateSettings(string id, IEnumerable<KeyValuePair<string, object>> updates)
    {
        if (!_appsById.TryGetValue(id, out var type)) return new SettingsUpdateResult(SettingsStatus.NotFound, Array.Empty<string>());

        if (_activeApp != null && string.Equals(_activeApp.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            if (_activeApp is not IConfigurableApp) return new SettingsUpdateResult(SettingsStatus.NotConfigurable, Array.Empty<string>());
            foreach (var (key, value) in updates) UpdateCurrentAppSetting(key, value);
            return new SettingsUpdateResult(SettingsStatus.Ok, Array.Empty<string>());
        }

        var instance = Create(type);
        try
        {
            if (instance is not IConfigurableApp configurable)
                return new SettingsUpdateResult(SettingsStatus.NotConfigurable, Array.Empty<string>());
            if (_settingsStorage != null) RestoreAppSettings(configurable);

            var rejected = new List<string>();
            foreach (var (key, value) in updates)
            {
                var before = configurable.GetSettings().FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                if (before == null) { rejected.Add(key); continue; }
                try { configurable.UpdateSetting(key, value); }
                catch { /* an app that was never activated may fail in its change hook; the value itself is read back below */ }

                var after = configurable.GetSettings().First(x => x.Key == before.Key);
                _settingsStorage?.UpdateAppSetting(configurable.Id, after.Key, after.CurrentValue);
            }
            return new SettingsUpdateResult(SettingsStatus.Ok, rejected);
        }
        finally { (instance as IDisposable)?.Dispose(); }
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
