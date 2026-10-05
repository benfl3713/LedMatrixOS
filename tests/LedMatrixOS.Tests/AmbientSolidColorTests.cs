using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class AmbientSolidColorTests
{
    private static object Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static SolidColorApp WithMode(string mode)
    {
        var app = new SolidColorApp();
        app.UpdateSetting("mode", mode);
        return app;
    }

    [Fact]
    public void Identity_IsUnchanged()
    {
        var app = new SolidColorApp();
        Assert.Equal("solid_color", app.Id);
        Assert.Equal("Solid Color", app.Name);
    }

    [Fact]
    public void Default_IsOneFlatColour()
    {
        var frame = AmbientRig.Render(new SolidColorApp(), TimeSpan.FromSeconds(3));
        var expected = new Pixel(20, 255, 0);
        foreach (var p in frame.GetPixelsSpan()) Assert.Equal(expected, p);
    }

    [Fact]
    public void Solid_ColourChangeFadesThenSettles()
    {
        var rig = new AmbientRig(new SolidColorApp()).Advance(200);
        ((SolidColorApp)rig.App).UpdateSetting("red", 255);
        ((SolidColorApp)rig.App).UpdateSetting("green", 0);
        rig.Advance(30, 16);
        var mid = rig.Draw().GetPixel(5, 5);
        Assert.NotEqual(new Pixel(255, 0, 0), mid);
        Assert.NotEqual(new Pixel(20, 255, 0), mid);
        rig.Advance(2000);
        Assert.Equal(new Pixel(255, 0, 0), rig.Draw().GetPixel(5, 5));
    }

    [Theory]
    [InlineData("Breathing")]
    [InlineData("Gradient Drift")]
    [InlineData("Color Cycle")]
    [InlineData("Sparkle")]
    [InlineData("Candle")]
    [InlineData("Aurora")]
    public void Modes_AreNonBlank_Deterministic_AndAnimate(string mode)
    {
        FrameBuffer At(int ms) => AmbientRig.Render(WithMode(mode), TimeSpan.FromMilliseconds(ms));
        var a = At(2300);
        Assert.False(SnapshotHelper.IsBlank(a));
        Assert.True(Stage.Same(a, At(2300)));
        Assert.False(Stage.Same(a, At(4100)));
    }

    [Fact]
    public void Brightness_ScalesTheOutput()
    {
        var app = new SolidColorApp();
        app.UpdateSetting("brightness", 50);
        var p = AmbientRig.Render(app, TimeSpan.FromSeconds(1)).GetPixel(10, 10);
        Assert.InRange(p.G, 126, 129);
    }

    [Fact]
    public void Settings_RoundTrip_IncludingOldPersistedJsonValues()
    {
        var app = new SolidColorApp();
        // The shapes the settings file produced for the old implementation: JSON numbers and numeric strings.
        app.UpdateSetting("red", Json("200"));
        app.UpdateSetting("green", Json("\"128\""));
        app.UpdateSetting("blue", 7);
        Assert.Equal((200, 128, 7), (app.Red, app.Green, app.Blue));
        app.UpdateSetting("red", 999);
        Assert.Equal(255, app.Red);

        // New keys, and an unknown mode is ignored rather than breaking the app.
        app.UpdateSetting("mode", Json("\"Candle\""));
        Assert.Equal("Candle", app.Mode);
        app.UpdateSetting("mode", "Disco Inferno");
        Assert.Equal("Candle", app.Mode);

        var keys = app.GetSettings().Select(s => s.Key).ToArray();
        Assert.Equal(["colour", "mode", "speed", "spread", "brightness", "fadeDuration"], keys);
        Assert.Equal("Candle", app.GetSettings().First(s => s.Key == "mode").CurrentValue);
    }

    [Fact]
    public void Colour_Hex_ParsesAndInvalidKeepsPrevious()
    {
        var app = new SolidColorApp();
        app.UpdateSetting("colour", "#FF8000");
        Assert.Equal(new Pixel(255, 128, 0), app.Parsed);
        app.UpdateSetting("colour", "0f0");
        Assert.Equal(new Pixel(0, 255, 0), app.Parsed);
        app.UpdateSetting("colour", "banana");
        Assert.Equal(new Pixel(0, 255, 0), app.Parsed);
    }

    [Fact]
    public void OldRgbValues_MigrateIntoColour()
    {
        var app = new SolidColorApp();
        app.UpdateSetting("red", 10); app.UpdateSetting("green", 20); app.UpdateSetting("blue", 30);
        Assert.Equal("#0A141E", app.Colour);
    }

    [Fact]
    public void FadeDuration_Zero_SnapsInstantly()
    {
        var rig = new AmbientRig(new SolidColorApp { FadeDuration = 0 }).Advance(200);
        ((SolidColorApp)rig.App).UpdateSetting("colour", "#FF0000");
        rig.Advance(1, 16);
        Assert.Equal(new Pixel(255, 0, 0), rig.Draw().GetPixel(5, 5));
    }

    [Theory]
    [InlineData("Breathing", 3500)]
    [InlineData("Gradient Drift", 4000)]
    [InlineData("Color Cycle", 5000)]
    [InlineData("Sparkle", 3000)]
    [InlineData("Candle", 2500)]
    [InlineData("Aurora", 4000)]
    public void Modes_Snapshot(string mode, int ms)
    {
        var frame = AmbientRig.Render(WithMode(mode), TimeSpan.FromMilliseconds(ms));
        SnapshotHelper.AssertMatchesSnapshot(frame, $"ambient_solid_{mode.Replace(' ', '_').ToLowerInvariant()}_t{ms}");
    }

    [Theory]
    [InlineData("Solid")]
    [InlineData("Breathing")]
    [InlineData("Gradient Drift")]
    [InlineData("Color Cycle")]
    [InlineData("Sparkle")]
    [InlineData("Candle")]
    [InlineData("Aurora")]
    public void SteadyState_AllocatesNothingPerFrame(string mode) =>
        new AmbientRig(WithMode(mode)).AssertNoAllocationsPerFrame();
}
