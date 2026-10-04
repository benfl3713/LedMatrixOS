using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Screens;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps.Screens;

/// <summary>
/// Renders a user-defined <see cref="ScreenDefinition"/> from the <see cref="IScreenStore"/>. The definition's id is a hidden preset
/// (<c>screenId</c>, applied through <c>UpdateSetting</c> by an alias registration, so it never shows up as a setting). The node tree is built
/// once per activation by <see cref="ScreenNodeFactory"/>; data comes from <see cref="BindingResolver"/> sources, so steady-state rendering allocates nothing.
/// A missing screen shows "Screen not found"; unusable nodes inside a screen are skipped.
/// </summary>
public sealed class ScreenApp : WidgetApp, IPollHost
{
    private readonly IScreenStore _store;
    private readonly HttpClient _http;
    private readonly Dictionary<string, ILiveData<BindValue>> _overrides = new(StringComparer.Ordinal);
    private IConfiguration? _config;
    private CancellationTokenSource? _pollCts;

    public override string Id => "screen";
    public override string Name => "Screen";
    public override int FrameRate => 30;

    /// <summary>Id of the screen to show. Set by the alias preset before activation.</summary>
    public string ScreenId { get; set; } = "";

    [ActivatorUtilitiesConstructor]
    public ScreenApp(IScreenStore store, HttpClient http)
    {
        _store = store;
        _http = http;
    }

    // The screen id is an alias preset, not a user setting.
    public override IEnumerable<AppSetting> GetSettings() => [];

    public override void UpdateSetting(string key, object value)
    {
        if (string.Equals(key, "screenId", StringComparison.OrdinalIgnoreCase)) ScreenId = value?.ToString() ?? "";
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _config = configuration;
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _pollCts?.Cancel();
        _pollCts = null;
        await base.OnDeactivatedAsync(cancellationToken);
    }

    /// <summary>Number of distinct binding sources the last build created (shared weather counts once per field).</summary>
    internal int BindingCount { get; private set; }

    /// <summary>Test seam: replaces the data behind a binding key (e.g. <c>weather.temp</c>) with a fixed source. Call before the first frame.</summary>
    internal void UseData(string key, ILiveData<BindValue> data) => _overrides[key] = data;

    internal void UseValue(string key, string text, double number = double.NaN) => _overrides[key] = new StaticLive(text, number);

    ILiveData<T> IPollHost.Poll<T>(TimeSpan interval, Func<CancellationToken, Task<T>> fetch)
    {
        _pollCts ??= new CancellationTokenSource();
        return Poll(interval, fetch, _pollCts.Token);
    }

    protected override Node Build()
    {
        var definition = _store.TryGet(ScreenId);
        if (definition?.Root is null) return NotFound(definition is null ? "Screen not found" : "Screen is empty");

        var resolver = new BindingResolver(Time, _config, _http, this, _overrides);
        var root = new ScreenNodeFactory(resolver, Time).Build(definition.Root);
        BindingCount = resolver.SourceCount;
        return root ?? NotFound("Screen is empty");
    }

    private static Node NotFound(string message) => new Label(message)
    {
        Style = new TextStyle(Fonts.Small, new Pixel(200, 200, 200), Shadow: false),
        HAlign = Align.Center,
        VAlign = Align.Center,
    };
}
