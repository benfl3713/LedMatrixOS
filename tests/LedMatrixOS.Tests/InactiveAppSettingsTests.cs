using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class InactiveAppSettingsTests : IDisposable
{
    private sealed class AlphaApp : SettingsAppBase
    {
        public override string Id => "alpha";
        public override string Name => "Alpha";

        [Setting("Count", Min = 1, Max = 9)]
        public int Count { get; set; } = 3;

        [Setting("Label")]
        public string Label { get; set; } = "hi";

        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private sealed class BetaApp : MatrixAppBase
    {
        public override string Id => "beta";
        public override string Name => "Beta";
        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private readonly string _file = Path.Combine(Path.GetTempPath(), "app-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    private AppManager Create(string startActive)
    {
        var mgr = new AppManager(new EmptyServices(), new ConfigurationBuilder().Build(), 64, 256, new AppSettingsStorage(_file));
        mgr.Register(typeof(AlphaApp));
        mgr.Register(typeof(BetaApp));
        mgr.ActivateAsync(startActive, CancellationToken.None).GetAwaiter().GetResult();
        return mgr;
    }

    [Fact]
    public void GetSettings_WorksForInactiveApp_AndLeavesActiveAppAlone()
    {
        var mgr = Create("beta");
        var active = mgr.ActiveApp;

        var lookup = mgr.GetSettings("alpha");

        Assert.Equal(SettingsStatus.Ok, lookup.Status);
        Assert.Contains(lookup.Settings, s => s.Key == "count" && (int)s.CurrentValue == 3);
        Assert.Same(active, mgr.ActiveApp);
    }

    [Fact]
    public void UpdateSettings_PersistsForInactiveApp_AndAppliesOnActivation()
    {
        var mgr = Create("beta");

        var result = mgr.UpdateSettings("alpha", new Dictionary<string, object> { ["count"] = 7, ["label"] = "yo" });
        Assert.Equal(SettingsStatus.Ok, result.Status);
        Assert.Empty(result.RejectedKeys);

        // Visible without activation, including through a fresh manager reading the same file
        Assert.Contains(mgr.GetSettings("alpha").Settings, s => s.Key == "count" && (int)s.CurrentValue == 7);
        var reloaded = Create("beta");
        Assert.Contains(reloaded.GetSettings("alpha").Settings, s => s.Key == "label" && (string)s.CurrentValue == "yo");

        reloaded.ActivateAsync("alpha", CancellationToken.None).GetAwaiter().GetResult();
        var live = (AlphaApp)reloaded.ActiveApp!;
        Assert.Equal(7, live.Count);
        Assert.Equal("yo", live.Label);
    }

    [Fact]
    public void UpdateSettings_UnknownKeyAndAppAndNonConfigurable()
    {
        var mgr = Create("beta");

        Assert.Equal(SettingsStatus.NotFound, mgr.UpdateSettings("nope", new Dictionary<string, object>()).Status);
        Assert.Equal(SettingsStatus.NotFound, mgr.GetSettings("nope").Status);
        Assert.Equal(SettingsStatus.NotConfigurable, mgr.UpdateSettings("beta", new Dictionary<string, object> { ["x"] = 1 }).Status);
        Assert.Equal(new[] { "bogus" }, mgr.UpdateSettings("alpha", new Dictionary<string, object> { ["bogus"] = 1 }).RejectedKeys);
    }

    [Fact]
    public void UpdateSettings_ActiveApp_StillUpdatesLiveInstance()
    {
        var mgr = Create("alpha");
        mgr.UpdateSettings("alpha", new Dictionary<string, object> { ["count"] = 5 });
        Assert.Equal(5, ((AlphaApp)mgr.ActiveApp!).Count);
    }

    [Fact]
    public void UpdateSettings_InvalidValue_DoesNotPersistGarbage()
    {
        var mgr = Create("beta");
        mgr.UpdateSettings("alpha", new Dictionary<string, object> { ["count"] = "not a number" });
        Assert.Contains(mgr.GetSettings("alpha").Settings, s => s.Key == "count" && (int)s.CurrentValue == 3);
    }
}
