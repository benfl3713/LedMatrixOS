namespace LedMatrixOS.Core.Animation;

/// <summary>
/// Readable duration literals: <c>250.Ms()</c>, <c>8.Seconds()</c>.
/// </summary>
public static class DurationExtensions
{
    public static TimeSpan Ms(this int value) => TimeSpan.FromMilliseconds(value);
    public static TimeSpan Ms(this double value) => TimeSpan.FromMilliseconds(value);
    public static TimeSpan Seconds(this int value) => TimeSpan.FromSeconds(value);
    public static TimeSpan Seconds(this double value) => TimeSpan.FromSeconds(value);
}
