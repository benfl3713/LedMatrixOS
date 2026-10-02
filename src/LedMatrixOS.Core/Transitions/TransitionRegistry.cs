namespace LedMatrixOS.Core.Transitions;

public sealed class TransitionRegistry
{
    public const string RandomName = "random";

    private readonly Dictionary<string, ITransition> _transitions = new(StringComparer.OrdinalIgnoreCase);

    public TransitionRegistry()
    {
        foreach (var d in Enum.GetValues<MoveDirection>())
        {
            Register(new SlideTransition(d));
            Register(new WipeTransition(d));
        }
        Register(new CrossfadeTransition());
        Register(new DissolveTransition());
        Register(new IrisTransition());
        Register(new MatrixRainTransition());
    }

    public IReadOnlyCollection<string> Names => _transitions.Keys;

    public void Register(ITransition transition) => _transitions[transition.Name] = transition;

    public bool TryGet(string name, out ITransition transition) => _transitions.TryGetValue(name, out transition!);

    public bool IsValidName(string name) =>
        name.Equals(RandomName, StringComparison.OrdinalIgnoreCase) || _transitions.ContainsKey(name);

    /// <summary>Resolves a name or "random" to a transition (null if unknown).</summary>
    public ITransition? Resolve(string name) =>
        name.Equals(RandomName, StringComparison.OrdinalIgnoreCase) ? Random() : TryGet(name, out var t) ? t : null;

    public ITransition Random() =>
        _transitions.Values.ElementAt(System.Random.Shared.Next(_transitions.Count));
}
