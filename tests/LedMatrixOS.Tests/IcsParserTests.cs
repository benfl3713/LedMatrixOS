using LedMatrixOS.Apps.Calendar;
using Xunit;

namespace LedMatrixOS.Tests;

public class IcsParserTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset From = new(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);      // a Monday
    private static readonly DateTimeOffset To = From.AddDays(14);

    private static string Cal(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("", events.Select(e => $"BEGIN:VEVENT\r\n{e}\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

    private static List<CalEvent> Parse(string ics) => IcsParser.Parse(ics, From, To, Utc);

    [Fact]
    public void ParsesUtcEventWithEscapesAndFoldedLines()
    {
        var events = Parse(Cal("DTSTART:20260303T093000Z\r\nDTEND:20260303T103000Z\r\nSUMMARY:Planning\\, Q2 \r\n  review\r\nLOCATION:Room 4\\;Floor 2"));
        var e = Assert.Single(events);
        Assert.Equal("Planning, Q2  review", e.Title);
        Assert.Equal("Room 4;Floor 2", e.Location);
        Assert.Equal(new DateTimeOffset(2026, 3, 3, 9, 30, 0, TimeSpan.Zero), e.Start);
        Assert.Equal(TimeSpan.FromHours(1), e.End - e.Start);
        Assert.False(e.AllDay);
    }

    [Fact]
    public void AllDayEventsSpanTheirDates()
    {
        var e = Assert.Single(Parse(Cal("DTSTART;VALUE=DATE:20260305\r\nDTEND;VALUE=DATE:20260307\r\nSUMMARY:Holiday")));
        Assert.True(e.AllDay);
        Assert.Equal(new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), e.Start);
        Assert.Equal(TimeSpan.FromDays(2), e.End - e.Start);
    }

    [Fact]
    public void TzidTimesAreConvertedToTheLocalZone()
    {
        TimeZoneInfo ny;
        try { ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); } catch (TimeZoneNotFoundException) { return; }
        var e = Assert.Single(Parse(Cal("DTSTART;TZID=America/New_York:20260310T090000\r\nSUMMARY:Standup")));
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 13, 0, 0, TimeSpan.Zero).UtcDateTime, e.Start.UtcDateTime);   // EDT (UTC-4) from 8 Mar
    }

    [Fact]
    public void EventsOutsideTheWindowAreDropped_AndResultIsSorted()
    {
        var events = Parse(Cal(
            "DTSTART:20260306T100000Z\r\nSUMMARY:Later",
            "DTSTART:20260303T100000Z\r\nSUMMARY:Sooner",
            "DTSTART:20260101T100000Z\r\nSUMMARY:Ancient",
            "DTSTART:20260601T100000Z\r\nSUMMARY:Far"));
        Assert.Equal(["Sooner", "Later"], events.Select(e => e.Title));
    }

    [Fact]
    public void CancelledAndMalformedEventsAreSkipped()
    {
        var events = Parse(Cal(
            "DTSTART:20260303T100000Z\r\nSUMMARY:Gone\r\nSTATUS:CANCELLED",
            "DTSTART:garbage\r\nSUMMARY:Bad",
            "SUMMARY:No start",
            "DTSTART:20260304T100000Z\r\nSUMMARY:Fine"));
        Assert.Equal(["Fine"], events.Select(e => e.Title));
    }

    [Fact]
    public void DailyRecurrenceWithCountAndExdate()
    {
        var events = Parse(Cal("DTSTART:20260302T080000Z\r\nRRULE:FREQ=DAILY;COUNT=5\r\nEXDATE:20260304T080000Z\r\nSUMMARY:Run"));
        Assert.Equal([2, 3, 5, 6], events.Select(e => e.Start.Day));   // five occurrences from the 2nd, the 4th excluded
    }

    [Fact]
    public void WeeklyByDayWithIntervalAndUntil()
    {
        var events = Parse(Cal("DTSTART:20260302T180000Z\r\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;UNTIL=20260311\r\nSUMMARY:Class"));
        Assert.Equal([2, 4], events.Select(e => e.Start.Day));   // week 0 Mon+Wed; the 9th and 11th fall in the skipped week
    }

    [Fact]
    public void WeeklyWithoutByDayUsesTheStartWeekday()
    {
        var events = Parse(Cal("DTSTART:20260225T120000Z\r\nRRULE:FREQ=WEEKLY\r\nSUMMARY:Lunch"));   // Wed 25 Feb, started before the window
        Assert.Equal([4, 11], events.Select(e => e.Start.Day));
    }

    [Fact]
    public void MonthlyAndYearlyRecurrence()
    {
        var events = Parse(Cal(
            "DTSTART:20260103T100000Z\r\nRRULE:FREQ=MONTHLY\r\nSUMMARY:Rent",
            "DTSTART:20200305T100000Z\r\nRRULE:FREQ=YEARLY\r\nSUMMARY:Birthday"));
        Assert.Equal(["Rent", "Birthday"], events.Select(e => e.Title));
        Assert.Equal([3, 5], events.Select(e => e.Start.Day));
    }

    [Fact]
    public void OngoingEventStartedBeforeTheWindowIsKept()
    {
        var e = Assert.Single(Parse(Cal("DTSTART:20260228T100000Z\r\nDTEND:20260303T100000Z\r\nSUMMARY:Conference")));
        Assert.Equal("Conference", e.Title);
    }

    // ---- RECURRENCE-ID overrides and EXDATE ---------------------------------------------------------------------------

    private const string Standup = "UID:standup-1\r\nDTSTART:20260302T090000Z\r\nDTEND:20260302T091500Z\r\nRRULE:FREQ=DAILY;COUNT=4\r\nSUMMARY:Standup";

    [Fact]
    public void MovedInstanceReplacesTheGeneratedOccurrence()
    {
        var events = Parse(Cal(
            Standup,
            "UID:standup-1\r\nRECURRENCE-ID:20260303T090000Z\r\nDTSTART:20260303T140000Z\r\nDTEND:20260303T143000Z\r\nSUMMARY:Standup (moved)\r\nLOCATION:Room 2"));

        Assert.Equal(["Standup", "Standup (moved)", "Standup", "Standup"], events.Select(e => e.Title));
        var moved = events[1];
        Assert.Equal(new DateTimeOffset(2026, 3, 3, 14, 0, 0, TimeSpan.Zero), moved.Start);
        Assert.Equal(TimeSpan.FromMinutes(30), moved.End - moved.Start);
        Assert.Equal("Room 2", moved.Location);
        Assert.DoesNotContain(events, e => e.Title == "Standup" && e.Start.Day == 3);   // the 09:00 original is gone
    }

    [Fact]
    public void CancelledInstanceRemovesJustThatOccurrence()
    {
        var events = Parse(Cal(
            Standup,
            "UID:standup-1\r\nRECURRENCE-ID:20260304T090000Z\r\nDTSTART:20260304T090000Z\r\nSTATUS:CANCELLED\r\nSUMMARY:Standup"));

        Assert.Equal([2, 3, 5], events.Select(e => e.Start.Day));
    }

    [Fact]
    public void OverridesAreMatchedByInstantAcrossTimeZones_AndOnlyWithinTheirOwnUid()
    {
        TimeZoneInfo ny;
        try { ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); } catch (TimeZoneNotFoundException) { return; }
        // 09:00 New York on the 10th (EDT, UTC-4) is 13:00 UTC; the override names it in UTC, the other series shares the time but not the UID
        var events = IcsParser.Parse(Cal(
            "UID:a\r\nDTSTART;TZID=America/New_York:20260309T090000\r\nRRULE:FREQ=DAILY;COUNT=3\r\nSUMMARY:Series A",
            "UID:a\r\nRECURRENCE-ID:20260310T130000Z\r\nDTSTART:20260310T130000Z\r\nSTATUS:CANCELLED\r\nSUMMARY:Series A",
            "UID:b\r\nDTSTART;TZID=America/New_York:20260310T090000\r\nSUMMARY:Series B"), From, To, ny);

        Assert.Equal(["Series A", "Series B", "Series A"], events.Select(e => e.Title));
    }

    [Fact]
    public void OverrideWithoutAnyGeneratedMatchStillShowsOnItsOwn()
    {
        var e = Assert.Single(Parse(Cal("UID:lost\r\nRECURRENCE-ID:20260101T090000Z\r\nDTSTART:20260305T090000Z\r\nSUMMARY:Orphan")));
        Assert.Equal("Orphan", e.Title);
    }

    [Fact]
    public void ExdateRemovesOccurrences_AcrossLinesListsAndDateOnlyValues()
    {
        var events = Parse(Cal(
            "UID:x\r\nDTSTART:20260302T080000Z\r\nRRULE:FREQ=DAILY;COUNT=7\r\nEXDATE:20260303T080000Z,20260304T080000Z\r\nEXDATE;VALUE=DATE:20260306\r\nSUMMARY:Run"));

        Assert.Equal([2, 5, 7, 8], events.Select(e => e.Start.Day));
    }

    [Fact]
    public void AllDayRecurrenceHonoursExdateAndOverride()
    {
        var events = Parse(Cal(
            "UID:d\r\nDTSTART;VALUE=DATE:20260302\r\nRRULE:FREQ=DAILY;COUNT=4\r\nEXDATE;VALUE=DATE:20260303\r\nSUMMARY:Bins",
            "UID:d\r\nRECURRENCE-ID;VALUE=DATE:20260304\r\nDTSTART;VALUE=DATE:20260306\r\nSUMMARY:Bins (late)"));

        Assert.Equal([("Bins", 2), ("Bins", 5), ("Bins (late)", 6)], events.Select(e => (e.Title, e.Start.Day)));
    }
}
