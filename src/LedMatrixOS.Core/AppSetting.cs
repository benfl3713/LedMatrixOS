namespace LedMatrixOS.Core;

public record AppSetting(string Key, string Name, string Description, AppSettingType Type, object DefaultValue, object CurrentValue, object? MinValue = null, object? MaxValue = null, string[]? Options = null);

public enum AppSettingType
{
    Boolean,
    Integer,
    String,
    Color,
    Select
}

