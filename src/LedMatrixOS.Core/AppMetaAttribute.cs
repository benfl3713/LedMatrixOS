namespace LedMatrixOS.Core;

/// <summary>Where an app is listed in the controller. Serialised lower-case by the API.</summary>
public enum AppCategory { Clocks, Travel, Info, Ambient, Games, Media, Developer, Other }

/// <summary>User-facing catalogue metadata for an app: its category and a short (about 50 chars) description, and optional search
/// synonyms: words people might type that the name and description do not say (such as "train" for a rail app).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AppMetaAttribute(AppCategory category, string description, params string[] keywords) : Attribute
{
    public AppCategory Category { get; } = category;
    public string Description { get; } = description;
    public string[] Keywords { get; } = keywords;
}
