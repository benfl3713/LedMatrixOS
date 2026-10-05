using System.Text.Json;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class AmbientCountdownTests
{
    private static object Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static AmbientRig Rig(int minutes = 5, string target = "", string label = "", bool celebrate = true, bool autoRestart = false)
    {
        var app = new CountdownTimerApp { DurationMinutes = minutes, Target = target, Label = label, Celebrate = celebrate, AutoRestart = autoRestart };
        return new AmbientRig(app, new FakeTime { Now = new DateTimeOffset(2026, 10, 2, 13, 45, 7, TimeSpan.Zero) });
    }

    [Fact]
    public void Identity_IsUnchanged()
    {
        var app = new CountdownTimerApp();
        Assert.Equal("countdown-timer", app.Id);
        Assert.Equal("Countdown Timer", app.Name);
    }

    [Fact]
    public void Runs_NonBlank_Deterministic_AndTicks()
    {
        FrameBuffer At(int ms) => Rig().Advance(ms).Copy();
        var a = At(3000);
        Assert.False(SnapshotHelper.IsBlank(a));
        Assert.True(Stage.Same(a, At(3000)));
        Assert.False(Stage.Same(a, At(4200)));
    }

    [Fact]
    public void Completion_FiresConfetti_ThenHolds_AndAutoRestartStartsAgain()
    {
        var rig = Rig(1);
        rig.Advance(59_000);
        var view = (CountdownTimerApp)rig.App;
        Assert.False(view.IsComplete);
        rig.Advance(1500);
        Assert.True(view.IsComplete);
        Assert.True(view.ConfettiCount > 50);

        var noConfetti = Rig(1, celebrate: false).Advance(61_500);
        var v2 = (CountdownTimerApp)noConfetti.App;
        Assert.Equal(0, v2.ConfettiCount);

        var again = Rig(1, autoRestart: true).Advance(61_000);
        var v3 = (CountdownTimerApp)again.App;
        Assert.True(v3.IsComplete);
        again.Advance(10_000);
        Assert.False(v3.IsComplete);
        Assert.InRange(v3.RemainingSeconds, 40, 60);
    }

    [Fact]
    public void TargetMode_CountsToTheMoment()
    {
        // 13:45:07 now, target 13:47:07 the same day: two minutes remain.
        var rig = Rig(target: "2026-10-02 13:47:07").Advance(1000);
        var view = (CountdownTimerApp)rig.App;
        Assert.InRange(view.RemainingSeconds, 118, 120);
        Assert.True(CountdownTimerApp_TryParse("18:30", out var t));
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 18, 30, 0, TimeSpan.Zero), t);
        Assert.True(CountdownTimerApp_TryParse("09:00", out var t2));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero), t2);
        Assert.False(CountdownTimerApp_TryParse("not a date", out _));
    }

    private static bool CountdownTimerApp_TryParse(string text, out DateTimeOffset t) =>
        CountdownTimerApp.TryParseTarget(text, new DateTimeOffset(2026, 10, 2, 13, 45, 7, TimeSpan.Zero), out t);

    [Fact]
    public void Settings_RoundTrip_IncludingOldPersistedValues()
    {
        var app = new CountdownTimerApp();
        app.UpdateSetting("durationMinutes", Json("12"));
        app.UpdateSetting("autoRestart", Json("true"));
        app.UpdateSetting("textColor", Json("\"Orange\""));
        app.UpdateSetting("backgroundColor", Json("\"DarkBlue\""));
        Assert.Equal((12, true, "Orange", "DarkBlue"), (app.DurationMinutes, app.AutoRestart, app.TextColor, app.BackgroundColor));
        app.UpdateSetting("durationMinutes", 500);
        Assert.Equal(99, app.DurationMinutes);
        app.UpdateSetting("textColor", "Chartreuse");
        Assert.Equal("Orange", app.TextColor);
        app.UpdateSetting("target", "2027-01-01 00:00");
        app.UpdateSetting("label", "NEW YEAR");
        app.UpdateSetting("celebrate", Json("false"));
        Assert.Equal(("2027-01-01 00:00", "NEW YEAR", false), (app.Target, app.Label, app.Celebrate));
        Assert.Equal(["durationMinutes", "autoRestart", "textColor", "backgroundColor", "target", "label", "showSeconds", "showDays", "celebrate"],
            app.GetSettings().Select(s => s.Key).ToArray());
    }

    [Fact]
    public void ChangingTheDuration_RestartsTheCountdown()
    {
        var rig = Rig(1).Advance(30_000);
        var view = (CountdownTimerApp)rig.App;
        Assert.InRange(view.RemainingSeconds, 29, 31);
        ((CountdownTimerApp)rig.App).UpdateSetting("durationMinutes", 3);
        rig.Advance(100);
        Assert.InRange(view.RemainingSeconds, 178, 180);
    }

    [Theory]
    [InlineData(2000, 5, "", "ambient_countdown_5min_t2000")]
    [InlineData(31_000, 1, "", "ambient_countdown_warning_t31000")]
    [InlineData(55_500, 1, "", "ambient_countdown_final_t55500")]
    [InlineData(58_400, 1, "BREAK", "ambient_countdown_label_t58400")]
    [InlineData(60_200, 1, "BREAK", "ambient_countdown_zero_t60200")]
    [InlineData(61_400, 1, "BREAK", "ambient_countdown_celebrate_t61400")]
    [InlineData(64_000, 1, "BREAK", "ambient_countdown_celebrate_t64000")]
    public void Snapshots(int ms, int minutes, string label, string name)
    {
        var rig = Rig(minutes, label: label).Advance(ms, 25);
        SnapshotHelper.AssertMatchesSnapshot(rig.Copy(), name);
    }

    [Fact]
    public void Snapshot_DaysAndHours_TargetMode()
    {
        var rig = Rig(target: "2026-12-25 00:00", label: "CHRISTMAS").Advance(2000);
        SnapshotHelper.AssertMatchesSnapshot(rig.Copy(), "ambient_countdown_days_t2000");
    }

    [Fact]
    public void Snapshot_InvalidTarget_ShowsError()
    {
        var rig = Rig(target: "next tuesday-ish").Advance(2000);
        Assert.True(((CountdownTimerApp)rig.App).TargetInvalid);
        SnapshotHelper.AssertMatchesSnapshot(rig.Copy(), "ambient_countdown_invalid_target");
    }

    [Fact]
    public void Snapshot_NoSecondsNoDays()
    {
        var rig = Rig(target: "2026-12-25 00:00", label: "CHRISTMAS");
        var app = (CountdownTimerApp)rig.App;
        app.ShowSeconds = false; app.ShowDays = false;
        rig.Advance(2000);
        SnapshotHelper.AssertMatchesSnapshot(rig.Copy(), "ambient_countdown_no_seconds_no_days");
    }

    [Fact]
    public void Snapshot_HoursNoLabel()
    {
        var rig = Rig(target: "2026-10-02 18:10:00").Advance(2000);
        SnapshotHelper.AssertMatchesSnapshot(rig.Copy(), "ambient_countdown_hours_t2000");
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(1, 61_000)]
    public void SteadyState_AllocatesNothingPerFrame(int minutes, int priorMs)
    {
        var rig = Rig(minutes);
        if (priorMs > 0) rig.Advance(priorMs);
        rig.AssertNoAllocationsPerFrame();
    }
}
