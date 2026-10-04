namespace LedMatrixOS.Core.Settings;

/// <summary>
/// Marks a public property of a <see cref="SettingsAppBase"/> as a user-editable setting.
/// The setting key is the property name in camelCase.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute : Attribute
{
    public SettingAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public string Description { get; set; } = "";
    /// <summary>Inclusive lower bound for integer settings (an int constant).</summary>
    public object? Min { get; set; }
    /// <summary>Inclusive upper bound for integer settings (an int constant).</summary>
    public object? Max { get; set; }
    /// <summary>Allowed values; makes a string setting a Select.</summary>
    public string[]? Options { get; set; }
    /// <summary>Makes a string setting a Search: one id picked through the options endpoint (see <see cref="SettingOptionsRegistry"/>).</summary>
    public bool Search { get; set; }
    /// <summary>
    /// Makes a string setting a MultiSearch: a comma separated list of ids picked through the options endpoint.
    /// <see cref="Max"/> (an int) limits how many ids are kept.
    /// </summary>
    public bool MultiSearch { get; set; }
    /// <summary>
    /// With <see cref="Search"/>/<see cref="MultiSearch"/>: the options are a short list the client shows straight away (no typing needed),
    /// computed from the app's other settings.
    /// </summary>
    public bool Browse { get; set; }
    /// <summary>Rarely used or raw setting: clients may hide it under an "Advanced" section.</summary>
    public bool Advanced { get; set; }
}

/// <summary>
/// Declares a setting key an app used to have, so persisted values, schedule presets and clients that still post it keep working.
/// With <c>mapsTo</c> the value is applied to that setting (an "id | name" value is reduced to the id, and a MultiSearch target gets
/// the id added); without it the value is accepted and ignored.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class LegacySettingKeyAttribute(string key, string? mapsTo = null) : Attribute
{
    public string Key { get; } = key;
    public string? MapsTo { get; } = mapsTo;
}
