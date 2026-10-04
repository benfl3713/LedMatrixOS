namespace LedMatrixOS.Core;

public record AppSetting(string Key, string Name, string Description, AppSettingType Type, object DefaultValue, object CurrentValue, object? MinValue = null, object? MaxValue = null, string[]? Options = null,
    string? CurrentLabel = null, string[]? CurrentLabels = null);

public enum AppSettingType
{
    Boolean,
    Integer,
    String,
    Color,
    Select,
    /// <summary>One id picked through the live options endpoint; the label comes back as CurrentLabel.</summary>
    Search,
    /// <summary>A comma separated list of ids picked through the live options endpoint; labels come back as CurrentLabels.</summary>
    MultiSearch
}

