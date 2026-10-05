using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

public class BinDayAppTests(ITestOutputHelper output)
{
    private const string ThreeBins = "Black|#3a3a3a|Mon|2|2026-10-05; Recycling|#1e90ff|Mon|2|2026-10-12; Garden|#2ea043|Wed|1|2026-10-07";

    private static (BinDayApp App, AppStage Stage) Rig(DateTime local, string bins = ThreeBins, string reminders = "", string keyword = "", List<CalEvent>? events = null, int warmFrames = 30)
    {
        Fonts.Load();
        var app = new BinDayApp(new HttpClient())
        {
            Time = new FakeTime { Now = new DateTimeOffset(local, TimeSpan.Zero) },
            Bins = bins,
            Reminders = reminders,
            CalendarKeyword = keyword,
        };
        if (events != null) app.UseCalendar(new MutableLive<List<CalEvent>> { Value = events });
        var stage = new AppStage(app);
        stage.Step(33, warmFrames);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        TubeFixtures.Preview(frame, name);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new BinDayApp();
        Assert.Equal("bin-day", app.Id);
        Assert.Equal("Bin Day", app.Name);
        Assert.Equal(["bins", "calendarKeyword", "reminders", "eveningHour", "pageSeconds"], app.GetSettings().Select(s => s.Key));
    }

    [Fact]
    public void Tomorrow_ShowsPutOutHeadline_AndTonightFromTheEveningHour()
    {
        var (app, _) = Rig(new DateTime(2026, 10, 4, 10, 0, 0));
        Assert.Equal("Alert", app.CurrentView);
        Assert.Equal("TOMORROW", app.PlateText);
        Assert.Equal("PUT OUT THE BLACK BIN", app.HeadlineText);

        var (evening, _) = Rig(new DateTime(2026, 10, 4, 18, 0, 0));
        Assert.Equal("TONIGHT", evening.PlateText);

        var (today, _) = Rig(new DateTime(2026, 10, 5, 7, 30, 0));
        Assert.Equal("TODAY", today.PlateText);
    }

    [Fact]
    public void Headline_ListsEveryBin()
    {
        var (app, _) = Rig(new DateTime(2026, 10, 5, 7, 30, 0), ThreeBins + "; Food|#a0522d|Mon|1|2026-10-05");
        Assert.Equal("PUT OUT THE BLACK AND FOOD BINS", app.HeadlineText);
    }

    [Fact]
    public void AfterMiddayOnCollectionDay_TomorrowsBinsTakeOver()
    {
        var (app, _) = Rig(new DateTime(2026, 10, 6, 13, 0, 0), "Garden|#2ea043|Tue|1|; Black|#333|Wed|1|");
        Assert.Equal("TOMORROW", app.PlateText);
        Assert.Contains("BLACK", app.HeadlineText);
    }

    [Fact]
    public void Summary_PagesTwoRowsAtATime()
    {
        var (app, _) = Rig(new DateTime(2026, 10, 3, 10, 0, 0));
        Assert.Equal("Summary", app.CurrentView);
        Assert.Equal(2, app.Pages.Length);
    }

    [Fact]
    public void ActiveReminder_TakesOver_AndEndsWithItsWindow()
    {
        var (app, stage) = Rig(new DateTime(2026, 10, 5, 9, 0, 0), reminders: "Take pills|08:00|20:00|Mon");
        Assert.Equal("Reminder", app.CurrentView);

        var (later, _) = Rig(new DateTime(2026, 10, 5, 20, 0, 0), reminders: "Take pills|08:00|20:00|Mon");
        Assert.NotEqual("Reminder", later.CurrentView);
    }

    [Fact]
    public void CalendarKeyword_AddsOneOffCollections()
    {
        var day = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        var events = new List<CalEvent> { new("Bulky waste collection", day, day.AddDays(1), true, null), new("Dentist", day, day.AddDays(1), true, null) };

        var (on, _) = Rig(new DateTime(2026, 10, 5, 19, 0, 0), bins: "", keyword: "collection", events: events);
        Assert.Equal("Alert", on.CurrentView);
        Assert.Equal("TONIGHT", on.PlateText);

        var (off, _) = Rig(new DateTime(2026, 10, 5, 19, 0, 0), bins: "", keyword: "", events: events);
        Assert.Equal("Message", off.CurrentView);
    }

    [Fact]
    public void InvalidEntries_AreIgnored_AndExplainedWhenNothingIsLeft()
    {
        var (mixed, _) = Rig(new DateTime(2026, 10, 3, 10, 0, 0), "Nonsense; " + ThreeBins);
        Assert.Equal("Summary", mixed.CurrentView);

        var (bad, _) = Rig(new DateTime(2026, 10, 3, 10, 0, 0), "Nonsense|red|Mon");
        Assert.Equal("Message", bad.CurrentView);
    }

    [Fact]
    public void Goldens()
    {
        Golden(Rig(new DateTime(2026, 10, 4, 10, 0, 0)).Stage, "bin_day_tomorrow");
        Golden(Rig(new DateTime(2026, 10, 5, 7, 30, 0), ThreeBins + "; Garden|#2ea043|Mon|1|2026-10-05").Stage, "bin_day_today_two_bins");
        Golden(Rig(new DateTime(2026, 10, 3, 10, 0, 0)).Stage, "bin_day_next_summary");
        Golden(Rig(new DateTime(2026, 10, 5, 9, 0, 0), reminders: "Take pills|08:00|20:00|Mon,Tue,Wed,Thu,Fri,Sat,Sun").Stage, "bin_day_reminder");
        Golden(Rig(new DateTime(2026, 10, 3, 10, 0, 0), bins: "").Stage, "bin_day_not_configured");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var failures = new List<string>();
        foreach (var local in new[] { new DateTime(2026, 10, 3, 10, 0, 0), new DateTime(2026, 10, 4, 18, 0, 0), new DateTime(2026, 10, 5, 9, 0, 0) })
        {
            var (app, stage) = Rig(local, reminders: local.Day == 5 ? "Take pills|08:00|20:00" : "", warmFrames: 300);
            var run = stage.MeasureSteadyAllocation(windows: 12, warmFrames: 300, beginWindow: () =>
            {
                int page = app.Pager!.PageIndex;
                return () => page == app.Pager.PageIndex && !app.Pager.IsTransitioning;
            });

            output.WriteLine($"bin day {local:d MMM HH:mm}: {run.MsPerFrame:F3} ms/frame, least window {run.Least} bytes");
            if (run.Least >= 256) failures.Add($"{local}: {run.Least} bytes");
        }
        Assert.Empty(failures);
    }
}
