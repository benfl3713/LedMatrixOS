using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class AmbientHomePageTests
{
    private static object Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static AmbientRig Rig(string mode = "Aurora", string theme = "Calm Blue", int hour = 13, int minute = 45, int second = 7,
        bool show24 = true, bool showDate = true)
    {
        var app = new HomePageApp { DisplayMode = mode, Theme = theme, Show24Hour = show24, ShowDate = showDate };
        return new AmbientRig(app, new FakeTime { Now = new DateTimeOffset(2026, 10, 2, hour, minute, second, TimeSpan.Zero) });
    }

    [Fact]
    public void Identity_IsUnchanged()
    {
        var app = new HomePageApp();
        Assert.Equal("home", app.Id);
        Assert.Equal("Home", app.Name);
    }

    [Theory]
    [InlineData("Aurora")]
    [InlineData("Starfield")]
    [InlineData("Embers")]
    [InlineData("Waves")]
    [InlineData("Day Night Sky")]
    [InlineData("Minimal")]
    public void EveryScene_IsNonBlank_Deterministic_AndAnimates(string mode)
    {
        FrameBuffer At(int ms) => Rig(mode).Advance(ms).Copy();
        var a = At(3000);
        Assert.False(SnapshotHelper.IsBlank(a));
        Assert.True(Stage.Same(a, At(3000)));
        Assert.False(Stage.Same(a, At(3400)));
    }

    [Theory]
    [InlineData("Calm Blue")]
    [InlineData("Warm Sunset")]
    [InlineData("Forest Green")]
    [InlineData("Lavender Dreams")]
    [InlineData("Monochrome")]
    [InlineData("Neon Synthwave")]
    public void EveryTheme_Renders(string theme)
    {
        Assert.False(SnapshotHelper.IsBlank(Rig("Aurora", theme).Advance(2500).Copy()));
    }

    [Fact]
    public void Themes_LookGenuinelyDifferent()
    {
        var blue = Rig("Minimal", "Calm Blue").Advance(3000).Copy();
        var sunset = Rig("Minimal", "Warm Sunset").Advance(3000).Copy();
        long diff = 0;
        for (int i = 0; i < blue.GetPixelsSpan().Length; i++)
            diff += Math.Abs(blue.GetPixelsSpan()[i].R - sunset.GetPixelsSpan()[i].R);
        Assert.True(diff > 20000, $"themes differ by only {diff}");
    }

    [Fact]
    public void Clock_MinuteChangeRollsDigits()
    {
        var rig = Rig(minute: 45, second: 59).Advance(2000, moveWallClock: false);
        var before = rig.Copy();
        rig.Clock.Now = new DateTimeOffset(2026, 10, 2, 13, 46, 0, TimeSpan.Zero);
        rig.Advance(100, 16, moveWallClock: false);
        var mid = rig.Copy();
        rig.Advance(2500, moveWallClock: false);
        var after = rig.Copy();
        Assert.False(Stage.Same(before, mid));
        Assert.False(Stage.Same(mid, after));
    }

    [Fact]
    public void TwelveHour_ShowsAmPmAndDropsLeadingZero()
    {
        var a = Rig(show24: true, hour: 9).Advance(3000).Copy();
        var b = Rig(show24: false, hour: 9).Advance(3000).Copy();
        Assert.False(Stage.Same(a, b));
    }

    [Fact]
    public void HidingTheDate_ChangesTheLayout_AndSettlesToACentredClock()
    {
        var withDate = Rig().Advance(3000).Copy();
        var rig = Rig();
        rig.Advance(3000);
        ((HomePageApp)rig.App).UpdateSetting("showDate", false);
        rig.Advance(2000);
        Assert.False(Stage.Same(withDate, rig.Copy()));
    }

    [Fact]
    public void Settings_RoundTrip_IncludingOldPersistedValues()
    {
        var app = new HomePageApp();
        // Old option names stored by the previous implementation land on the closest new scene.
        foreach (var (old, expected) in new[]
                 {
                     ("Ambient Particles", "Embers"), ("Flowing Waves", "Waves"), ("Starfield", "Starfield"),
                     ("Geometric Art", "Aurora"), ("Minimalist", "Minimal"), ("Day Night Sky", "Day Night Sky"), ("garbage", "Aurora"),
                 })
        {
            app.UpdateSetting("displayMode", Json($"\"{old}\""));
            Assert.Equal(expected, app.DisplayMode);
        }

        foreach (var theme in new[] { "Calm Blue", "Warm Sunset", "Forest Green", "Lavender Dreams", "Monochrome" })
        {
            app.UpdateSetting("theme", Json($"\"{theme}\""));
            Assert.Equal(theme, app.Theme);
        }
        app.UpdateSetting("theme", "Unknown Theme");
        Assert.Equal("Monochrome", app.Theme);

        app.UpdateSetting("showDate", Json("false"));
        app.UpdateSetting("show24Hour", Json("false"));
        app.UpdateSetting("ambientSpeed", Json("7"));
        Assert.False(app.ShowDate);
        Assert.False(app.Show24Hour);
        Assert.Equal(7, app.AmbientSpeed);
        app.UpdateSetting("ambientSpeed", 99);
        Assert.Equal(10, app.AmbientSpeed);

        Assert.Equal(["displayMode", "showDate", "show24Hour", "theme", "ambientSpeed"], app.GetSettings().Select(s => s.Key).Take(5).ToArray());
    }

    [Fact]
    public void SceneChange_FadesAndLandsOnTheNewScene()
    {
        var rig = Rig("Aurora").Advance(2500);
        var before = rig.Copy();
        ((HomePageApp)rig.App).UpdateSetting("displayMode", "Embers");
        rig.Advance(100, 16);
        var mid = rig.Copy();
        rig.Advance(2500);
        var after = rig.Copy();
        Assert.False(Stage.Same(before, mid));
        Assert.False(Stage.Same(mid, after));
        Assert.False(SnapshotHelper.IsBlank(after));
    }

    [Theory]
    [InlineData("Aurora", "Calm Blue", 13, 4000, "ambient_home_aurora_calmblue_t4000")]
    [InlineData("Starfield", "Neon Synthwave", 13, 8200, "ambient_home_starfield_neon_t8200")]
    [InlineData("Embers", "Warm Sunset", 13, 4000, "ambient_home_embers_sunset_t4000")]
    [InlineData("Waves", "Forest Green", 13, 5000, "ambient_home_waves_forest_t5000")]
    [InlineData("Minimal", "Monochrome", 13, 4000, "ambient_home_minimal_mono_t4000")]
    [InlineData("Aurora", "Lavender Dreams", 13, 4000, "ambient_home_aurora_lavender_t4000")]
    [InlineData("Day Night Sky", "Calm Blue", 6, 4000, "ambient_home_daysky_0630_t4000")]
    [InlineData("Day Night Sky", "Calm Blue", 13, 4000, "ambient_home_daysky_1345_t4000")]
    [InlineData("Day Night Sky", "Warm Sunset", 18, 4000, "ambient_home_daysky_1830_t4000")]
    [InlineData("Day Night Sky", "Calm Blue", 23, 4000, "ambient_home_daysky_2330_t4000")]
    public void Scenes_Snapshot(string mode, string theme, int hour, int ms, string name)
    {
        var rig = Rig(mode, theme, hour: hour, minute: hour == 6 || hour == 18 ? 30 : hour == 23 ? 30 : 45);
        SnapshotHelper.AssertMatchesSnapshot(rig.Advance(ms).Copy(), name);
    }

    [Fact]
    public void Snapshot_TwelveHourNoDate()
    {
        var rig = Rig("Starfield", "Lavender Dreams", hour: 9, minute: 5, show24: false, showDate: false);
        SnapshotHelper.AssertMatchesSnapshot(rig.Advance(4000).Copy(), "ambient_home_12h_nodate_t4000");
    }

    [Theory]
    [InlineData(250)]
    [InlineData(600)]
    public void Snapshot_Entrance(int ms)
    {
        SnapshotHelper.AssertMatchesSnapshot(Rig().Advance(ms).Copy(), $"ambient_home_entrance_t{ms}");
    }

    [Fact]
    public void Snapshot_MinuteRoll()
    {
        var rig = Rig(minute: 45, second: 59).Advance(3000, moveWallClock: false);
        rig.Clock.Now = new DateTimeOffset(2026, 10, 2, 13, 46, 0, TimeSpan.Zero);
        rig.Advance(180, 20, moveWallClock: false);
        SnapshotHelper.AssertMatchesSnapshot(rig.Copy(), "ambient_home_minute_roll");
    }

    [Theory]
    [InlineData("Aurora")]
    [InlineData("Starfield")]
    [InlineData("Embers")]
    [InlineData("Waves")]
    [InlineData("Day Night Sky")]
    [InlineData("Minimal")]
    public void SteadyState_AllocatesNothingPerFrame(string mode) => Rig(mode).AssertNoAllocationsPerFrame();
}
