using LedMatrixOS.Apps.Screens;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Screens;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class ScreenCatalogTests : IDisposable
{
    private sealed class HomeStub : SettingsAppBase
    {
        public override string Id => "home";
        public override string Name => "Home";
        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private sealed class Services(ScreenStore store) : IServiceProvider
    {
        private readonly HttpClient _http = new();
        public object? GetService(Type t) =>
            t == typeof(IScreenStore) || t == typeof(ScreenStore) ? store : t == typeof(HttpClient) ? _http : null;
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ledmatrix-screens-" + Guid.NewGuid().ToString("N"));
    private readonly ScreenStore _store = new();
    private readonly AppManager _apps;
    private readonly ScreenCatalog _catalog;

    public ScreenCatalogTests()
    {
        var sp = new Services(_store);
        _apps = new AppManager(sp, new ConfigurationBuilder().Build(), 64, 256);
        _apps.Register(typeof(ScreenApp));
        _apps.Register(typeof(HomeStub));
        _catalog = new ScreenCatalog(_store, _apps, Path.Combine(_dir, "screens.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string FilePath => Path.Combine(_dir, "screens.json");

    private static ScreenDefinition Make(string id, string text, string name = "Name")
    {
        var doc = ScreenDocument.TryParse($$$"""{"screens":[{"id":"{{{id}}}","name":"{{{name}}}","root":{"type":"label","text":"{{{text}}}"}}]}""", out var err);
        Assert.True(doc != null, err);
        return doc!.Screens[0];
    }

    [Fact]
    public void Put_PersistsRegistersAndReloads()
    {
        Assert.Empty(_catalog.Put(Make("a", "hello", "Alpha")));

        Assert.True(File.Exists(FilePath));
        var info = Assert.Single(_apps.Screens);
        Assert.Equal("screen:a", info.Id);
        Assert.Equal("Alpha", info.Name);

        // A fresh process loads the same screen back.
        var reloaded = new ScreenStore();
        var catalog2 = new ScreenCatalog(reloaded, _apps, FilePath);
        Assert.Empty(catalog2.Load());
        Assert.Equal("a", Assert.Single(catalog2.All).Id);
    }

    [Fact]
    public void Put_InvalidScreen_ReturnsErrorsAndWritesNothing()
    {
        var bad = Make("a", "x");
        bad.Root.Type = "nonsense";
        var errors = _catalog.Put(bad);
        Assert.NotEmpty(errors);
        Assert.False(File.Exists(FilePath));
        Assert.Empty(_apps.Screens);
        Assert.Empty(_store.All);
    }

    [Fact]
    public void Put_ReplacesInPlace()
    {
        _catalog.Put(Make("a", "one", "First"));
        _catalog.Put(Make("a", "two", "Second"));
        Assert.Equal("Second", Assert.Single(_apps.Screens).Name);
        Assert.Single(ScreenDocument.TryParse(File.ReadAllText(FilePath), out _)!.Screens);
    }

    [Fact]
    public async Task Delete_RemovesEverywhere_AndReturnsFalseWhenMissing()
    {
        _catalog.Put(Make("a", "x"));
        Assert.False(await _catalog.DeleteAsync("nope", CancellationToken.None));
        Assert.True(await _catalog.DeleteAsync("a", CancellationToken.None));
        Assert.Empty(_apps.Screens);
        Assert.Empty(ScreenDocument.TryParse(File.ReadAllText(FilePath), out _)!.Screens);
        Assert.False(await _apps.ActivateAsync("screen:a", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ActiveScreen_SwitchesToHome()
    {
        _catalog.Put(Make("a", "x"));
        Assert.True(await _apps.ActivateAsync("screen:a", CancellationToken.None));
        Assert.Equal("screen:a", _apps.ActiveAppId);
        await _catalog.DeleteAsync("a", CancellationToken.None);
        Assert.Equal("home", _apps.ActiveAppId);
    }

    [Fact]
    public async Task ScheduleRuleForDeletedScreen_IsJustNotActivatable()
    {
        _catalog.Put(Make("a", "x"));
        await _catalog.DeleteAsync("a", CancellationToken.None);
        // ScheduleRunner ignores the result of ActivateAsync (it only applies overrides on success), so an unknown id is skipped.
        Assert.False(await _apps.ActivateAsync("screen:a", CancellationToken.None));
    }

    [Fact]
    public void EditingActiveScreen_RebuildsTheTree()
    {
        _catalog.Put(Make("a", "before"));
        var app = new ScreenApp(_store, new HttpClient()) { ScreenId = "a" };
        var rig = new AmbientRig(app);
        rig.Advance(100);
        var first = rig.Copy();

        _catalog.Put(Make("a", "after, much longer text"));
        rig.Advance(100);
        var second = rig.Copy();

        Assert.NotEqual(first.GetPixelsSpan().ToArray(), second.GetPixelsSpan().ToArray());
    }
}
