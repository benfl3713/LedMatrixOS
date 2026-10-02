namespace LedMatrixOS.Core.Settings;

/// <summary>
/// Base class for apps whose settings are declared with [Setting] properties.
/// Implements <see cref="IConfigurableApp"/> so the property attributes are all an app needs.
/// </summary>
public abstract class SettingsAppBase : MatrixAppBase, IConfigurableApp
{
    public IEnumerable<AppSetting> GetSettings() => SettingsBinder.GetSettings(this);

    public void UpdateSetting(string key, object value)
    {
        if (SettingsBinder.TryUpdate(this, key, value))
        {
            OnSettingChanged(key);
        }
    }

    /// <summary>Called after a setting has been applied, so the app can react (e.g. rebuild cached state).</summary>
    protected virtual void OnSettingChanged(string key)
    {
    }
}
