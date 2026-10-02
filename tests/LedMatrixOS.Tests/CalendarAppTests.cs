using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Graphics.Text;
using Xunit;

namespace LedMatrixOS.Tests;

public class CalendarAppTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 13, 45, 7, TimeSpan.Zero);   // FakeTime's clock

    private static CalEvent Ev(string title, DateTimeOffset start, int minutes = 60, string? place = null, bool allDay = false) =>
        new(title, start, start.AddMinutes(minutes), allDay, place);

    private static CalEvent Day(string title, int daysFromToday) =>
        Ev(title, new DateTimeOffset(Now.Date.AddDays(daysFromToday), TimeSpan.Zero), 24 * 60, allDay: true);

    // ---- wording ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(-30, 60, "NOW", 3)]
    [InlineData(3, 30, "3 MIN", 2)]
    [InlineData(12, 30, "12 MIN", 1)]
    [InlineData(45, 30, "45 MIN", 0)]
    [InlineData(90, 30, "15:15", 0)]
    [InlineData(60 * 24, 30, "TMRW 13:45", 0)]
    [InlineData(60 * 24 * 3, 30, "MON 13:45", 0)]
    public void When_FormatsTimedEvents(int startOffsetMinutes, int length, string text, int kind)
    {
        var (t, k) = CalendarFormat.When(Ev("x", Now.AddMinutes(startOffsetMinutes), length), Now);
        Assert.Equal(text, t);
        Assert.Equal((WhenKind)kind, k);
    }

    [Fact]
    public void When_FormatsAllDayEvents()
    {
        Assert.Equal(("TODAY", WhenKind.Ongoing), CalendarFormat.When(Day("a", 0), Now));
        Assert.Equal(("TOMORROW", WhenKind.Later), CalendarFormat.When(Day("a", 1), Now));
        Assert.Equal("MON 5 JAN", CalendarFormat.When(Day("a", 3), Now).Text);
    }

    [Fact]
    public void FirstUpcoming_SkipsFinishedEvents()
    {
        var events = new[] { Ev("done", Now.AddHours(-3)), Ev("now", Now.AddMinutes(-10)), Ev("later", Now.AddHours(2)) };
        Assert.Equal(1, CalendarFormat.FirstUpcoming(events, Now));
        Assert.Equal(-1, CalendarFormat.FirstUpcoming(events[..1], Now));
    }

    // ---- app -------------------------------------------------------------------------------------------------------

    private static (CalendarApp App, AppStage Stage) Rig(List<CalEvent>? events, string url = "https://example.com/c.ics", bool showAllDay = true)
    {
        Fonts.Load();
        var app = new CalendarApp(new HttpClient()) { Time = new FakeTime(), Zone = TimeZoneInfo.Utc, ShowAllDay = showAllDay };
        app.UseData(new MutableLive<List<CalEvent>> { Value = events }, url);
        var stage = new AppStage(app);
        stage.Step(33, 10);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        TubeFixtures.Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    private static List<CalEvent> Agenda() =>
    [
        Ev("Design review with the platform team", Now.AddMinutes(12), 45, "Meeting room 4, 2nd floor"),
        Ev("1:1 Sam", Now.AddHours(2), 30),
        Day("Dentist", 1),
        Ev("Team dinner", Now.AddDays(1).AddHours(5), 120, "The Crown"),
    ];

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new CalendarApp(new HttpClient());
        Assert.Equal("calendar", app.Id);
        Assert.Equal(["showAllDay"], app.GetSettings().Select(s => s.Key));
    }

    [Fact]
    public void Golden_NextEventSoon()
    {
        var (_, stage) = Rig(Agenda());
        Golden(stage, "calendar_soon");
    }

    [Fact]
    public void Golden_EventInProgress()
    {
        var (_, stage) = Rig([Ev("Sprint planning", Now.AddMinutes(-20), 60, "Zoom"), .. Agenda().Skip(1)]);
        Golden(stage, "calendar_now");
    }

    [Fact]
    public void Golden_NothingComingUp()
    {
        var (_, stage) = Rig([Ev("Yesterday", Now.AddDays(-1))]);
        Golden(stage, "calendar_empty");
    }

    [Fact]
    public void Golden_NotConfigured()
    {
        var (_, stage) = Rig(null, url: "");
        Golden(stage, "calendar_not_configured");
    }

    [Fact]
    public void HidingAllDayEventsRemovesThemFromTheList()
    {
        var (app, stage) = Rig([Day("Holiday", 0), Ev("Standup", Now.AddHours(1))], showAllDay: false);
        stage.Step(33);
        Assert.Single(app.Board!.Events);
        Assert.Equal("Standup", app.Board.Events[0].Title);
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Rig(Agenda());
        for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }

        long least = long.MaxValue;
        for (int window = 0; window < 8; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++) { stage.Step(33); stage.Render(); }
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(least < 256, $"least allocation: {least} bytes");
    }
}
