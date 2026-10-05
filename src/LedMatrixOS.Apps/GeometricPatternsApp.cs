using LedMatrixOS.Apps.Toys;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Hypnotic neon geometry: breathing spirographs, a twisting polygon tunnel, Lissajous figures, a mirrored kaleidoscope and a rippling
/// hex tessellation. In "auto" the styles take turns, blending into each other with a random screen transition.
/// </summary>
public sealed class GeometricPatternsApp : WidgetApp
{
    private GeometricField? _field;

    public override string Id => "geometric-patterns";
    public override string Name => "Geometric Patterns";

    [Setting("Pattern", Description = "Which geometry to show; auto cycles through all of them",
        Options = ["Auto", "Spirograph", "Polygons", "Lissajous", "Kaleidoscope", "Tessellation"])]
    public string Pattern { get; set; } = "Auto";

    [Setting("Palette", Description = "Colour palette", Options = ["Neon", "Sunset", "Ocean", "Candy", "Aurora", "Rainbow", "Mono"])]
    public string Palette { get; set; } = "Neon";

    [Setting("Speed", Description = "Animation speed (1-10, 5 is normal)", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Interval", Description = "Seconds between patterns in auto mode", Min = 3, Max = 120)]
    public int Interval { get; set; } = 12;

    public GeometricField? Field => _field;

    /// <summary>Old stored speeds were percentages (10-400); they are migrated onto the 1-10 levels.</summary>
    public override void UpdateSetting(string key, object value) =>
        base.UpdateSetting(key, string.Equals(key, "speed", StringComparison.OrdinalIgnoreCase) ? ToySpeed.Migrate(value) : value);

    protected override Node Build()
    {
        _field = new GeometricField();
        Apply();
        return _field;
    }

    protected override void OnSettingChanged(string key) => Apply();

    private void Apply()
    {
        if (_field is null) return;
        _field.Pattern = Pattern;
        _field.PaletteName = Palette;
        _field.Speed = ToySpeed.Percent(Speed);
        _field.Interval = Interval;
    }
}
