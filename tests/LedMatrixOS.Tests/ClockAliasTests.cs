using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

/// <summary>The retired 'animated-clock' / 'flip-clock' ids keep working as aliases of Clock with a Style preset.</summary>
public sealed class ClockAliasTests : IDisposable
{
    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private readonly string _file = Path.Combine(Path.GetTempPath(), "clock-alias-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    private AppManager Create()
    {
        Fonts.Load();
        var mgr = new AppManager(new EmptyServices(), new ConfigurationBuilder().Build(), 64, 256, new AppSettingsStorage(_file));
        mgr.Register(typeof(ClockApp));
        foreach (var (alias, target, preset) in BuiltInApps.Aliases()) mgr.RegisterAlias(alias, target, preset);
        return mgr;
    }

    private static Task<bool> Activate(AppManager m, string id) => m.ActivateAsync(id, CancellationToken.None);

    [Fact]
    public void Aliases_AreNotListed_ButAreKnown()
    {
        var mgr = Create();
        Assert.Equal(["clock"], mgr.AppInfos.Select(i => i.Id));
        Assert.Contains("animated-clock", mgr.AliasIds);
        Assert.Contains("flip-clock", mgr.AliasIds);
    }

    [Theory]
    [InlineData("animated-clock", "Animated")]
    [InlineData("flip-clock", "Flip")]
    [InlineData("FLIP-CLOCK", "Flip")]
    [InlineData("clock", "Digital")]
    public async Task Activating_AnAlias_ActivatesClockWithStylePreset(string id, string style)
    {
        var mgr = Create();
        Assert.True(await Activate(mgr, id));
        var app = Assert.IsType<ClockApp>(mgr.ActiveApp);
        Assert.Equal("clock", app.Id);
        Assert.Equal(style, app.Style);
    }

    [Fact]
    public async Task UnknownId_StillFails() => Assert.False(await Activate(Create(), "no-such-clock"));

    [Fact]
    public async Task LegacyPersistedSettings_StillApplyToTheAlias()
    {
        // An app-settings.json written before the merge.
        File.WriteAllText(_file, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["animated-clock"] = new Dictionary<string, object> { ["palette"] = "Lava", ["showSeconds"] = false, ["waves"] = false },
            ["flip-clock"] = new Dictionary<string, object> { ["textColor"] = "Amber", ["show24Hour"] = false },
            ["clock"] = new Dictionary<string, object> { ["palette"] = "Neon" },
        }));
        var mgr = Create();

        await Activate(mgr, "animated-clock");
        var anim = (ClockApp)mgr.ActiveApp!;
        Assert.Equal(("Animated", "Lava", false, false), (anim.Style, anim.Palette, anim.ShowSeconds, anim.Waves));

        await Activate(mgr, "flip-clock");
        var flip = (ClockApp)mgr.ActiveApp!;
        Assert.Equal(("Flip", "Amber", false), (flip.Style, flip.TextColor, flip.Show24Hour));

        await Activate(mgr, "clock");
        var digital = (ClockApp)mgr.ActiveApp!;
        Assert.Equal(("Digital", "Neon", true), (digital.Style, digital.Palette, digital.ShowSeconds));
    }

    [Fact]
    public async Task AliasProfiles_StayIndependentOfClock_WhenSwitching()
    {
        var mgr = Create();
        await Activate(mgr, "flip-clock");
        mgr.UpdateCurrentAppSetting("textColor", "Cyan");
        await Activate(mgr, "clock");
        mgr.UpdateCurrentAppSetting("palette", "Ocean");
        await Activate(mgr, "flip-clock");
        Assert.Equal("Cyan", ((ClockApp)mgr.ActiveApp!).TextColor);
        Assert.Equal("Sunset", ((ClockApp)mgr.ActiveApp!).Palette);

        var stored = new AppSettingsStorage(_file);
        Assert.Equal("Ocean", stored.GetAppSettings("clock")!["palette"].ToString());
        Assert.Equal("Cyan", stored.GetAppSettings("flip-clock")!["textColor"].ToString());
    }

    [Fact]
    public async Task SettingsEndpointsLogic_WorksThroughAliases()
    {
        var mgr = Create();
        var result = mgr.UpdateSettings("animated-clock", new Dictionary<string, object> { ["palette"] = "Cyber" });
        Assert.Equal(SettingsStatus.Ok, result.Status);
        Assert.Empty(result.RejectedKeys);

        var lookup = mgr.GetSettings("animated-clock");
        Assert.Contains(lookup.Settings, s => s.Key == "style" && (string)s.CurrentValue == "Animated");
        Assert.Contains(lookup.Settings, s => s.Key == "palette" && (string)s.CurrentValue == "Cyber");
        // The real Clock profile is untouched.
        Assert.Contains(mgr.GetSettings("clock").Settings, s => s.Key == "palette" && (string)s.CurrentValue == "Sunset");

        await Activate(mgr, "animated-clock");
        Assert.Equal("Cyber", ((ClockApp)mgr.ActiveApp!).Palette);
        Assert.Equal(SettingsStatus.NotFound, mgr.GetSettings("nope").Status);
    }
}
