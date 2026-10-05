using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Briefing;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class MorningBriefingAppTests(ITestOutputHelper output)
{
    // Friday 2 January 2026, 07:30 (the fake clock's local zone is UTC).
    private static readonly DateTimeOffset Morning = new(2026, 1, 2, 7, 30, 0, TimeSpan.Zero);
    private const string TwoBins = "Black|#3a3a3a|Fri|1|2026-01-02; Garden|#2ea043|Sat|1|2026-01-03";

    private static CalEvent Event(string title, int hour, int minute, int minutes = 45, int dayOffset = 0, bool allDay = false)
    {
        var start = Morning.Date.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        var s = new DateTimeOffset(start, TimeSpan.Zero);
        return new CalEvent(title, allDay ? new DateTimeOffset(Morning.Date.AddDays(dayOffset), TimeSpan.Zero) : s, allDay ? s.AddDays(1) : s.AddMinutes(minutes), allDay, null);
    }

    private static List<CalEvent> TodayEvents() =>
    [
        Event("Stand-up", 9, 30, 15),
        Event("Design review with the platform team", 11, 0, 60),
        Event("Dentist", 16, 15, 45),
        Event("Tomorrow only", 10, 0, 30, dayOffset: 1),
    ];

    private sealed record Rig(MorningBriefingApp App, AppStage Stage);

    private static Rig Make(DateTimeOffset? now = null, bool weather = true, List<CalEvent>? events = null, bool arrivals = true, string bins = "",
        string station = "940GZZLUVIC", int walk = 1, Action<MorningBriefingApp>? configure = null, int warm = 3)
    {
        Fonts.Load();
        var app = new MorningBriefingApp { Time = new FakeTime { Now = now ?? Morning }, StationId = station, WalkMinutes = walk, Bins = bins };
        configure?.Invoke(app);
        var snapshot = weather ? new FakeWeatherSource(code: 61, isDay: true, tempC: 11).GetAsync(new WeatherQuery("London", false), default).Result : null;
        app.UseData(
            weather ? new MutableLive<WeatherSnapshot> { Value = snapshot } : null,
            events is null ? null : new MutableLive<List<CalEvent>> { Value = events },
            arrivals ? new MutableLive<TflArrival[]> { Value = CommuteBoard() } : null,
            arrivals ? new MutableLive<LineStatus[]> { Value = StationLines() } : null);
        var stage = new AppStage(app);
        stage.Step(33, warm);
        return new Rig(app, stage);
    }

    private static Rig Full(Action<MorningBriefingApp>? configure = null) => Make(events: TodayEvents(), bins: TwoBins, configure: configure);

    private static void RunUntil(AppStage stage, Func<bool> done, int maxSeconds = 120)
    {
        for (int i = 0; i < maxSeconds * 30 && !done(); i++) stage.Step(33);
        Assert.True(done());
    }

    /// <summary>Plays forward until card <paramref name="index"/> is at rest, then lets its entrance finish.</summary>
    private static void GoTo(Rig rig, int index)
    {
        RunUntil(rig.Stage, () => rig.App.Pager!.PageIndex == index && !rig.App.Pager.IsTransitioning);
        rig.Stage.Step(33, 25);
    }

    private static void Golden(Rig rig, string name)
    {
        var frame = rig.Stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- identity & settings ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new MorningBriefingApp();
        Assert.Equal("morning-briefing", app.Id);
        Assert.Equal("Morning Briefing", app.Name);
        Assert.Equal(new[] { "stationId", "walkMinutes", "pageSeconds", "bins", "showWeather", "showCalendar", "showCommute", "showBins", "units" },
            app.GetSettings().Select(s => s.Key).ToArray());
        var seconds = app.GetSettings().Single(s => s.Key == "pageSeconds");
        Assert.Equal(6, seconds.CurrentValue);
        Assert.Equal(3, seconds.MinValue);
        Assert.Equal(20, seconds.MaxValue);
        Assert.All(new[] { "showWeather", "showCalendar", "showCommute", "showBins" }, k => Assert.Equal(true, app.GetSettings().Single(s => s.Key == k).CurrentValue));
        Assert.Contains(typeof(MorningBriefingApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void StationSelect_SetsTheStation()
    {
        Fonts.Load();
        var app = new MorningBriefingApp();
        app.UpdateSetting("stationSelect", "940GZZLUBST | Baker Street Underground Station");
        Assert.Equal("940GZZLUBST", app.StationId);
    }

    [Fact]
    public void SettingsDescription_ExplainsTheTotalSeconds()
    {
        var rig = Full();
        var description = rig.App.GetSettings().Single(s => s.Key == "pageSeconds").Description;
        Assert.Contains("6 cards x 6 s = 36 s", description);
    }

    // ---- greeting ---------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "Good morning")]
    [InlineData(5, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(17, "Good afternoon")]
    [InlineData(18, "Good evening")]
    [InlineData(23, "Good evening")]
    public void Greeting_FollowsTheAppClockHour(int hour, string expected)
    {
        Assert.Equal(expected, BriefingGreeting.ForHour(hour));
        var rig = Make(now: new DateTimeOffset(2026, 1, 2, hour, 10, 0, TimeSpan.Zero));
        Assert.Equal(expected, rig.App.Model.GreetingText);
        Assert.Equal("Friday 2 January", rig.App.Model.DateText);
    }

    [Fact]
    public void Greeting_UsesTheAppClockNotTheSystemClock()
    {
        var rig = Make(now: new DateTimeOffset(2031, 7, 14, 21, 0, 0, TimeSpan.Zero));
        Assert.Equal("Good evening", rig.App.Model.GreetingText);
        Assert.Equal("Monday 14 July", rig.App.Model.DateText);
    }

    // ---- page selection ----------------------------------------------------------------------------------------------------------------

    private static BriefingPage[] PagesOf(Rig rig) => rig.App.Pages.ToArray();

    [Fact]
    public void Pages_AllConfigured_AreInOrder()
    {
        var rig = Full();
        Assert.Equal([BriefingPage.Greeting, BriefingPage.Weather, BriefingPage.Calendar, BriefingPage.Commute, BriefingPage.Bins, BriefingPage.SignOff], PagesOf(rig));
        Assert.Equal(6, rig.App.PageCount);
        Assert.Equal(36, rig.App.TotalSeconds);
    }

    [Fact]
    public void Pages_MissingData_IsSkipped()
    {
        Assert.DoesNotContain(BriefingPage.Weather, PagesOf(Make(weather: false, events: TodayEvents(), bins: TwoBins)));
        Assert.DoesNotContain(BriefingPage.Calendar, PagesOf(Make(events: null, bins: TwoBins)));       // calendar not configured
        Assert.DoesNotContain(BriefingPage.Commute, PagesOf(Make(arrivals: false, events: TodayEvents(), bins: TwoBins)));
        Assert.DoesNotContain(BriefingPage.Commute, PagesOf(Make(station: "", events: TodayEvents(), bins: TwoBins)));
        Assert.DoesNotContain(BriefingPage.Bins, PagesOf(Make(events: TodayEvents(), bins: "")));         // nothing set up
        Assert.DoesNotContain(BriefingPage.Bins, PagesOf(Make(events: TodayEvents(), bins: "Blue|#0000ff|Wed|1|2026-01-07")));   // not due today or tomorrow
    }

    [Fact]
    public void Pages_SwitchedOff_AreSkipped()
    {
        Assert.DoesNotContain(BriefingPage.Weather, PagesOf(Full(a => a.ShowWeather = false)));
        Assert.DoesNotContain(BriefingPage.Calendar, PagesOf(Full(a => a.ShowCalendar = false)));
        Assert.DoesNotContain(BriefingPage.Commute, PagesOf(Full(a => a.ShowCommute = false)));
        Assert.DoesNotContain(BriefingPage.Bins, PagesOf(Full(a => a.ShowBins = false)));
    }

    [Fact]
    public void Pages_NothingOptional_LeavesGreetingAndSignOff()
    {
        var rig = Make(weather: false, events: null, arrivals: false);
        Assert.Equal([BriefingPage.Greeting, BriefingPage.SignOff], PagesOf(rig));
        Assert.Equal(12, rig.App.TotalSeconds);
    }

    [Fact]
    public void Pages_EmptyCalendarDay_StillSaysNothingOn()
    {
        var rig = Make(events: [Event("Tomorrow only", 10, 0, dayOffset: 1)]);
        Assert.Contains(BriefingPage.Calendar, PagesOf(rig));
        Assert.Equal(0, rig.App.Model.EventCount);
        Assert.Equal("Nothing on today", rig.App.Model.NothingText);
    }

    [Fact]
    public void Calendar_ShowsTheNextThreeOfToday_AndDropsFinishedOnes()
    {
        var events = new List<CalEvent> { Event("Early", 6, 0, 30), Event("A", 9, 0), Event("B", 10, 0), Event("C", 11, 0), Event("D", 12, 0) };
        var rig = Make(events: events);
        Assert.Equal(3, rig.App.Model.EventCount);
        Assert.Equal(new[] { "A", "B", "C" }, rig.App.Model.EventTitles);
        Assert.Equal("09:00", rig.App.Model.EventTimes[0]);
    }

    [Fact]
    public void Calendar_AllDayEventsComeAfterTimedOnes()
    {
        var rig = Make(events: [Event("Holiday", 0, 0, allDay: true), Event("Call", 9, 0)]);
        Assert.Equal(new[] { "Call", "Holiday", "" }, rig.App.Model.EventTitles);
        Assert.Equal("ALL DAY", rig.App.Model.EventTimes[1]);
    }

    [Fact]
    public void Commute_LeaveInUsesTheWalkMinutes()
    {
        // Fixture trains are ~190, 340, 500 s away: with a 1 minute walk the first is 130 s from the door
        var rig = Make(walk: 1);
        Assert.Equal("Leave in 2 min", rig.App.Model.LeaveText);
        Assert.Equal("JUBILEE", rig.App.Model.LineName);          // the worst line serving the station
        Assert.Equal("Severe Delays", rig.App.Model.LineText);

        var late = Make(walk: 3);
        Assert.Equal("Leave now", late.App.Model.LeaveText);
    }

    [Fact]
    public void Bins_DueTodayAndTomorrow()
    {
        var rig = Make(bins: TwoBins);
        Assert.Equal("Bins out today", rig.App.Model.BinHeadline);
        Assert.Equal("Black", rig.App.Model.BinNames);
        Assert.Equal("Tomorrow: Garden", rig.App.Model.BinSecond);
        Assert.Equal(1, rig.App.Model.BinCount);
    }

    // ---- sequencing --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Sequence_AdvancesEveryPageSeconds_AndStopsOnTheSignOff()
    {
        var rig = Full();
        var pager = rig.App.Pager!;
        Assert.Equal(0, pager.PageIndex);

        // Nothing moves before a card's time is up; the second card is settled a little after Page Seconds
        rig.Stage.Step(33, 5 * 30);
        Assert.Equal(0, pager.PageIndex);
        RunUntil(rig.Stage, () => pager.PageIndex == 1 && !pager.IsTransitioning);
        Assert.InRange(rig.Stage.Time.TotalSeconds, 6.0, 7.5);

        RunUntil(rig.Stage, () => pager.PageIndex == 5 && !pager.IsTransitioning);
        Assert.InRange(rig.Stage.Time.TotalSeconds, 30.0, 32.5);

        // It never wraps back to the greeting
        rig.Stage.Step(33, 60 * 30);
        Assert.Equal(5, pager.PageIndex);
        Assert.False(pager.IsTransitioning);
        Assert.Equal(1f, MathF.Min(1f, rig.App.Progress.Fraction));
        Assert.Equal(5, rig.App.Progress.Index);
        Assert.Equal(6, rig.App.Progress.Count);
    }

    [Fact]
    public void Sequence_FillsTheTotalSecondsExactly()
    {
        var rig = Full(a => a.PageSeconds = 4);
        Assert.Equal(24, rig.App.TotalSeconds);
        rig.Stage.Step(33, (int)(rig.App.TotalSeconds * 30 * 1.0) - 6);
        // at the end of the entry the sign-off has had a full share
        Assert.Equal(5, rig.App.Pager!.PageIndex);
        Assert.False(rig.App.Pager.IsTransitioning);
    }

    [Fact]
    public void Sequence_ReplaysFromTheFirstCardWhenActivatedAgain()
    {
        var rig = Make(configure: a => { }, events: null);
        var config = new ConfigurationBuilder().Build();
        RunUntil(rig.Stage, () => rig.App.Pager!.PageIndex == 1 && !rig.App.Pager.IsTransitioning);
        Assert.Equal(1, rig.App.Pager!.PageIndex);

        rig.App.OnDeactivatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        rig.App.OnActivatedAsync((64, 256), config, CancellationToken.None).GetAwaiter().GetResult();
        rig.Stage.Step(33, 3);

        Assert.Equal(0, rig.App.Pager!.PageIndex);
        Assert.Equal("Good morning", rig.App.Model.GreetingText);
    }

    [Fact]
    public void Sources_AreFakedAndPolledOnActivation()
    {
        Fonts.Load();
        var app = new MorningBriefingApp { Time = new FakeTime { Now = Morning }, StationId = "940GZZLUVIC", Bins = TwoBins };
        app.UseSources(new MorningBriefingApp.BriefingSources(
            (f, ct) => new FakeWeatherSource().GetAsync(new WeatherQuery("London", f), ct),
            ct => Task.FromResult(TodayEvents()),
            (id, ct) => Task.FromResult(CommuteBoard()),
            (live, ct) => Task.FromResult(StationLines())));
        app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None).GetAwaiter().GetResult();
        var stage = new AppStage(app);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (app.PageCount < 6 && DateTime.UtcNow < deadline)
        {
            stage.Step(33);
            Thread.Sleep(5);
        }

        Assert.Equal(6, app.PageCount);
        app.OnDeactivatedAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    // ---- goldens -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_Greeting()
    {
        var rig = Full();
        GoTo(rig, 0);
        Golden(rig, "briefing_greeting");
    }

    [Fact]
    public void Golden_Weather()
    {
        var rig = Full();
        GoTo(rig, 1);
        Golden(rig, "briefing_weather");
    }

    [Fact]
    public void Golden_Calendar()
    {
        var rig = Full();
        GoTo(rig, 2);
        Golden(rig, "briefing_calendar");
    }

    [Fact]
    public void Golden_Commute()
    {
        var rig = Full();
        GoTo(rig, 3);
        Golden(rig, "briefing_commute");
    }

    [Fact]
    public void Golden_Bins()
    {
        var rig = Full();
        GoTo(rig, 4);
        Golden(rig, "briefing_bins");
    }

    [Fact]
    public void Golden_SignOff()
    {
        var rig = Full();
        GoTo(rig, 5);
        Golden(rig, "briefing_signoff");
    }

    [Fact]
    public void Golden_MinimalOnlyGreeting()
    {
        var rig = Make(weather: false, events: null, arrivals: false);
        GoTo(rig, 0);
        Golden(rig, "briefing_minimal_only_greeting");
    }

    // ---- allocation --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var rig = Full();
        var stage = rig.Stage;
        GoTo(rig, 1);   // weather card: glyph, sparkline and labels all running

        long least = long.MaxValue;
        double ms = 0;
        int measured = 0;
        for (int window = 0; window < 6; window++)
        {
            // keep inside one card's rest period and away from a minute rollover (the app clock is frozen, so strings never rebuild)
            if (rig.App.Pager!.IsTransitioning) { stage.Step(33, 40); continue; }
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (rig.App.Pager.IsTransitioning || rig.App.Pager.PageIndex != 1) continue;   // a page change allocates by design
            measured++;
            ms = sw.Elapsed.TotalMilliseconds / 60;
            least = Math.Min(least, allocated);
        }

        output.WriteLine($"briefing: {ms:F3} ms/frame, {measured} steady windows, {least} bytes");
        Assert.True(measured >= 1);
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
