namespace LedMatrixOS.Core.Screens;

/// <summary>
/// Owns the user's screens: validates edits, persists them to screens.json (atomically) and keeps the <see cref="ScreenStore"/>
/// and the <c>screen:&lt;id&gt;</c> aliases in <see cref="AppManager"/> in step. The REST endpoints are thin wrappers over this.
/// </summary>
public sealed class ScreenCatalog
{
    public const string AliasPrefix = "screen:";
    private const string TargetAppId = "screen";
    private const string FallbackAppId = "home";

    private readonly ScreenStore _store;
    private readonly AppManager _apps;
    private readonly string _path;
    private readonly object _gate = new();

    public ScreenCatalog(ScreenStore store, AppManager apps, string path)
    {
        _store = store;
        _apps = apps;
        _path = path;
    }

    public IReadOnlyList<ScreenDefinition> All => _store.All.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();

    public ScreenDefinition? TryGet(string id) => _store.TryGet(id);

    /// <summary>Loads screens.json (if present). Returns validation errors; on any error nothing is loaded and the file is left untouched.</summary>
    public IReadOnlyList<ScreenError> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return [];
            var doc = ScreenDocument.TryParse(File.ReadAllText(_path), out var parseError);
            if (doc == null) return [new ScreenError("", parseError!)];
            var errors = doc.Validate();
            if (errors.Count > 0) return errors;
            _store.ReplaceAll(doc.Screens);
            foreach (var s in doc.Screens) Register(s);
            return [];
        }
    }

    /// <summary>Validates and saves a screen (add or replace). Returns errors, or an empty list on success.</summary>
    public IReadOnlyList<ScreenError> Put(ScreenDefinition screen)
    {
        var errors = new ScreenDocument { Screens = { screen } }.Validate();
        if (errors.Count > 0) return errors;
        lock (_gate)
        {
            var next = _store.All.Where(s => s.Id != screen.Id).Append(screen).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            Persist(next); // throws before touching memory, so a failed write changes nothing
            _store.Replace(screen);
            Register(screen);
        }
        return [];
    }

    /// <summary>Removes a screen. False if it does not exist. If it is on display the device switches to the home app.</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_store.TryGet(id) == null) return false;
            Persist(_store.All.Where(s => s.Id != id).OrderBy(s => s.Id, StringComparer.Ordinal).ToList());
            _store.Remove(id);
        }
        var alias = AliasPrefix + id;
        bool active = string.Equals(_apps.ActiveAppId, alias, StringComparison.OrdinalIgnoreCase);
        _apps.UnregisterAlias(alias);
        // A schedule rule that still names the deleted screen is skipped by ScheduleRunner (ActivateAsync returns false).
        if (active) await _apps.ActivateAsync(FallbackAppId, ct);
        return true;
    }

    private void Persist(List<ScreenDefinition> screens) =>
        ScreenDocument.WriteAtomic(_path, new ScreenDocument { Screens = screens }.ToJson());

    private void Register(ScreenDefinition s) =>
        _apps.RegisterAlias(AliasPrefix + s.Id, TargetAppId, new Dictionary<string, object> { ["screenId"] = s.Id }, s.Name, isScreen: true);
}
