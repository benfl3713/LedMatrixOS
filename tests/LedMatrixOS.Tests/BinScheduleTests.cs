using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class BinScheduleTests
{
    private static readonly Pixel Grey = new(58, 58, 58);

    private static BinRule Rule(string name, DayOfWeek day, int every, string? anchor, params string[] skips) =>
        new(name, Grey, day, every, anchor is null ? null : DateOnly.Parse(anchor), skips.Select(DateOnly.Parse).ToArray());

    [Theory]
    [InlineData("2026-10-05", "2026-10-05", "2026-10-05")]   // anchor day itself
    [InlineData("2026-10-06", "2026-10-05", "2026-10-19")]   // fortnightly: next on-week
    [InlineData("2026-10-19", "2026-10-05", "2026-10-19")]
    [InlineData("2026-10-20", "2026-10-05", "2026-11-02")]
    [InlineData("2026-09-25", "2026-10-05", "2026-10-05")]   // before the anchor
    [InlineData("2026-09-01", "2026-10-19", "2026-09-07")]   // anchor in the future, grid extends backwards
    [InlineData("2026-09-08", "2026-10-19", "2026-09-21")]
    [InlineData("2026-12-29", "2026-10-05", "2027-01-11")]   // year boundary (12-28 then 01-11)
    public void Fortnightly_FollowsTheAnchorParity(string from, string anchor, string expected)
    {
        var rule = Rule("Black", DayOfWeek.Monday, 2, anchor);
        Assert.Equal(DateOnly.Parse(expected), rule.NextOnOrAfter(DateOnly.Parse(from)));
    }

    [Theory]
    [InlineData("2026-10-03", "2026-10-07")]   // Saturday -> Wednesday
    [InlineData("2026-10-07", "2026-10-07")]
    [InlineData("2026-10-08", "2026-10-14")]
    [InlineData("2026-12-31", "2027-01-06")]
    public void Weekly_WithoutAnchor_UsesTheWeekday(string from, string expected)
    {
        var rule = Rule("Garden", DayOfWeek.Wednesday, 1, null);
        Assert.Equal(DateOnly.Parse(expected), rule.NextOnOrAfter(DateOnly.Parse(from)));
    }

    [Fact]
    public void EveryFourWeeks_AndMonthBoundary()
    {
        var rule = Rule("Glass", DayOfWeek.Friday, 4, "2026-10-02");
        Assert.Equal(new DateOnly(2026, 10, 30), rule.NextOnOrAfter(new DateOnly(2026, 10, 3)));
        Assert.Equal(new DateOnly(2026, 11, 27), rule.NextOnOrAfter(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void SkipDates_RemoveThatCollectionOnly()
    {
        var rule = Rule("Black", DayOfWeek.Monday, 1, "2026-12-21", "2026-12-28");
        Assert.Equal(new DateOnly(2027, 1, 4), rule.NextOnOrAfter(new DateOnly(2026, 12, 22)));
        Assert.Equal(new DateOnly(2026, 12, 21), rule.NextOnOrAfter(new DateOnly(2026, 12, 21)));
    }

    [Fact]
    public void Upcoming_ListsEveryBinInDateOrder_AndNextPerBinOnePerBin()
    {
        var schedule = new CollectionSchedule([
            Rule("Black", DayOfWeek.Monday, 2, "2026-10-05"),
            Rule("Recycling", DayOfWeek.Monday, 2, "2026-10-12"),
            Rule("Garden", DayOfWeek.Wednesday, 1, "2026-10-07")]);
        var today = new DateOnly(2026, 10, 3);

        var up = schedule.Upcoming(today, 11);
        Assert.Equal(["Black", "Garden", "Recycling", "Garden"], up.Select(c => c.Name).ToArray());

        var next = schedule.NextPerBin(today);
        Assert.Equal(["Black", "Garden", "Recycling"], next.Select(c => c.Name).ToArray());
        Assert.Equal(new DateOnly(2026, 10, 12), next[2].Date);
    }

    [Fact]
    public void Extras_AreMergedAndPastOnesIgnored()
    {
        var schedule = new CollectionSchedule([], [new Collection("Bulky", Grey, new DateOnly(2026, 10, 9)), new Collection("Old", Grey, new DateOnly(2026, 9, 1))]);
        Assert.Equal(["Bulky"], schedule.NextPerBin(new DateOnly(2026, 10, 3)).Select(c => c.Name).ToArray());
        Assert.Single(schedule.On(new DateOnly(2026, 10, 9)));
    }

    [Fact]
    public void IsDue_FromTheEveningBeforeUntilTheEndOfTheDay()
    {
        var schedule = new CollectionSchedule([Rule("Black", DayOfWeek.Monday, 1, null)]);
        Assert.False(schedule.IsDue(new DateTime(2026, 10, 4, 16, 59, 0), 17));   // Sunday afternoon
        Assert.True(schedule.IsDue(new DateTime(2026, 10, 4, 17, 0, 0), 17));
        Assert.True(schedule.IsDue(new DateTime(2026, 10, 5, 23, 59, 0), 17));    // collection day
        Assert.False(schedule.IsDue(new DateTime(2026, 10, 6, 0, 0, 0), 17));
        Assert.False(schedule.IsDue(new DateTime(2026, 10, 4, 17, 0, 0), 20));
    }

    // ---- parsing ----------------------------------------------------------------------------------------------

    [Fact]
    public void ParseBins_ReadsTheDocumentedSyntax_AndReportsBadEntries()
    {
        var (rules, errors) = BinParser.ParseBins(
            "Black|#3a3a3a|Mon|2|2026-10-05; Recycling|#1e90ff|Mon|2|2026-10-12\nGarden|#2ea043|Wed|1|2026-10-07|2026-12-30;" +
            "Bad|#zzz|Mon|1|; NoAnchor|#fff|Tue|2|; WrongDay|#fff|Tue|1|2026-10-05; Weeks|#fff|Tue|9|; |#fff|Mon");
        Assert.Equal(["Black", "Recycling", "Garden"], rules.Select(r => r.Name).ToArray());
        Assert.Equal(new Pixel(0x3a, 0x3a, 0x3a), rules[0].Colour);
        Assert.Equal(DayOfWeek.Wednesday, rules[2].Weekday);
        Assert.Equal([new DateOnly(2026, 12, 30)], rules[2].Skips);
        Assert.Equal(5, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("Bad"));
    }

    [Fact]
    public void ParseBins_EmptyOrNull_IsEmptyWithoutErrors()
    {
        Assert.Empty(BinParser.ParseBins(null).Rules);
        Assert.Empty(BinParser.ParseBins("  ;\n ").Errors);
        Assert.Equal(new Pixel(0x11, 0x22, 0x33), BinParser.ParseBins("A|#123|Fri").Rules[0].Colour);
    }

    // ---- reminders --------------------------------------------------------------------------------------------

    private static Reminder Pills(string days = "Mon,Tue,Wed,Thu,Fri,Sat,Sun", string from = "08:00", string to = "20:00") =>
        BinParser.ParseReminders($"Take pills|{from}|{to}|{days}").Reminders.Single();

    [Theory]
    [InlineData(7, 59, false)]
    [InlineData(8, 0, true)]
    [InlineData(19, 59, true)]
    [InlineData(20, 0, false)]
    public void Reminder_Window_StartInclusiveEndExclusive(int h, int m, bool expected) =>
        Assert.Equal(expected, Pills().IsActive(new DateTime(2026, 10, 5, h, m, 0)));

    [Fact]
    public void Reminder_OnlyOnTheChosenDays()
    {
        var r = Pills("Mon,Fri");
        Assert.True(r.IsActive(new DateTime(2026, 10, 5, 9, 0, 0)));    // Monday
        Assert.False(r.IsActive(new DateTime(2026, 10, 6, 9, 0, 0)));   // Tuesday
    }

    [Fact]
    public void Reminder_WrapsPastMidnight_BelongingToTheStartDay()
    {
        var r = Pills("Fri", "22:00", "02:00");
        Assert.True(r.IsActive(new DateTime(2026, 10, 9, 23, 0, 0)));   // Friday night
        Assert.True(r.IsActive(new DateTime(2026, 10, 10, 1, 30, 0)));  // Saturday small hours
        Assert.False(r.IsActive(new DateTime(2026, 10, 10, 2, 0, 0)));
        Assert.False(r.IsActive(new DateTime(2026, 10, 9, 1, 0, 0)));   // Friday small hours belong to Thursday
        Assert.False(r.IsActive(new DateTime(2026, 10, 10, 23, 0, 0))); // Saturday night
    }

    [Fact]
    public void ParseReminders_DefaultsToDaily_AndRejectsBadEntries()
    {
        var (list, errors) = BinParser.ParseReminders("Water plants|18:00|19:00; Bad|25:00|19:00|Mon; Same|08:00|08:00; Days|08:00|09:00|Moo");
        Assert.Single(list);
        Assert.Equal(0x7F, list[0].DayMask);
        Assert.Equal(3, errors.Count);
    }
}
