using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using Xunit;

namespace LedMatrixOS.Tests;

/// <summary>The Advanced flag on [Setting] reaches the settings JSON as "advanced", false by default.</summary>
public sealed class AdvancedSettingTests
{
    private sealed class FlagApp : SettingsAppBase
    {
        public override string Id => "flag";
        public override string Name => "Flag";

        [Setting("Basic")]
        public string Basic { get; set; } = "";

        [Setting("Raw", Advanced = true)]
        public string Raw { get; set; } = "";

        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private static JsonElement Json(IConfigurableApp app) => JsonSerializer.SerializeToElement(
        new { settings = app.GetSettings().ToList() }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void AdvancedFlag_IsInSettingsJson_AndDefaultsToFalse()
    {
        var settings = Json(new FlagApp()).GetProperty("settings").EnumerateArray().ToList();

        Assert.False(settings.First(s => s.GetProperty("key").GetString() == "basic").GetProperty("advanced").GetBoolean());
        Assert.True(settings.First(s => s.GetProperty("key").GetString() == "raw").GetProperty("advanced").GetBoolean());
    }

    [Fact]
    public void CapturedDefaults_SurviveChangedValues()
    {
        var app = new FlagApp { Raw = "factory" };
        SettingsBinder.CaptureDefaults(app);
        app.Raw = "edited";

        var raw = app.GetSettings().First(s => s.Key == "raw");
        Assert.Equal("factory", raw.DefaultValue);
        Assert.Equal("edited", raw.CurrentValue);
    }

    [Fact]
    public void PlatformFilter_IsAdvanced()
    {
        var json = Json(new TubeDeparturesApp(new HttpClient())).GetProperty("settings").EnumerateArray();
        Assert.True(json.First(s => s.GetProperty("key").GetString() == "platformFilter").GetProperty("advanced").GetBoolean());
    }
}
