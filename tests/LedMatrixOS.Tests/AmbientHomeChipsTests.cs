using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class AmbientHomeChipsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 13, 45, 7, TimeSpan.Zero);

    private static WeatherSnapshot Weather(int code = 61, bool day = true, double temp = 17.4) =>
        new FakeWeatherSource(code, day, temp).GetAsync(new WeatherQuery("London", false), default).GetAwaiter().GetResult();

    private static List<CalEvent> Events() =>
    [
        new("Standup", Start.AddMinutes(-90), Start.AddMinutes(-60), false, null),
        new("Dentist appointment with Dr Patel and the hygienist", Start.AddMinutes(105), Start.AddMinutes(165), false, "Clinic"),
        new("Later", Start.AddHours(8), Start.AddHours(9), false, null),
    ];

    private static LineStatus[] Lines() =>
    [
        new("bakerloo", "Bakerloo", 10, "Good Service", ""),
        new("central", "Central", 9, "Minor Delays", "Signal failure"),
        new("northern", "Northern", 10, "Good Service", ""),
    ];

    private static TflArrival[] Buses() =>
    [
        new() { Id = "1", LineName = "38", TimeToStation = 540 },
        new() { Id = "2", LineName = "73", TimeToStation = 185 },
    ];

    private static AmbientRig Rig(Action<HomePageApp>? configure = null, string mode = "Aurora", bool show24 = true, bool showDate = true,
        int hour = 13, int minute = 45, bool weather = false, bool events = false, bool line = false, bool bus = false, bool data = true)
    {
        var app = new HomePageApp
        {
            DisplayMode = mode, Show24Hour = show24, ShowDate = showDate,
            ShowWeatherChip = weather, ShowEventChip = events, ShowLineChip = line, ShowBusChip = bus, ChipStopId = bus ? "490000001" : "",
        };
        configure?.Invoke(app);
        var rig = new AmbientRig(app, new FakeTime { Now = new DateTimeOffset(2026, 10, 2, hour, minute, 7, TimeSpan.Zero) });
        if (data)
            app.UseData(new FakeLive<WeatherSnapshot> { Value = Weather() }, new FakeLive<List<CalEvent>> { Value = Events() },
                new FakeLive<LineStatus[]> { Value = Lines() }, new FakeLive<TflArrival[]> { Value = Buses() });
        return rig;
    }

    [Fact]
    public void Settings_AreAppended_AfterTheExistingOnes_AndDefaultOff()
    {
        var app = new HomePageApp();
        Assert.Equal(
            ["displayMode", "showDate", "show24Hour", "theme", "ambientSpeed", "showWeatherChip", "showEventChip", "showLineChip", "showBusChip", "chipStopId", "chipSeconds"],
            app.GetSettings().Select(s => s.Key).ToArray());
        Assert.False(app.ShowWeatherChip || app.ShowEventChip || app.ShowLineChip || app.ShowBusChip);
        Assert.Equal(6, app.ChipSeconds);
        app.UpdateSetting("chipSeconds", 99);
        Assert.Equal(30, app.ChipSeconds);
    }

    [Fact]
    public void ChipsOff_ChangesNothing_EvenWithDataAvailable()
    {
        var plain = new AmbientRig(new HomePageApp(), new FakeTime { Now = Start }).Advance(4000).Copy();
        Assert.True(Stage.Same(plain, Rig().Advance(4000).Copy()));
    }

    [Fact]
    public void NothingPolls_WhenAllChipsAreOff_AndOnlyEnabledChipsPollWhenOn()
    {
        int weather = 0, events = 0, lines = 0, bus = 0;
        var sources = new HomePageApp.ChipSources(
            ct => { Interlocked.Increment(ref weather); return Task.FromResult(Weather()); },
            ct => { Interlocked.Increment(ref events); return Task.FromResult(Events()); },
            ct => { Interlocked.Increment(ref lines); return Task.FromResult(Lines()); },
            (stop, ct) => { Interlocked.Increment(ref bus); return Task.FromResult(Buses()); });

        var rig = Rig(data: false);
        ((HomePageApp)rig.App).UseSources(sources);
        rig.Advance(1000);
        Thread.Sleep(150);
        Assert.Equal(0, weather + events + lines + bus);

        ((HomePageApp)rig.App).UpdateSetting("showWeatherChip", true);
        ((HomePageApp)rig.App).UpdateSetting("showBusChip", true);
        rig.Advance(200);
        Wait(() => Volatile.Read(ref weather) > 0);
        Assert.Equal(0, Volatile.Read(ref bus));   // no stop id yet

        ((HomePageApp)rig.App).UpdateSetting("chipStopId", "490000001");
        rig.Advance(200);
        Wait(() => Volatile.Read(ref bus) > 0);
        Assert.Equal(0, events + lines);

        // The weather chip appears once its data has arrived.
        Wait(() => ((HomePageApp)rig.App).WeatherFeed?.Data.Value is not null);
        rig.Advance(2000);
        Assert.False(SnapshotHelper.IsBlank(rig.Copy()));
    }

    [Fact]
    public void FailingSources_HideTheirChips_WithoutCrashing()
    {
        var rig = Rig(weather: true, events: true, line: true, bus: true, data: false);
        ((HomePageApp)rig.App).UseSources(new HomePageApp.ChipSources(
            ct => throw new HttpRequestException("offline"), null, ct => throw new HttpRequestException("offline"),
            (s, ct) => throw new HttpRequestException("offline")));
        var withFailures = rig.Advance(3000).Copy();
        var plain = new AmbientRig(new HomePageApp(), new FakeTime { Now = Start }).Advance(3000).Copy();
        Assert.True(Stage.Same(withFailures, plain));
    }

    [Fact]
    public void UnavailableData_KeepsTheClockWhereItWas()
    {
        // Enabled, but no calendar feed configured, no disruption and no stop: nothing to show, so no layout change.
        var app = new HomePageApp { ShowEventChip = true, ShowLineChip = true, ShowBusChip = true };
        var rig = new AmbientRig(app, new FakeTime { Now = Start });
        app.UseData(null, null, new FakeLive<LineStatus[]> { Value = [new("bakerloo", "Bakerloo", 10, "Good Service", "")] }, null);
        Assert.True(Stage.Same(rig.Advance(4000).Copy(), new AmbientRig(new HomePageApp(), new FakeTime { Now = Start }).Advance(4000).Copy()));
    }

    [Fact]
    public void Chips_CycleAfterTheirInterval_AndAppearWithData()
    {
        var rig = Rig(a => a.ChipSeconds = 3, weather: true, events: true, line: true, bus: true);
        var first = rig.Advance(2500).Copy();
        var second = rig.Advance(3000).Copy();
        var third = rig.Advance(3000).Copy();
        Assert.False(Stage.Same(first, second));
        Assert.False(Stage.Same(second, third));
    }

    [Theory]
    [InlineData(true, false, false, false, "ambient_home_chip_weather")]
    [InlineData(false, true, false, false, "ambient_home_chip_event")]
    [InlineData(false, false, true, false, "ambient_home_chip_line")]
    [InlineData(false, false, false, true, "ambient_home_chip_bus")]
    public void Snapshot_EachChip(bool weather, bool events, bool line, bool bus, string name)
    {
        var rig = Rig(weather: weather, events: events, line: line, bus: bus);
        SnapshotHelper.AssertMatchesSnapshot(rig.Advance(4000).Copy(), name);
    }

    [Fact]
    public void Snapshot_MinimalMode_TwelveHourWithDate()
    {
        var rig = Rig(mode: "Minimal", show24: false, hour: 9, minute: 5, weather: true);
        SnapshotHelper.AssertMatchesSnapshot(rig.Advance(4000).Copy(), "ambient_home_chips_minimal_mode");
    }

    [Fact]
    public void Snapshot_ChipCentredWhenTheDateIsHidden()
    {
        var rig = Rig(mode: "Starfield", showDate: false, line: true);
        SnapshotHelper.AssertMatchesSnapshot(rig.Advance(4000).Copy(), "ambient_home_chip_line_nodate");
    }

    [Fact]
    public void SteadyState_AllocatesNothingPerFrame_WithEveryChipOn()
    {
        Rig(a => a.ChipSeconds = 3, weather: true, events: true, line: true, bus: true).AssertNoAllocationsPerFrame();
    }

    [Fact]
    public void SteadyState_AllocatesNothingPerFrame_TwelveHourNoDate()
    {
        Rig(a => a.ChipSeconds = 3, show24: false, showDate: false, weather: true, events: true, line: true, bus: true).AssertNoAllocationsPerFrame();
    }

    private static void Wait(Func<bool> cond)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!cond())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException();
            Thread.Sleep(5);
        }
    }
}
