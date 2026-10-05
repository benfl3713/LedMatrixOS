using LedMatrixOS.Apps.Aquarium;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>A calm fish tank: pixel fish, bubbles and swaying seaweed. Fully deterministic for a given time.</summary>
public sealed class AquariumApp : WidgetApp
{
    private AquariumField? _field;

    public override string Id => "aquarium";
    public override string Name => "Aquarium";
    public override int FrameRate => 30;

    [Setting("Fish", Description = "How many fish swim in the tank.", Min = 0, Max = AquariumField.MaxFish)]
    public int FishCount { get; set; } = 8;

    [Setting("Water", Description = "Colour of the water.", Options = ["Blue", "Teal", "Deep Sea", "Green", "Purple"])]
    public string Water { get; set; } = "Blue";

    protected override Node Build()
    {
        _field = new AquariumField { HAlign = Align.Stretch, VAlign = Align.Stretch };
        Apply();
        return _field;
    }

    protected override void OnSettingChanged(string key) => Apply();

    private void Apply()
    {
        if (_field is null) return;
        _field.FishCount = FishCount;
        var (top, bottom) = Water switch
        {
            "Teal" => (new Pixel(10, 130, 140), new Pixel(2, 40, 60)),
            "Deep Sea" => (new Pixel(8, 40, 100), new Pixel(0, 6, 28)),
            "Green" => (new Pixel(20, 120, 80), new Pixel(2, 40, 30)),
            "Purple" => (new Pixel(70, 40, 130), new Pixel(14, 6, 44)),
            _ => (new Pixel(10, 70, 130), new Pixel(2, 20, 60)),
        };
        _field.SetWater(top, bottom);
    }
}
