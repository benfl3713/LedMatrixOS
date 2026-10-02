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
}
