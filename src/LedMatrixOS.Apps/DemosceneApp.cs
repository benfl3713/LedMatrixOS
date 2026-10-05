using LedMatrixOS.Apps.Demoscene;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Demoscene effects on the whole display: plasma, tunnel, metaballs, starfield warp, lava lamp, a rotating 3D cube and an endless
/// Mandelbrot zoom. Pick one, or auto-cycle through them with a crossfade. Everything is table driven and allocation free.
/// </summary>
public sealed class DemosceneApp : WidgetApp
{
    public const string AutoCycle = "Auto-cycle";

    internal static readonly string[] EffectNames = [AutoCycle, "Plasma", "Tunnel", "Metaballs", "Starfield Warp", "Lava Lamp", "Cube", "Mandelbrot Zoom"];
    internal static readonly string[] PaletteNames = ["Neon", "Fire", "Ocean", "Forest", "Mono", "Rainbow", "Sunset"];
    internal static readonly string[] MirrorNames = ["Off", "Horizontal", "Quad"];

    public override string Id => "demoscene";
    public override string Name => "Demoscene";

    [Setting("Effect", Description = "Which effect to show, or cycle through all of them",
        Options = [AutoCycle, "Plasma", "Tunnel", "Metaballs", "Starfield Warp", "Lava Lamp", "Cube", "Mandelbrot Zoom"])]
    public string Effect { get; set; } = AutoCycle;

    [Setting("Seconds Per Effect", Description = "How long each effect stays up in Auto-cycle", Min = 5, Max = 120)]
    public int SecondsPerEffect { get; set; } = 20;

    [Setting("Palette", Description = "Colour palette", Options = ["Neon", "Fire", "Ocean", "Forest", "Mono", "Rainbow", "Sunset"])]
    public string Palette { get; set; } = "Neon";

    [Setting("Speed", Description = "Animation speed", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Scale", Description = "Feature size, cube size or zoom depth", Min = 1, Max = 10)]
    public int Scale { get; set; } = 5;

    [Setting("Mirror", Description = "Kaleidoscope mirroring", Options = ["Off", "Horizontal", "Quad"])]
    public string Mirror { get; set; } = "Off";

    [Setting("Intensity", Description = "Brightness", Min = 1, Max = 10)]
    public int Intensity { get; set; } = 8;

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    private DemosceneVisual? _view;

    /// <summary>Index of the effect on screen (0 plasma ... 6 mandelbrot).</summary>
    public int CurrentEffect => _view?.CurrentEffect ?? 0;

    /// <summary>True while crossfading between two effects.</summary>
    public bool Fading => _view?.Fading ?? false;

    protected override Node Build()
    {
        var view = _view = new DemosceneVisual(this, Seed ?? Random.Shared.Next());
        return new Panel { Children = { view } };
    }
}
