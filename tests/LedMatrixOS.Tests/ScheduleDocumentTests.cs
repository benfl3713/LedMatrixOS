using LedMatrixOS.Core.Scheduling;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class ScheduleDocumentTests
{
    private const string Valid = """
    {
      "playlists": [ { "name": "day", "entries": [
          { "appId": "clock", "durationMs": 10000, "settings": { "Format": "24h", "Seconds": true }, "transition": "fade" },
          { "appId": "weather", "durationMs": 5000 } ] } ],
      "rules": [ { "playlistId": "day", "priority": 60, "startTime": "07:00", "endTime": "23:00", "daysMask": 62, "brightnessOverride": 120 } ]
    }
    """;

    [Fact]
    public void Valid_Document_HasNoErrors()
    {
        var doc = ScheduleDocument.TryParse(Valid, out var err)!;
        Assert.Null(err);
        Assert.Empty(doc.Validate(id => id is "clock" or "weather", t => t == "fade"));
    }

    [Fact]
    public void Malformed_Json_ReturnsError()
    {
        Assert.Null(ScheduleDocument.TryParse("{ nope", out var err));
        Assert.Contains("Invalid JSON", err);
    }

    [Fact]
    public void Invalid_Document_ReportsAllProblems()
    {
        var doc = ScheduleDocument.TryParse("""
        { "playlists": [ { "name": "a", "entries": [ { "appId": "ghost", "durationMs": 0, "transition": "warp" } ] },
                         { "name": "a", "entries": [] } ],
          "rules": [ { "playlistId": "missing", "startTime": "25:00", "daysMask": 0, "brightnessOverride": 300, "condition": "bogus" } ] }
        """, out _)!;
        var errors = doc.Validate(id => id == "clock", t => t == "fade");
        Assert.Contains(errors, e => e.Contains("'ghost' is not a registered app"));
        Assert.Contains(errors, e => e.Contains("durationMs"));
        Assert.Contains(errors, e => e.Contains("'warp'"));
        Assert.Contains(errors, e => e.Contains("duplicated"));
        Assert.Contains(errors, e => e.Contains("at least one entry"));
        Assert.Contains(errors, e => e.Contains("'missing'"));
        Assert.Contains(errors, e => e.Contains("startTime"));
        Assert.Contains(errors, e => e.Contains("daysMask"));
        Assert.Contains(errors, e => e.Contains("brightnessOverride"));
        Assert.Contains(errors, e => e.Contains("condition"));
    }

    [Fact]
    public void Replace_And_Export_RoundTrip()
    {
        var doc = ScheduleDocument.TryParse(Valid, out _)!;
        var sched = new ScheduleService();
        sched.Replace(doc);
        var exported = sched.Export();
        Assert.Single(exported.Rules);
        Assert.Equal("07:00", exported.Rules[0].StartTime);
        Assert.Equal(62, exported.Rules[0].DaysMask);
        Assert.Equal(120, exported.Rules[0].BrightnessOverride);
        Assert.Equal(2, exported.Playlists[0].Entries.Count);
        Assert.Equal("24h", exported.Playlists[0].Entries[0].Settings!["Format"].GetString());
    }

    [Fact]
    public void WriteAtomic_ReplacesFile_AndLeavesNoTempFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ledmatrix-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "schedule.json");
        try
        {
            ScheduleDocument.WriteAtomic(path, "one");
            ScheduleDocument.WriteAtomic(path, "two");
            Assert.Equal("two", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));

            var sched = new ScheduleService();
            ScheduleDocument.WriteAtomic(path, ScheduleDocument.TryParse(Valid, out _)!.ToJson());
            Assert.True(sched.TryLoadFromJson(path));
            Assert.Single(sched.Export().Playlists);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Status_ReportsRulePlaylistPositionAndNextChange()
    {
        var clock = new TestClock(new DateTimeOffset(2025, 1, 20, 8, 0, 0, TimeSpan.Zero)); // Monday
        var sched = new ScheduleService(clock);
        sched.Replace(ScheduleDocument.TryParse(Valid, out _)!);

        var s = sched.GetStatus();
        Assert.Equal("day", s.ActiveRule!.PlaylistId);
        Assert.Equal("day", s.Playlist);
        Assert.Equal(0, s.EntryIndex);
        Assert.Equal(2, s.EntryCount);
        Assert.Equal("clock", s.AppId);
        Assert.Equal(clock.GetUtcNow().AddSeconds(10), s.NextChange);
        Assert.Equal("playlist", s.NextChangeReason);

        clock.Advance(TimeSpan.FromSeconds(11));
        s = sched.GetStatus();
        Assert.Equal(1, s.EntryIndex);
        Assert.Equal("weather", s.AppId);
    }

    [Fact]
    public void Status_NextChange_IsRuleEnd_WhenSingleEntry()
    {
        var clock = new TestClock(new DateTimeOffset(2025, 1, 20, 22, 30, 0, TimeSpan.Zero));
        var sched = new ScheduleService(clock);
        sched.Replace(ScheduleDocument.TryParse("""
        { "playlists": [ { "name": "p", "entries": [ { "appId": "clock", "durationMs": 1000 } ] } ],
          "rules": [ { "playlistId": "p", "startTime": "07:00", "endTime": "23:00" } ] }
        """, out _)!);
        var s = sched.GetStatus();
        Assert.Equal(new DateTimeOffset(2025, 1, 20, 23, 0, 0, TimeSpan.Zero), s.NextChange);
        Assert.Equal("rule", s.NextChangeReason);
    }

    [Fact]
    public void Status_NextChange_IsExactlyOnTheBoundary_EvenWhenTheClockMovesBetweenReads()
    {
        var clock = new TickingClock(new DateTimeOffset(2025, 1, 20, 6, 30, 15, TimeSpan.Zero));
        var sched = new ScheduleService(clock);
        sched.Replace(ScheduleDocument.TryParse("""
        { "playlists": [ { "name": "p", "entries": [ { "appId": "clock", "durationMs": 1000 } ] } ],
          "rules": [ { "playlistId": "p", "startTime": "07:00", "endTime": "23:00" } ] }
        """, out _)!);

        var s = sched.GetStatus();

        Assert.Equal(new DateTimeOffset(2025, 1, 20, 7, 0, 0, TimeSpan.Zero), s.NextChange);
        Assert.Equal("rule", s.NextChangeReason);
    }

    [Fact]
    public void Status_NoRules_IsEmpty()
    {
        var s = new ScheduleService().GetStatus();
        Assert.Null(s.ActiveRule);
        Assert.Null(s.Playlist);
        Assert.Null(s.NextChange);
    }
}

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now;
    public TestClock(DateTimeOffset start) => _now = start;
    public void Advance(TimeSpan d) => _now += d;
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>A clock that moves forward one tick every time it is read, like a real one.</summary>
internal sealed class TickingClock : TimeProvider
{
    private DateTimeOffset _now;
    public TickingClock(DateTimeOffset start) => _now = start;
    public override DateTimeOffset GetUtcNow() => _now = _now.AddTicks(1);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
