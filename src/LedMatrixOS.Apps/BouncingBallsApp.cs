using LedMatrixOS.Apps.Toys;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Glossy balls that squash and stretch, leave glowing trails and burst into sparks when they collide, in a field whose gravity
/// occasionally swings to a new direction. The "lava" style swaps the balls for a lava-lamp of merging metaballs.
/// </summary>
public sealed class BouncingBallsApp : WidgetApp
{
    private BouncingBallsField? _field;

    public override string Id => "bouncing-balls";
    public override string Name => "Bouncing Balls";

    [Setting("Style", Description = "Glossy bouncing balls or a lava lamp of merging blobs", Options = ["balls", "lava"])]
    public string Style { get; set; } = "balls";

    [Setting("Count", Description = "How many balls (lava: how many blobs, roughly half)", Min = 1, Max = BouncingBallsField.MaxBalls)]
    public int Count { get; set; } = 10;

    [Setting("Gravity", Description = "0 is weightless, 100 is heavy", Min = 0, Max = 100)]
    public int Gravity { get; set; } = 60;

    [Setting("Trails", Description = "Glowing motion trails")]
    public bool Trails { get; set; } = true;

    [Setting("Palette", Description = "Colour palette", Options = ["neon", "sunset", "ocean", "candy", "aurora"])]
    public string Palette { get; set; } = "neon";

    /// <summary>The scene node (after the first frame).</summary>
    public BouncingBallsField? Field => _field;

    protected override Node Build()
    {
        _field = new BouncingBallsField();
        Apply();
        return _field;
    }

    protected override void OnSettingChanged(string key) => Apply();

    private void Apply()
    {
        if (_field is null) return;
        _field.Style = Style;
        _field.Count = Count;
        _field.Gravity = Gravity;
        _field.Trails = Trails;
        _field.PaletteName = Palette;
    }
}
