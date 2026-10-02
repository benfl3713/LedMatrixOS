using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Digital rain in three depth layers (tiny dim glyphs far away, katakana up close), bright white-hot heads, long fading trails,
/// shimmering glyphs, an occasional scan-line word reveal and short glitch bursts, all under a bloom pass.
/// </summary>
public sealed class MatrixRainApp : WidgetApp
{
    public override string Id => "matrix-rain";
    public override string Name => "Matrix Rain";

    [Setting("Colour", Description = "Rain colour", Options = ["Green", "Cyan", "Red", "Purple", "Gold", "Rainbow"])]
    public string Colour { get; set; } = "Green";

    [Setting("Density", Description = "How many streams fall at once", Min = 1, Max = 10)]
    public int Density { get; set; } = 6;

    [Setting("Speed", Description = "Fall speed", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Words", Description = "Occasionally reveal a word in the rain")]
    public bool Words { get; set; } = true;

    [Setting("Glitch", Description = "Occasional horizontal glitch bursts")]
    public bool Glitch { get; set; } = true;

    [Setting("Glow", Description = "Bloom around the bright heads (turn off on slow hardware)")]
    public bool Glow { get; set; } = true;

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    protected override Node Build() => new Panel { Children = { new RainVisual(this, Seed ?? Random.Shared.Next()) } };
}
