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
        Options = ["auto", "spirograph", "polygons", "lissajous", "kaleidoscope", "tessellation"])]
    public string Pattern { get; set; } = "auto";

    [Setting("Palette", Description = "Colour palette", Options = ["neon", "sunset", "ocean", "candy", "aurora"])]
    public string Palette { get; set; } = "neon";

    [Setting("Speed", Description = "Animation speed in percent", Min = 10, Max = 400)]
    public int Speed { get; set; } = 100;

    [Setting("Interval", Description = "Seconds between patterns in auto mode", Min = 3, Max = 120)]
    public int Interval { get; set; } = 12;

    public GeometricField? Field => _field;

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
        _field.Speed = Speed;
        _field.Interval = Interval;
    }
}
