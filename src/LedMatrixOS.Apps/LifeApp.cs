using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Cellular automata on the whole display: Conway's Life, HighLife, Langton's ants and a small Wireworld circuit.
/// Cells are drawn straight into the frame buffer. When the grid dies out, settles into a still life or short loop, or simply
/// runs for long enough, it is reseeded so the display never goes stale.
/// </summary>
public sealed class LifeApp : WidgetApp
{
    public const string Conway = "Conway";
    public const string HighLife = "HighLife";
    public const string LangtonsAnt = "Langton's Ant";
    public const string Wireworld = "Wireworld";

    public override string Id => "life";
    public override string Name => "Life";
    public override int FrameRate => 30;

    [Setting("Rule", Description = "Which automaton to run", Options = [Conway, HighLife, LangtonsAnt, Wireworld])]
    public string Rule { get; set; } = Conway;

    [Setting("Palette", Description = "Cell colours", Options = ["Neon", "Fire", "Ocean", "Mono"])]
    public string Palette { get; set; } = "Neon";

    [Setting("Speed", Description = "Generations per second scale", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Cell Size", Description = "Pixels per cell", Min = 1, Max = 4)]
    public int CellSize { get; set; } = 2;

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    private LifeVisual? _view;

    public int Generation => _view?.Generation ?? 0;
    public int Population => _view?.Population ?? 0;
    public int Reseeds => _view?.Reseeds ?? 0;

    /// <summary>Replaces the grid with a pattern ('#' alive/conductor, 'o' electron head, 'x' tail) placed at the top left (needs a rendered frame first).</summary>
    public void SetPattern(params string[] rows) => _view?.SetPattern(rows);

    protected override Node Build()
    {
        var view = _view = new LifeVisual(this, Seed ?? Random.Shared.Next());
        return new Panel { Children = { view } };
    }
}
