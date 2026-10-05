using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class AppManagerAliasTests
{
    private sealed class GammaApp : SettingsAppBase
    {
        public override string Id => "gamma";
        public override string Name => "Gamma";
        [Setting("Count", Min = 1, Max = 9)]
        public int Count { get; set; } = 3;
        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static AppManager Create()
    {
        var mgr = new AppManager(new EmptyServices(), new ConfigurationBuilder().Build(), 64, 256);
        mgr.Register(typeof(GammaApp));
        return mgr;
    }

    private static Dictionary<string, object> Preset(int n) => new() { ["Count"] = n };

    [Fact]
    public async Task ScreenAliasIsListedAndReportsAliasIdWhenActive()
    {
        var mgr = Create();
        mgr.RegisterAlias("s1", "gamma", Preset(5), "My Screen", isScreen: true);
        mgr.RegisterAlias("legacy", "gamma", Preset(2));

        var screen = Assert.Single(mgr.Screens);
        Assert.Equal("s1", screen.Id);
        Assert.Equal("My Screen", screen.Name);
        Assert.Equal("gamma", screen.TargetId);
        Assert.Contains("legacy", mgr.AliasIds);

        Assert.True(await mgr.ActivateAsync("s1", CancellationToken.None));
        Assert.Equal("s1", mgr.ActiveAppId);
        Assert.Equal("gamma", mgr.ActiveApp!.Id);
        Assert.Equal(5, ((GammaApp)mgr.ActiveApp).Count);

        await mgr.ActivateAsync("gamma", CancellationToken.None);
        Assert.Equal("gamma", mgr.ActiveAppId);
    }

    [Fact]
    public async Task ReRegisterReplacesPresetAndUnregisterRemoves()
    {
        var mgr = Create();
        mgr.RegisterAlias("s1", "gamma", Preset(5), "A", isScreen: true);
        mgr.RegisterAlias("s1", "gamma", Preset(7), "B", isScreen: true);

        Assert.Equal("B", Assert.Single(mgr.Screens).Name);
        await mgr.ActivateAsync("s1", CancellationToken.None);
        Assert.Equal(7, ((GammaApp)mgr.ActiveApp!).Count);

        Assert.True(mgr.UnregisterAlias("s1"));
        Assert.False(mgr.UnregisterAlias("s1"));
        Assert.Empty(mgr.Screens);
        Assert.Equal("s1", mgr.ActiveAppId); // active instance keeps running
        Assert.False(await mgr.ActivateAsync("s1", CancellationToken.None));
    }
}
