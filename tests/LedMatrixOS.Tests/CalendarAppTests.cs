using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
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

    private static (CalendarApp App, AppStage Stage) Rig(List<CalEvent>? events, string url = "https://example.com/c.ics", bool showAllDay = true, int timelineSeconds = 0)
    {
        Fonts.Load();
        var app = new CalendarApp(new HttpClient()) { Time = new FakeTime(), Zone = TimeZoneInfo.Utc, ShowAllDay = showAllDay, TimelineSeconds = timelineSeconds };
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
        Assert.Equal(["showAllDay", "timelineSeconds", "titleOverflow", "maxEvents", "lookAheadDays", "hideDeclined", "showLocation", "show24Hour"], app.GetSettings().Select(s => s.Key));
    }

    private static (CalendarApp App, AppStage Stage) Configured(List<CalEvent> events, Action<CalendarApp> configure)
    {
        Fonts.Load();
        var app = new CalendarApp(new HttpClient()) { Time = new FakeTime(), Zone = TimeZoneInfo.Utc };
        configure(app);
        app.UseData(new MutableLive<List<CalEvent>> { Value = events }, "https://example.com/c.ics");
        var stage = new AppStage(app);
        stage.Step(33, 10);
        return (app, stage);
    }

    private const string LongTitle = "Design review with the platform team and friends";

    [Fact]
    public void Golden_TitleOverflowWrap_TwoLinesWithoutLocation()
    {
        var (_, stage) = Configured([Ev(LongTitle, Now.AddMinutes(30), 45, "Room 4"), .. Agenda().Skip(1)], a => { a.TitleOverflow = "Wrap"; a.ShowLocation = false; });
        Golden(stage, "calendar_wrap_two_lines");
    }

    [Fact]
    public void Golden_TitleOverflowWrap_EllipsisWithLocation()
    {
        var (_, stage) = Configured([Ev(LongTitle, Now.AddMinutes(30), 45, "Room 4"), .. Agenda().Skip(1)], a => a.TitleOverflow = "Wrap");
        Golden(stage, "calendar_wrap_ellipsis");
    }

    [Fact]
    public void Golden_TitleOverflowClip()
    {
        var (_, stage) = Configured([Ev(LongTitle, Now.AddMinutes(30), 45, "Room 4"), .. Agenda().Skip(1)], a => a.TitleOverflow = "Clip");
        stage.Step(100, 40);   // a marquee would have moved by now; Clip stays put
        Golden(stage, "calendar_clip");
    }

    [Fact]
    public void Golden_TwelveHourClock()
    {
        var (_, stage) = Configured(Agenda(), a => a.Show24Hour = false);
        Golden(stage, "calendar_12h");
    }

    [Fact]
    public void Golden_TwelveHourTimeline()
    {
        var (_, stage) = Configured(Agenda().Take(2).ToList(), a => { a.Show24Hour = false; a.TimelineSeconds = 5; });
        stage.Step(100, 60);
        Golden(stage, "calendar_12h_timeline");
    }

    [Fact]
    public void LookAhead_HidesFarEvents()
    {
        var events = new List<CalEvent> { Ev("soon", Now.AddHours(2)), Ev("far", Now.AddDays(10)) };
        var (app, _) = Configured(events, a => a.LookAheadDays = 3);
        Assert.Equal(0, app.Model.Next.Count);
        (app, _) = Configured(events, a => a.LookAheadDays = 14);
        Assert.Single(app.Model.Next);
    }

    [Fact]
    public void HideDeclined_RemovesDeclinedEvents()
    {
        var declined = Ev("skip me", Now.AddHours(1)) with { Declined = true };
        var events = new List<CalEvent> { declined, Ev("keep me", Now.AddHours(2)) };
        var (app, _) = Configured(events, a => { });
        Assert.Equal("skip me", app.Model.Title);
        (app, _) = Configured(events, a => a.HideDeclined = true);
        Assert.Equal("keep me", app.Model.Title);
    }

    [Fact]
    public void MaxEvents_LimitsAndRotatesTheList()
    {
        var events = Enumerable.Range(0, 9).Select(i => Ev("event " + i, Now.AddHours(1 + i))).ToList();
        var (one, _) = Configured(events, a => a.MaxEvents = 1);
        Assert.Empty(one.Model.Next);
        var (four, _) = Configured(events, a => a.MaxEvents = 4);
        Assert.Equal(3, four.Model.Next.Count);
        var (eight, stage) = Configured(events, a => a.MaxEvents = 8);
        Assert.Equal("event 1", eight.Model.Next[0].Title);
        stage.Step(1000, 7);   // past the page time: the next window of the list
        Assert.Equal("event 4", eight.Model.Next[0].Title);
        Assert.Equal(3, eight.Model.Next.Count);
    }

    [Fact]
    public void IcsParser_FlagsDeclinedEvents()
    {
        const string ics = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:1\nDTSTART:20260103T100000Z\nDTEND:20260103T110000Z\nSUMMARY:Mine\n" +
            "ATTENDEE;PARTSTAT=DECLINED:mailto:me@example.com\nATTENDEE;PARTSTAT=ACCEPTED:mailto:you@example.com\nEND:VEVENT\n" +
            "BEGIN:VEVENT\nUID:2\nDTSTART:20260103T120000Z\nDTEND:20260103T130000Z\nSUMMARY:Solo\nATTENDEE;PARTSTAT=DECLINED:mailto:me@example.com\nEND:VEVENT\nEND:VCALENDAR\n";
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(10);
        var withSelf = IcsParser.Parse(ics, from, to, TimeZoneInfo.Utc, "me@example.com");
        Assert.True(withSelf.Single(e => e.Title == "Mine").Declined);
        Assert.True(withSelf.Single(e => e.Title == "Solo").Declined);
        var noSelf = IcsParser.Parse(ics, from, to, TimeZoneInfo.Utc);
        Assert.False(noSelf.Single(e => e.Title == "Mine").Declined);   // one decline among several is somebody else's
        Assert.True(noSelf.Single(e => e.Title == "Solo").Declined);
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

    // ---- progress, long titles, list animation -------------------------------------------------------------------------

    private static DateTimeOffset At(int hour, int minute = 0) => new DateTimeOffset(Now.Date, TimeSpan.Zero).AddHours(hour).AddMinutes(minute);

    [Fact]
    public void Golden_EventInProgressShowsHowFarThroughItIs()
    {
        var (app, stage) = Rig([Ev("Sprint planning", Now.AddMinutes(-45), 60, "Zoom"), .. Agenda().Skip(1)]);
        Assert.True(app.Model.ShowProgress);
        Assert.InRange(app.Model.Progress, 0.74f, 0.76f);
        Golden(stage, "calendar_now_progress");
    }

    [Fact]
    public void ProgressBarOnlyShowsWhileATimedEventIsHappening()
    {
        var (app, _) = Rig(Agenda());
        Assert.False(app.Model.ShowProgress);
        (app, _) = Rig([Day("Holiday", 0), Ev("Standup", Now.AddHours(1))]);
        Assert.False(app.Model.ShowProgress);   // all-day events have no meaningful elapsed share
    }

    [Fact]
    public void Golden_LongTitleScrollsInsteadOfTruncating()
    {
        const string title = "Quarterly planning offsite with the whole platform and data teams";
        var (app, stage) = Rig([Ev(title, Now.AddMinutes(30), 90, "Conference centre, main hall"), .. Agenda().Skip(1)]);
        Assert.Equal(title, app.Model.Title);   // the full text goes to the marquee, nothing is cut with an ellipsis
        stage.Step(100, 35);   // 2s rest, then about 1.5s of scrolling
        Golden(stage, "calendar_long_title");
    }

    [Fact]
    public void NextEventsListAnimatesWhenItChanges()
    {
        Fonts.Load();
        var live = new MutableLive<List<CalEvent>> { Value = Agenda() };
        var app = new CalendarApp(new HttpClient()) { Time = new FakeTime(), Zone = TimeZoneInfo.Utc };
        app.UseData(live);
        var stage = new AppStage(app);
        stage.Step(33, 10);

        var list = Find<ListView<CalEvent>>(app.Root!)!;
        Assert.Equal(3, list.Count);

        // the dentist event goes away: its row collapses (still present while animating), then is dropped
        live.Value = [.. Agenda().Where(e => e.Title != "Dentist")];
        stage.Step(33, 3);
        Assert.Equal(2, list.Count);
        Assert.Equal(3, list.Children.Count);
        stage.Step(33, 20);
        Assert.Equal(2, list.Children.Count);
    }

    private static T? Find<T>(Node node) where T : Node
    {
        if (node is T hit) return hit;
        if (node is Container container)
            foreach (var child in container.Children)
                if (Find<T>(child) is { } found) return found;
        return null;
    }

    // ---- today timeline --------------------------------------------------------------------------------------------------

    private static (CalendarApp App, AppStage Stage) TimelineRig(List<CalEvent> events)
    {
        var (app, stage) = Rig(events, timelineSeconds: 3);
        stage.Step(100, 40);   // 3s on the board, 0.5s sliding, then settled on the timeline
        Assert.Equal(1, app.Pages!.PageIndex);
        return (app, stage);
    }

    private static List<CalEvent> BusyDay() =>
    [
        Day("Team offsite", 0),
        Ev("Standup", At(9), 15),
        Ev("Design review", At(11), 90, "Room 4"),
        Ev("Lunch and learn", At(12, 30), 90),
        Ev("1:1 Sam", At(15, 45), 30),
        Ev("Team dinner", At(19), 120, "The Crown"),
        Ev("Tomorrow thing", At(10).AddDays(1), 60),
    ];

    private static List<CalEvent> OverlappingDay() =>
    [
        Day("Birthday cake", 0),
        Day("Bank holiday", 0),
        Ev("Planning", At(10), 120),
        Ev("Design review", At(10, 30), 60),
        Ev("Interview", At(11), 90),
        Ev("Lunch", At(13), 60),
        Ev("Call with Priya", At(13, 30), 60),
        Ev("Gym", At(18), 60),
        Ev("Late shipment", At(21), 180),   // runs past midnight, so the axis stretches to the end of the day
    ];

    [Fact]
    public void Golden_TodayTimeline()
    {
        var (_, stage) = TimelineRig(BusyDay());
        Golden(stage, "calendar_timeline");
    }

    [Fact]
    public void Golden_TodayTimelineOverlappingEventsStackInLanes()
    {
        var (app, stage) = TimelineRig(OverlappingDay());
        Assert.Equal(3, app.Model.Lanes);
        Assert.Equal(2, app.Model.AllDayCount);
        Golden(stage, "calendar_timeline_overlap");
    }

    [Fact]
    public void Timeline_LaysOutLanesAndWindow()
    {
        var (app, _) = Rig(OverlappingDay());
        var m = app.Model;
        Assert.Equal(7, m.BarCount);
        Assert.Equal([0, 1, 2, 0, 1, 0, 0], m.Bars.Take(m.BarCount).Select(b => b.Lane));
        Assert.Equal(7f, m.StartHour);
        Assert.Equal(24f, m.EndHour);
        Assert.Equal("Birthday cake, Bank holiday", m.AllDayText);

        (app, _) = Rig(BusyDay());
        Assert.Equal(7f, app.Model.StartHour);
        Assert.Equal(23f, app.Model.EndHour);
        Assert.Equal(1, app.Model.Lanes);
        Assert.Equal(5, app.Model.BarCount);   // tomorrow's event is not on today's axis

        (app, _) = Rig([Ev("Early flight", At(5), 60), Ev("Tomorrow", At(10).AddDays(1))]);
        Assert.Equal(5f, app.Model.StartHour);
        (app, _) = Rig([Ev("Tomorrow", At(10).AddDays(1))]);
        Assert.Equal(0, app.Model.BarCount);
        Assert.Equal("Nothing scheduled", app.Model.AllDayText);
    }

    [Fact]
    public void TimelineSeconds_TurnsThePagerOnAndOff()
    {
        var (app, stage) = Rig(Agenda());
        Assert.Equal(1, app.Pages!.PageCount);

        app.TimelineSeconds = 4;
        stage.Step(33, 2);
        Assert.Equal(2, app.Pages.PageCount);
        Assert.Equal(TimeSpan.FromSeconds(4), app.Pages.Interval);

        app.TimelineSeconds = 0;
        stage.Step(33, 2);
        Assert.Equal(1, app.Pages.PageCount);
        Assert.Equal(0, app.Pages.PageIndex);
    }

    [Fact]
    public void Timeline_SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Rig(BusyDay(), timelineSeconds: 5);
        stage.Step(100, 60);   // onto the timeline page
        for (int i = 0; i < 20; i++) { stage.Step(33); stage.Render(); }

        long least = long.MaxValue;
        for (int window = 0; window < 6; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++) { stage.Step(33); stage.Render(); }
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(least < 256, $"least allocation: {least} bytes");
    }

    [Fact]
    public void HidingAllDayEventsRemovesThemFromTheList()
    {
        var (app, stage) = Rig([Day("Holiday", 0), Ev("Standup", Now.AddHours(1))], showAllDay: false);
        stage.Step(33);
        Assert.Single(app.Model.Events);
        Assert.Equal("Standup", app.Model.Events[0].Title);
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
