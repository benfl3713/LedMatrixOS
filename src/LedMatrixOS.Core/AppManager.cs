using LedMatrixOS.Core.Overlays;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Core;

/// <summary>Static description of a registered app, available without activating it.</summary>
public sealed record AppInfo(string Id, string Name, bool HasSettings);

/// <summary>A user-created screen: an alias of a registered app with a preset, listed alongside the built-in apps.</summary>
public sealed record ScreenInfo(string Id, string Name, string TargetId, bool HasSettings);

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
    private readonly Dictionary<string, (string TargetId, IReadOnlyDictionary<string, object> Preset, string? DisplayName, bool IsScreen)> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private IMatrixApp? _activeApp;
    private string? _activeKey; // the id settings are persisted under: the requested id, so an alias keeps its own saved profile

    public IEnumerable<Type> Apps => _appsById.Values;
    /// <summary>Retired ids that still resolve to a registered app; not part of <see cref="AppInfos"/>.</summary>
    public IEnumerable<string> AliasIds => _aliases.Keys;
    public IEnumerable<AppInfo> AppInfos => _infoById.Values;
    public IMatrixApp? ActiveApp => _activeApp;

    /// <summary>
    /// The id the active app was activated under: the alias id for an alias (ActiveApp.Id is the target's id), otherwise
    /// the app's own id. Null when nothing is active.
    /// </summary>
    public string? ActiveAppId => _activeApp == null ? null : _activeKey ?? _activeApp.Id;

    /// <summary>User-created screens (aliases registered with isScreen: true). Built-in aliases are never listed.</summary>
    public IEnumerable<ScreenInfo> Screens => _aliases
        .Where(a => a.Value.IsScreen && _infoById.ContainsKey(a.Value.TargetId))
        .Select(a => new ScreenInfo(a.Key, a.Value.DisplayName ?? a.Key, a.Value.TargetId, _infoById[a.Value.TargetId].HasSettings));

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

    /// <summary>
    /// Makes <paramref name="alias"/> an alternative id for the registered app <paramref name="targetId"/>. Activating or
    /// configuring the alias uses the target app with <paramref name="preset"/> applied on top of the alias's own persisted
    /// settings (stored under the alias id, so existing app-settings.json entries keep applying).
    /// </summary>
    public void RegisterAlias(string alias, string targetId, IReadOnlyDictionary<string, object> preset, string? displayName = null, bool isScreen = false)
    {
        if (!_appsById.ContainsKey(targetId)) throw new ArgumentException($"Unknown target app '{targetId}'", nameof(targetId));
        if (_appsById.ContainsKey(alias)) throw new ArgumentException($"'{alias}' is already a registered app", nameof(alias));
        _aliases[alias] = (targetId, preset, displayName, isScreen); // re-registering replaces the alias in place (editing a screen)
    }

    /// <summary>
    /// Removes an alias. Returns false if it does not exist. If the alias is currently active its instance is left
    /// running until something else is activated (it already holds its preset); it just stops resolving for new activations.
    /// Persisted settings under the alias id are not deleted.
    /// </summary>
    public bool UnregisterAlias(string alias) => _aliases.Remove(alias);

    private bool TryResolve(string id, out Type type, out string key, out IReadOnlyDictionary<string, object>? preset)
    {
        if (_appsById.TryGetValue(id, out type!)) { key = _infoById[id].Id; preset = null; return true; }
        if (_aliases.TryGetValue(id, out var alias) && _appsById.TryGetValue(alias.TargetId, out type!))
        {
            key = id; preset = alias.Preset; return true;
        }
        key = id; preset = null; type = null!;
        return false;
    }

    private static void ApplyPreset(IMatrixApp app, IReadOnlyDictionary<string, object>? preset)
    {
        if (preset == null || app is not IConfigurableApp configurable) return;
        foreach (var (key, value) in preset)
        {
            try { configurable.UpdateSetting(key, value); } catch { /* a preset must never stop an app starting */ }
        }
    }

    // Apps are built through DI so their constructors can take services (HttpClient factory, AudioDataService, ...)
    private IMatrixApp Create(Type app) => (IMatrixApp)ActivatorUtilities.CreateInstance(_services, app);

    public async Task<bool> ActivateAsync(string id, CancellationToken cancellationToken)
    {
        if (!TryResolve(id, out var next, out var key, out var preset)) return false;

        // Save current app settings before switching
        if (_activeApp is IConfigurableApp currentConfigurable && _settingsStorage != null)
        {
            SaveCurrentAppSettings(currentConfigurable);
        }

        // Create the new app instance first
        var nextApp = Create(next);
        if (nextApp is MatrixAppBase overlayAware) overlayAware.OverlayService = Overlays;
        ApplyPreset(nextApp, preset); // before activation so the first build already has the right shape
        
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
            RestoreAppSettings(nextConfigurable, key);
            ApplyPreset(nextApp, preset);
        }

        _activeApp = nextApp;
        _activeKey = key;

        return true;
    }

    public void UpdateCurrentAppSetting(string key, object value)
    {
        if (_activeApp is IConfigurableApp configurableApp && _settingsStorage != null)
        {
            configurableApp.UpdateSetting(key, value);
            _settingsStorage.UpdateAppSetting(_activeKey ?? _activeApp.Id, key, value);
        }
    }

    /// <summary>
    /// Reads an app's settings whether or not it is active. For an inactive app a short-lived instance is built
    /// (never activated) and given the persisted values, so the active app and its lifecycle are untouched.
    /// </summary>
    public SettingsLookup GetSettings(string id)
    {
        if (!TryResolve(id, out var type, out var key, out var preset)) return new SettingsLookup(SettingsStatus.NotFound, Array.Empty<AppSetting>());

        if (_activeApp != null && string.Equals(_activeKey, key, StringComparison.OrdinalIgnoreCase))
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
            ApplyPreset(instance, preset);
            if (_settingsStorage != null) RestoreAppSettings(configurable, key);
            ApplyPreset(instance, preset);
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
        if (!TryResolve(id, out var type, out var storageKey, out var preset)) return new SettingsUpdateResult(SettingsStatus.NotFound, Array.Empty<string>());

        if (_activeApp != null && string.Equals(_activeKey, storageKey, StringComparison.OrdinalIgnoreCase))
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
            ApplyPreset(instance, preset);
            if (_settingsStorage != null) RestoreAppSettings(configurable, storageKey);
            ApplyPreset(instance, preset);

            var rejected = new List<string>();
            foreach (var (key, value) in updates)
            {
                var before = configurable.GetSettings().FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                if (before == null) { rejected.Add(key); continue; }
                try { configurable.UpdateSetting(key, value); }
                catch { /* an app that was never activated may fail in its change hook; the value itself is read back below */ }

                var after = configurable.GetSettings().First(x => x.Key == before.Key);
                _settingsStorage?.UpdateAppSetting(storageKey, after.Key, after.CurrentValue);
            }
            return new SettingsUpdateResult(SettingsStatus.Ok, rejected);
        }
        finally { (instance as IDisposable)?.Dispose(); }
    }

    private void SaveCurrentAppSettings(IConfigurableApp app)
    {
        var settings = app.GetSettings().ToDictionary(s => s.Key, s => s.CurrentValue);
        _settingsStorage!.SaveAppSettings(_activeKey ?? ((IMatrixApp)app).Id, settings);
    }

    private void RestoreAppSettings(IConfigurableApp app, string storageKey)
    {
        var savedSettings = _settingsStorage!.GetAppSettings(storageKey);
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
