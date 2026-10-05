using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Hypnotic demoscene shader: counter-rotating log-spiral vortices over a drifting plasma that warps their arms, a cyclic colour
/// palette with a complementary hue in the gaps, glowing cores, comets that trail along the spiral, and a bloom pass.
/// </summary>
public sealed class RainbowSpiralApp : WidgetApp
{
    public override string Id => "rainbow-spiral";
    public override string Name => "Rainbow Spiral";

    [Setting("Speed", Description = "Animation speed", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Arms", Description = "Number of spiral arms", Min = 1, Max = 8)]
    public int Arms { get; set; } = 3;

    [Setting("Style", Description = "Visual style", Options = ["Twin Vortex", "Vortex", "Plasma", "Tunnel"])]
    public string Style { get; set; } = "Twin Vortex";

    [Setting("Palette", Description = "Colour palette", Options = ["Rainbow", "Neon", "Sunset", "Ocean", "Candy", "Aurora", "Mono"])]
    public string Palette { get; set; } = "Rainbow";

    [Setting("Comets", Description = "Glowing comets that trail along the spiral")]
    public bool Comets { get; set; } = true;

    [Setting("Glow", Description = "Bloom around the brightest areas (turn off on slow hardware)")]
    public bool Glow { get; set; } = true;

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    protected override Node Build() => new Panel { Children = { new SpiralVisual(this, Seed ?? Random.Shared.Next()) } };
}
