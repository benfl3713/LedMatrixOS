using LedMatrixOS.Apps.Alive;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Four self-running living systems on the whole display: boids flocking, Gray-Scott reaction-diffusion, falling sand with
/// scripted scenes, and a small Lenia-style continuous cellular automaton. Each one reseeds itself when it empties out or
/// freezes, so the panel is never dead; "Auto-cycle" rotates through them.
/// </summary>
public sealed class AliveApp : WidgetApp
{
    public const string BoidsName = "Boids";
    public const string ReactionName = "Reaction-Diffusion";
    public const string SandName = "Falling Sand";
    public const string LeniaName = "Lenia";
    public const string AutoCycle = "Auto-cycle";

    public override string Id => "alive";
    public override string Name => "Alive";
    public override int FrameRate => 30;

    [Setting("Simulation", Description = "Which living system to show", Options = [AutoCycle, BoidsName, ReactionName, SandName, LeniaName])]
    public string Simulation { get; set; } = AutoCycle;

    [Setting("Cycle Seconds", Description = "How long each simulation stays up in Auto-cycle", Min = 5, Max = 300)]
    public int CycleSeconds { get; set; } = 45;

    [Setting("Palette", Description = "Colours", Options = ["Neon", "Fire", "Ocean", "Forest", "Mono", "Rainbow"])]
    public string Palette { get; set; } = "Neon";

    [Setting("Speed", Description = "How fast the simulation runs", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Population", Description = "Boids in the flock, creatures and grains per spawn, seeds in the pattern", Min = 1, Max = 10)]
    public int Population { get; set; } = 5;

    [Setting("Trail", Description = "How long boids and creatures leave a glowing trail", Min = 0, Max = 10)]
    public int Trail { get; set; } = 5;

    [Setting("Reseed Minutes", Description = "Start a fresh run after this many minutes (0 = only when it gets stale)", Min = 0, Max = 60)]
    public int ReseedMinutes { get; set; } = 3;

    [Setting("Predator", Description = "A hawk that hunts the flock")]
    public bool Predator { get; set; } = true;

    [Setting("Variant", Description = "Reaction-diffusion preset or falling-sand scene (Auto picks one each run)",
        Options = ["Auto", "Coral", "Spots", "Worms", "Mitosis", "Rain", "Hourglass", "Garden", "Cascade"])]
    public string Variant { get; set; } = "Auto";

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    private AliveVisual? _view;

    /// <summary>Name of the simulation currently showing.</summary>
    public string CurrentSimulation => _view?.CurrentName ?? "";
    /// <summary>Simulation steps since the last seed.</summary>
    public int Ticks => _view?.Ticks ?? 0;
    /// <summary>Automatic reseeds so far (stagnation, empty world or the reseed timer); setting changes do not count.</summary>
    public int Reseeds => _view?.Reseeds ?? 0;

    /// <summary>Empties the running simulation (tests; it should then reseed itself).</summary>
    public void Wipe() => _view?.Wipe();

    protected override Node Build()
    {
        var view = _view = new AliveVisual(this, Seed ?? Random.Shared.Next());
        return new Panel { Children = { view } };
    }
}
