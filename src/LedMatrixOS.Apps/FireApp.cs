using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// A wall of fire: a heat-diffusion simulation driven by scrolling turbulence (so tongues lick and sway instead of flickering
/// per pixel), a palette ramp from ember red to white-hot, rising embers from the particle system and a bloom over the lot.
/// </summary>
public sealed class FireApp : WidgetApp
{
    public override string Id => "fire";
    public override string Name => "Fire";

    [Setting("Intensity", Description = "How tall and hot the flames burn", Min = 1, Max = 10)]
    public int Intensity { get; set; } = 6;

    [Setting("Palette", Description = "Flame colour", Options = ["Classic", "Blue", "Green", "Purple"])]
    public string Palette { get; set; } = "Classic";

    [Setting("Embers", Description = "Sparks rising off the flames")]
    public bool Embers { get; set; } = true;

    [Setting("Glow", Description = "Bloom around the brightest flames (turn off on slow hardware)")]
    public bool Glow { get; set; } = true;

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    protected override Node Build() => new Panel { Children = { new FireVisual(this, Seed ?? Random.Shared.Next()) } };
}
