using LedMatrixOS.Core.Scheduling;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class ScheduleServiceTests
{
    private static readonly TimeSpan Morning = TimeSpan.FromHours(7);
    private static readonly TimeSpan Evening = TimeSpan.FromHours(23);

    [Fact]
    public void NoRules_ReturnsNull()
    {
        var sched = new ScheduleService();
        Assert.Null(sched.GetActiveAppId());
    }

    [Fact]
    public void WithPlaylist_RotatesApps()
    {
        var timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 20, 8, 0, 0, DateTimeKind.Utc)); // Monday 8am
        var sched = new ScheduleService(timeProvider);

        var playlist = new PlaylistConfig
        {
            Name = "morning",
            Entries = new()
            {
                new PlaylistEntry("clock", TimeSpan.FromSeconds(10)),
                new PlaylistEntry("weather", TimeSpan.FromSeconds(10)),
            }
        };
        sched.RegisterPlaylist(playlist);

        var rule = new ScheduleRule { PlaylistId = "morning", Priority = 50 };
        sched.AddRule(rule);

        // At t=0, should be on clock
        Assert.Equal("clock", sched.GetActiveAppId());

        // At t=5, still clock
        timeProvider.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal("clock", sched.GetActiveAppId());

        // At t=11, rotated to weather
        timeProvider.AdvanceBy(TimeSpan.FromSeconds(6));
        Assert.Equal("weather", sched.GetActiveAppId());
    }

    [Fact]
    public void TimeBoundedRule_OnlyActiveInRange()
    {
        var timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 20, 6, 0, 0, DateTimeKind.Utc)); // Monday 6am
        var sched = new ScheduleService(timeProvider);

        var playlist = new PlaylistConfig { Name = "morning", Entries = new() { new PlaylistEntry("clock", TimeSpan.FromMinutes(30)) } };
        sched.RegisterPlaylist(playlist);

        var rule = new ScheduleRule
        {
            PlaylistId = "morning",
            StartTime = TimeSpan.FromHours(7),
            EndTime = TimeSpan.FromHours(9),
        };
        sched.AddRule(rule);

        // At 6am, rule doesn't match
        Assert.Null(sched.GetActiveAppId());

        // At 8am, rule matches
        timeProvider.AdvanceBy(TimeSpan.FromHours(2));
        Assert.Equal("clock", sched.GetActiveAppId());

        // At 10am, rule doesn't match anymore
        timeProvider.AdvanceBy(TimeSpan.FromHours(2));
        Assert.Null(sched.GetActiveAppId());
    }

    [Fact]
    public void PlaylistChange_RestartsRotation()
    {
        var timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 20, 8, 0, 0, DateTimeKind.Utc)); // Monday 8am
        var sched = new ScheduleService(timeProvider);

        var morning = new PlaylistConfig
        {
            Name = "morning",
            Entries = new() { new PlaylistEntry("clock", TimeSpan.FromSeconds(60)) }
        };
        var afternoon = new PlaylistConfig
        {
            Name = "afternoon",
            Entries = new() { new PlaylistEntry("weather", TimeSpan.FromSeconds(60)) }
        };
        sched.RegisterPlaylist(morning);
        sched.RegisterPlaylist(afternoon);

        var morningRule = new ScheduleRule { PlaylistId = "morning", StartTime = TimeSpan.FromHours(7), EndTime = TimeSpan.FromHours(12) };
        var afternoonRule = new ScheduleRule { PlaylistId = "afternoon", StartTime = TimeSpan.FromHours(12), EndTime = TimeSpan.FromHours(18), Priority = 60 };
        sched.AddRule(morningRule);
        sched.AddRule(afternoonRule);

        // Morning: on clock
        Assert.Equal("clock", sched.GetActiveAppId());

        // Advance to afternoon (12:01)
        timeProvider.AdvanceBy(TimeSpan.FromHours(4).Add(TimeSpan.FromMinutes(1)));
        Assert.Equal("weather", sched.GetActiveAppId()); // Restarted, should be on weather (entry 0)
    }

    [Fact]
    public void BrightnessOverride_ReturnsFromActiveRule()
    {
        var timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 20, 23, 0, 0, DateTimeKind.Utc)); // Monday 11pm
        var sched = new ScheduleService(timeProvider);

        var playlist = new PlaylistConfig { Name = "night", Entries = new() { new PlaylistEntry("clock", TimeSpan.FromMinutes(60)) } };
        sched.RegisterPlaylist(playlist);

        var nightRule = new ScheduleRule
        {
            PlaylistId = "night",
            StartTime = TimeSpan.FromHours(23),
            EndTime = TimeSpan.FromHours(7),
            BrightnessOverride = 50
        };
        sched.AddRule(nightRule);

        Assert.Equal("clock", sched.GetActiveAppId());
        Assert.Equal((byte)50, sched.GetActiveBrightnessOverride());

        // At 8am, no rule matches
        timeProvider.AdvanceBy(TimeSpan.FromHours(9));
        Assert.Null(sched.GetActiveBrightnessOverride());
    }

    [Fact]
    public void RuleWithDayMask_OnlyMatchesSelectedDays()
    {
        var timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 20, 8, 0, 0, DateTimeKind.Utc)); // Monday
        var sched = new ScheduleService(timeProvider);

        var playlist = new PlaylistConfig { Name = "commute", Entries = new() { new PlaylistEntry("clock", TimeSpan.FromMinutes(30)) } };
        sched.RegisterPlaylist(playlist);

        // Weekdays only (Mon=2, Tue=4, Wed=8, Thu=16, Fri=32)
        var weekdayMask = 2 | 4 | 8 | 16 | 32;
        var rule = new ScheduleRule
        {
            PlaylistId = "commute",
            ActiveDaysMask = weekdayMask,
            StartTime = TimeSpan.FromHours(7),
            EndTime = TimeSpan.FromHours(9),
        };
        sched.AddRule(rule);

        // Monday 8am: matches
        Assert.Equal("clock", sched.GetActiveAppId());

        // Saturday 8am: doesn't match
        timeProvider.AdvanceBy(TimeSpan.FromDays(5)); // Saturday
        Assert.Null(sched.GetActiveAppId());
    }

    [Fact]
    public void MidnightWrap_HandlesNextDay()
    {
        var timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 20, 23, 30, 0, DateTimeKind.Utc)); // Monday 11:30pm
        var sched = new ScheduleService(timeProvider);

        var playlist = new PlaylistConfig { Name = "night", Entries = new() { new PlaylistEntry("clock", TimeSpan.FromMinutes(60)) } };
        sched.RegisterPlaylist(playlist);

        // 23:00 to 07:00 (next day)
        var rule = new ScheduleRule
        {
            PlaylistId = "night",
            StartTime = TimeSpan.FromHours(23),
            EndTime = TimeSpan.FromHours(7),
        };
        sched.AddRule(rule);

        // At 11:30pm: matches
        Assert.Equal("clock", sched.GetActiveAppId());

        // At 1:00am (next day): still matches
        timeProvider.AdvanceBy(TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(30)));
        Assert.Equal("clock", sched.GetActiveAppId());

        // At 8:00am: stops matching
        timeProvider.AdvanceBy(TimeSpan.FromHours(7));
        Assert.Null(sched.GetActiveAppId());
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTime initialTime)
        {
            _now = new DateTimeOffset(initialTime);
        }

        public void AdvanceBy(TimeSpan delta) => _now = _now.Add(delta);

        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
