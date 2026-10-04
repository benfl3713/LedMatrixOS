using LedMatrixOS.Core.Scheduling;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class AttentionConditionTests
{
    private sealed class FakeSource(string kind, Func<string?, bool> answer) : IAttentionSource
    {
        public string Kind => kind;
        public bool IsActive(string? argument) => answer(argument);
    }

    [Theory]
    [InlineData("spotify_playing", "spotify_playing", null)]
    [InlineData("bin_day", "bin_day", null)]
    [InlineData("line_disrupted:victoria", "line_disrupted", "victoria")]
    [InlineData("bus_due:490000077E", "bus_due", "490000077E")]
    [InlineData("ha_state:light.lamp=on", "ha_state", "light.lamp=on")]
    [InlineData(" Spotify_Playing ", "spotify_playing", null)]
    public void Parse_AcceptsKnownForms(string text, string kind, string? arg)
    {
        Assert.True(AttentionCondition.TryParse(text, out var c, out _));
        Assert.Equal(kind, c.Kind);
        Assert.Equal(arg, c.Argument);
    }

    [Theory]
    [InlineData("")]
    [InlineData("event_starting")]
    [InlineData("spotify_playing:x")]
    [InlineData("bin_day:x")]
    [InlineData("line_disrupted")]
    [InlineData("bus_due:")]
    [InlineData("ha_state:light.lamp")]
    [InlineData("ha_state:=on")]
    [InlineData("ha_state:a=")]
    public void Parse_RejectsBadForms(string text)
    {
        Assert.False(AttentionCondition.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Evaluator_PassesArgumentToMatchingSource()
    {
        string? seen = null;
        var ev = new AttentionEvaluator(new[]
        {
            new FakeSource("line_disrupted", a => { seen = a; return a == "victoria"; }),
        });

        Assert.True(ev.Evaluate("line_disrupted:victoria"));
        Assert.Equal("victoria", seen);
        Assert.False(ev.Evaluate("line_disrupted:central"));
    }

    [Fact]
    public void Evaluator_MissingSourceThrowingSourceOrBadConditionAreNotMet_BlankIsMet()
    {
        var ev = new AttentionEvaluator(new[] { new FakeSource("spotify_playing", _ => throw new InvalidOperationException()) });
        Assert.True(ev.Evaluate(null));
        Assert.True(ev.Evaluate("  "));
        Assert.False(ev.Evaluate("bus_due:123"));
        Assert.False(ev.Evaluate("spotify_playing"));
        Assert.False(ev.Evaluate("nonsense"));
    }

    private static ScheduleService BuildSchedule(TestClock clock, AttentionEvaluator ev)
    {
        var sched = new ScheduleService(clock, ev);
        sched.Replace(ScheduleDocument.TryParse("""
        { "playlists": [ { "name": "base", "entries": [ { "appId": "clock", "durationMs": 60000 } ] },
                         { "name": "music", "entries": [ { "appId": "spotify", "durationMs": 60000 } ] } ],
          "rules": [ { "playlistId": "base", "priority": 10 },
                     { "playlistId": "music", "priority": 80, "condition": "spotify_playing", "brightnessOverride": 200 } ] }
        """, out _)!);
        return sched;
    }

    [Fact]
    public void Rule_WithCondition_OnlyWinsWhileSourceIsActive()
    {
        bool playing = false;
        var clock = new TestClock(new DateTimeOffset(2025, 1, 20, 12, 0, 0, TimeSpan.Zero));
        var sched = BuildSchedule(clock, new AttentionEvaluator(new[] { new FakeSource("spotify_playing", _ => playing) }));

        Assert.Equal("clock", sched.GetActiveAppId());
        Assert.Null(sched.GetActiveBrightnessOverride());

        playing = true;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("spotify", sched.GetActiveAppId());
        Assert.Equal((byte)200, sched.GetActiveBrightnessOverride());
        Assert.Equal("spotify_playing", sched.GetStatus().ActiveRule!.Condition);

        playing = false;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("clock", sched.GetActiveAppId());
    }

    [Fact]
    public void Rule_WithConditionAndNoSource_NeverMatches()
    {
        var clock = new TestClock(new DateTimeOffset(2025, 1, 20, 12, 0, 0, TimeSpan.Zero));
        var sched = BuildSchedule(clock, new AttentionEvaluator());
        Assert.Equal("clock", sched.GetActiveAppId());
    }

    [Fact]
    public void Rule_ConditionCombinesWithTimeWindow()
    {
        var clock = new TestClock(new DateTimeOffset(2025, 1, 20, 6, 0, 0, TimeSpan.Zero));
        var ev = new AttentionEvaluator(new[] { new FakeSource("ha_state", a => a == "light.lamp=on") });
        var sched = new ScheduleService(clock, ev);
        sched.Replace(ScheduleDocument.TryParse("""
        { "playlists": [ { "name": "lamp", "entries": [ { "appId": "clock", "durationMs": 1000 } ] } ],
          "rules": [ { "playlistId": "lamp", "startTime": "07:00", "endTime": "09:00", "condition": "ha_state:light.lamp=on" } ] }
        """, out _)!);

        Assert.Null(sched.GetActiveAppId());          // condition true, outside window
        clock.Advance(TimeSpan.FromHours(1.5));
        Assert.Equal("clock", sched.GetActiveAppId()); // inside window
    }
}
