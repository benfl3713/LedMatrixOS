using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class AmbientScrollingTextTests
{
    private static object Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static AmbientRig Rig(Action<ScrollingTextApp>? configure = null)
    {
        var app = new ScrollingTextApp();
        configure?.Invoke(app);
        return new AmbientRig(app);
    }

    [Fact]
    public void Identity_IsUnchanged()
    {
        var app = new ScrollingTextApp();
        Assert.Equal("scrolling-text", app.Id);
        Assert.Equal("Scrolling Text", app.Name);
    }

    [Theory]
    [InlineData("Solid")]
    [InlineData("Gradient")]
    [InlineData("Rainbow")]
    [InlineData("Wave")]
    [InlineData("Rainbow Wave")]
    [InlineData("Fire")]
    public void Effects_AreNonBlank_Deterministic_AndMove(string effect)
    {
        FrameBuffer At(int ms) => Rig(a => a.TextEffect = effect).Advance(ms).Copy();
        var a = At(5000);
        Assert.False(SnapshotHelper.IsBlank(a));
        Assert.True(Stage.Same(a, At(5000)));
        Assert.False(Stage.Same(a, At(5600)));
    }

    [Fact]
    public void Loop_RepeatsTheMessage_SinglePassLeavesAGap()
    {
        // Short message in loop mode appears several times; in single-pass mode the screen is empty after it leaves.
        var loop = Rig(a => { a.Message = "HI"; a.ScrollSpeed = 100; a.Separator = "Star"; a.Decor = "None"; a.TextEffect = "Solid"; });
        loop.Advance(6000);
        var loopFrame = loop.Copy();
        Assert.False(SnapshotHelper.IsBlank(loopFrame));

        var once = Rig(a => { a.Message = "HI"; a.ScrollSpeed = 100; a.Loop = false; a.Decor = "None"; a.TextEffect = "Solid"; });
        once.Advance(3700);
        Assert.True(SnapshotHelper.IsBlank(once.Copy()), "single pass should have left the screen");
    }

    [Fact]
    public void ChangingTheMessage_AnimatesTheSwap()
    {
        var rig = Rig(a => a.Decor = "None").Advance(3000);
        var before = rig.Copy();
        ((ScrollingTextApp)rig.App).UpdateSetting("message", "SOMETHING ELSE");
        rig.Advance(120, 20);
        var mid = rig.Copy();
        rig.Advance(2500);
        Assert.False(Stage.Same(before, mid));
        Assert.False(SnapshotHelper.IsBlank(rig.Copy()));
    }

    [Fact]
    public void Settings_RoundTrip_IncludingOldPersistedValues()
    {
        var app = new ScrollingTextApp();
        app.UpdateSetting("message", Json("\"Welcome home\""));
        app.UpdateSetting("scrollSpeed", Json("55"));
        app.UpdateSetting("fontSize", Json("40"));
        app.UpdateSetting("fontStyle", Json("\"Regular\""));
        app.UpdateSetting("textColor", Json("\"Cyan\""));
        app.UpdateSetting("backgroundColor", Json("\"Navy\""));
        Assert.Equal(("Welcome home", 55, "Huge", "Regular", "Cyan", "Navy"),
            (app.Message, app.ScrollSpeed, app.FontSize, app.FontStyle, app.TextColor, app.BackgroundColor));
        app.UpdateSetting("scrollSpeed", 5);
        Assert.Equal(10, app.ScrollSpeed);
        app.UpdateSetting("textColor", "Plaid");
        Assert.Equal("Cyan", app.TextColor);

        app.UpdateSetting("textEffect", "Rainbow Wave");
        app.UpdateSetting("separator", Json("\"Heart\""));
        app.UpdateSetting("loop", Json("false"));
        app.UpdateSetting("decor", "Chase Lights");
        Assert.Equal(("Rainbow Wave", "Heart", false, "Chase Lights"), (app.TextEffect, app.Separator, app.Loop, app.Decor));

        Assert.Equal(["message", "scrollSpeed", "fontSize", "direction", "verticalAlign", "outline", "fontStyle", "textColor", "backgroundColor", "textEffect", "separator", "loop", "decor"],
            app.GetSettings().Select(s => s.Key).ToArray());
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(48)]
    public void AllFontSizes_Render(int size)
    {
        Assert.False(SnapshotHelper.IsBlank(Rig(a => { a.FontSize = ScrollingTextApp.SizeFromNumber(size); a.Message = "Size test"; }).Advance(2500).Copy()));
    }

    [Theory]
    [InlineData("gradient_star_big", 24, "Gradient", "Star", "Edge Glow", "Red", 3000)]
    [InlineData("rainbow_wave_heart", 24, "Rainbow Wave", "Heart", "None", "Cyan", 4000)]
    [InlineData("fire_huge_bolt", 48, "Fire", "Bolt", "Chase Lights", "Orange", 2500)]
    [InlineData("wave_medium_diamond_lights", 16, "Wave", "Diamond", "Chase Lights", "Green", 3500)]
    [InlineData("rainbow_small_arrow", 10, "Rainbow", "Arrow", "Edge Glow", "White", 4500)]
    [InlineData("solid_dot_glow", 24, "Solid", "Dot", "Edge Glow", "Yellow", 5200)]
    public void Snapshots(string name, int size, string effect, string sep, string decor, string color, int ms)
    {
        var rig = Rig(a =>
        {
            a.Message = "Welcome to the LED matrix!";
            a.FontSize = ScrollingTextApp.SizeFromNumber(size); a.TextEffect = effect; a.Separator = sep; a.Decor = decor; a.TextColor = color; a.ScrollSpeed = 60;
        });
        SnapshotHelper.AssertMatchesSnapshot(rig.Advance(ms, 25).Copy(), $"ambient_ticker_{name}");
    }

    [Fact]
    public void Snapshot_MessageSwapMidAnimation_Placeholder() { }

    [Theory]
    [InlineData("Right", "Middle", false)]
    [InlineData("Up", "Middle", false)]
    [InlineData("Left", "Top", true)]
    [InlineData("Left", "Bottom", false)]
    public void NewOptions_RenderAndAllocateNothing(string dir, string valign, bool outline)
    {
        Rig(a => { a.Direction = dir; a.VerticalAlign = valign; a.Outline = outline; a.Message = "Welcome to the LED matrix! More words here"; }).AssertNoAllocationsPerFrame();
    }

    [Theory]
    [InlineData("right_medium", "Right", "Top", false, "Medium", 3000)]
    [InlineData("up_wrapped", "Up", "Middle", false, "Medium", 1500)]
    [InlineData("left_outline_bottom", "Left", "Bottom", true, "Large", 3000)]
    public void NewOptionSnapshots(string name, string dir, string valign, bool outline, string size, int ms)
    {
        var rig = Rig(a => { a.Message = "Welcome to the LED matrix! Read me from afar"; a.Direction = dir; a.VerticalAlign = valign; a.Outline = outline; a.FontSize = size; a.ScrollSpeed = 40; a.Decor = "None"; a.TextEffect = "Solid"; a.TextColor = "Yellow"; });
        SnapshotHelper.AssertMatchesSnapshot(rig.Advance(ms, 25).Copy(), $"ambient_ticker_{name}");
    }

    [Fact]
    public void Snapshot_MessageSwapMidAnimation()
    {
        var rig = Rig(a => { a.Message = "FIRST MESSAGE"; a.Decor = "Edge Glow"; }).Advance(3000, 25);
        ((ScrollingTextApp)rig.App).UpdateSetting("message", "SECOND ONE");
        rig.Advance(160, 20);
        SnapshotHelper.AssertMatchesSnapshot(rig.Copy(), "ambient_ticker_swap_mid");
    }

    [Theory]
    [InlineData("Gradient", "Chase Lights", 24)]
    [InlineData("Rainbow Wave", "Edge Glow", 48)]
    [InlineData("Fire", "None", 16)]
    public void SteadyState_AllocatesNothingPerFrame(string effect, string decor, int size) =>
        Rig(a => { a.TextEffect = effect; a.Decor = decor; a.FontSize = ScrollingTextApp.SizeFromNumber(size); a.Message = "Steady state ticker text"; }).AssertNoAllocationsPerFrame();
}
