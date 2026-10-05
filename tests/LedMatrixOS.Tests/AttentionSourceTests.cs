using Microsoft.Extensions.Configuration;
using System.Net;
using LedMatrixOS.Apps.Attention;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Core.Scheduling;
using Xunit;

namespace LedMatrixOS.Tests;

public class AttentionSourceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> Respond { get; set; } = _ => (HttpStatusCode.OK, "[]");
        public List<string> Urls { get; } = new();
        public List<string?> AuthHeaders { get; } = new();

        public int Count { get { lock (Urls) return Urls.Count; } }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Urls) { Urls.Add(request.RequestUri!.ToString()); AuthHeaders.Add(request.Headers.Authorization?.ToString()); }
            var (status, body) = Respond(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("condition not reached in time");
            await Task.Delay(10);
        }
    }

    private static string LineJson(int severity, string description) =>
        $$"""[{"id":"victoria","name":"Victoria","lineStatuses":[{"statusSeverity":{{severity}},"statusSeverityDescription":"{{description}}"}]}]""";

    private static string ArrivalsJson(params int[] seconds) =>
        "[" + string.Join(",", seconds.Select((s, i) => $$"""{"id":"{{i}}","lineName":"73","timeToStation":{{s}}}""")) + "]";

    // ---- line_disrupted -------------------------------------------------------------------------------------------

    [Fact]
    public async Task LineDisruption_DoesNotPollUntilReferenced_ThenReflectsTheStatus_AndStopsWhenDropped()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, LineJson(6, "Severe Delays")) };
        var clock = new FakeTime();
        using var source = new LineDisruptionSource(new HttpClient(handler), "key123", clock);

        Assert.False(source.IsPolling);
        Assert.False(source.IsActive("victoria"));
        await Task.Delay(50);
        Assert.Equal(0, handler.Count);

        source.SetReferenced(["Victoria"]);
        Assert.True(source.IsPolling);
        await WaitFor(() => source.IsActive("victoria"));
        Assert.Contains("/Line/victoria/Status", handler.Urls[0]);
        Assert.Contains("app_key=key123", handler.Urls[0]);

        handler.Respond = _ => (HttpStatusCode.OK, LineJson(10, "Good Service"));
        await source.RefreshAsync(["victoria"], CancellationToken.None);
        Assert.False(source.IsActive("victoria"));

        source.SetReferenced([]);
        Assert.False(source.IsPolling);
        Assert.False(source.IsActive("victoria"));
    }

    [Fact]
    public async Task LineDisruption_PlannedWorksDoNotCount_ErrorsAndStaleDataAreFalse()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, LineJson(5, "Part Closure")) };
        var clock = new FakeTime();
        using var source = new LineDisruptionSource(new HttpClient(handler), (string?)null, clock);

        await source.RefreshAsync(["victoria"], CancellationToken.None);
        Assert.False(source.IsActive("victoria"));

        handler.Respond = _ => (HttpStatusCode.OK, LineJson(9, "Minor Delays"));
        await source.RefreshAsync(["victoria"], CancellationToken.None);
        Assert.True(source.IsActive("victoria"));

        clock.Now = clock.Now.AddMinutes(16);      // older than three polls: unknown, so false
        Assert.False(source.IsActive("victoria"));

        await source.RefreshAsync(["victoria"], CancellationToken.None);
        Assert.True(source.IsActive("victoria"));
        handler.Respond = _ => (HttpStatusCode.InternalServerError, "boom");
        await source.RefreshAsync(["victoria"], CancellationToken.None);
        Assert.False(source.IsActive("victoria"));

        handler.Respond = _ => (HttpStatusCode.OK, "not json");
        await source.RefreshAsync(["victoria"], CancellationToken.None);
        Assert.False(source.IsActive("victoria"));
    }

    // ---- bus_due --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task BusDue_IsTrueOnlyWhenABusIsWithinTheWindow()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, ArrivalsJson(400, 170)) };
        using var source = new BusDueSource(new HttpClient(handler), null, 3, new FakeTime());

        await source.RefreshAsync(["490000001A"], CancellationToken.None);
        Assert.True(source.IsActive("490000001A"));
        Assert.Contains("/StopPoint/490000001A/Arrivals", handler.Urls[0]);

        handler.Respond = _ => (HttpStatusCode.OK, ArrivalsJson(400, 200));
        await source.RefreshAsync(["490000001A"], CancellationToken.None);
        Assert.False(source.IsActive("490000001A"));

        handler.Respond = _ => (HttpStatusCode.OK, "[]");
        await source.RefreshAsync(["490000001A"], CancellationToken.None);
        Assert.False(source.IsActive("490000001A"));
        Assert.Equal(3, BusDueSource.DefaultDueMinutes);
    }

    [Fact]
    public async Task BusDue_LazyStartStop_AndOfflineIsFalse()
    {
        var handler = new FakeHandler { Respond = _ => throw new HttpRequestException("offline") };
        using var source = new BusDueSource(new HttpClient(handler), null, 5, new FakeTime());

        source.SetReferenced(["stopA", "stopB"]);
        Assert.True(source.IsPolling);
        Assert.Equal(2, source.PolledArguments.Count);
        await WaitFor(() => source.Refreshes >= 1);
        Assert.False(source.IsActive("stopA"));

        source.SetReferenced(["stopA"]);          // a changed set restarts the poll for just that stop
        Assert.Equal(new[] { "stopA" }, source.PolledArguments);

        source.SetReferenced([]);
        Assert.False(source.IsPolling);
    }

    // ---- ha_state -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task HomeAssistant_MatchesTheStateCaseInsensitively_AndSendsTheToken()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, """{"entity_id":"light.lamp","state":"on","attributes":{}}""") };
        using var source = new HomeAssistantStateSource(new HttpClient(handler), "http://ha.local:8123", "secret-token", new FakeTime());

        await source.RefreshAsync(["light.lamp=ON"], CancellationToken.None);
        Assert.True(source.IsActive("light.lamp=ON"));
        Assert.Equal("http://ha.local:8123/api/states/light.lamp", handler.Urls[0]);
        Assert.Equal("Bearer secret-token", handler.AuthHeaders[0]);

        await source.RefreshAsync(["light.lamp=off"], CancellationToken.None);
        Assert.False(source.IsActive("light.lamp=off"));

        handler.Respond = _ => (HttpStatusCode.NotFound, "{}");
        await source.RefreshAsync(["light.lamp=on"], CancellationToken.None);
        Assert.False(source.IsActive("light.lamp=on"));
    }

    [Theory]
    [InlineData("", "token")]
    [InlineData("http://ha.local", "")]
    [InlineData(null, null)]
    public async Task HomeAssistant_NeverPollsWithoutConfiguration(string? url, string? token)
    {
        var handler = new FakeHandler();
        using var source = new HomeAssistantStateSource(new HttpClient(handler), url, token, new FakeTime());

        source.SetReferenced(["light.lamp=on"]);
        await Task.Delay(50);
        Assert.False(source.IsPolling);
        Assert.Equal(0, handler.Count);
        Assert.False(source.IsActive("light.lamp=on"));
    }

    // ---- spotify_playing ------------------------------------------------------------------------------------------

    [Fact]
    public void Spotify_NeedsLoadedPlayingAndFreshData()
    {
        var clock = new FakeTime();
        var state = (Loaded: true, Playing: true, UpdatedAt: (DateTimeOffset?)clock.Now);
        using var source = new SpotifyPlayingSource(() => state, null, clock);

        Assert.True(source.IsActive(null));

        clock.Now = clock.Now.AddSeconds(31);     // the feed stopped updating
        Assert.False(source.IsActive(null));

        clock.Now = state.UpdatedAt!.Value;
        state.Playing = false;
        Assert.False(source.IsActive(null));
        state = (false, true, clock.Now);
        Assert.False(source.IsActive(null));
        state = (true, true, null);
        Assert.False(source.IsActive(null));
    }

    [Fact]
    public async Task Spotify_RunsItsOwnServiceOnlyWhileReferenced_AndOnlyWhenConfigured()
    {
        int started = 0, cancelled = 0;
        Task Run(CancellationToken ct)
        {
            Interlocked.Increment(ref started);
            ct.Register(() => Interlocked.Increment(ref cancelled));
            return Task.Delay(Timeout.Infinite, ct);
        }

        using var source = new SpotifyPlayingSource(() => (false, false, null), Run, new FakeTime());
        Assert.False(source.IsRunning);
        await Task.Delay(30);
        Assert.Equal(0, started);

        source.SetReferenced([null]);
        Assert.True(source.IsRunning);
        source.SetReferenced([null, null]);       // still one run
        await WaitFor(() => Volatile.Read(ref started) == 1);

        source.SetReferenced([]);
        Assert.False(source.IsRunning);
        await WaitFor(() => Volatile.Read(ref cancelled) == 1);

        using var unconfigured = new SpotifyPlayingSource(() => (false, false, null), null, new FakeTime());
        unconfigured.SetReferenced([null]);
        Assert.False(unconfigured.IsRunning);
    }

    // ---- bin_day --------------------------------------------------------------------------------------------------

    [Fact]
    public void BinDay_IsTrueFromTheEveningBeforeUntilTheEndOfCollectionDay()
    {
        var clock = new FakeTime();
        var source = new BinDayDueSource(() => ("Black|#333|Mon|1|2026-10-05", null), clock);

        clock.Now = new DateTimeOffset(2026, 10, 4, 16, 59, 0, TimeSpan.Zero);
        Assert.False(source.IsActive(null));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(source.IsActive(null));
        clock.Now = new DateTimeOffset(2026, 10, 5, 23, 59, 0, TimeSpan.Zero);
        Assert.True(source.IsActive(null));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.False(source.IsActive(null));
    }

    [Fact]
    public void BinDay_UsesTheConfiguredEveningHour_AndFollowsChangedRules()
    {
        var clock = new FakeTime { Now = new DateTimeOffset(2026, 10, 4, 19, 0, 0, TimeSpan.Zero) };
        var bins = "Black|#333|Mon|1|2026-10-05";
        int? hour = 20;
        var source = new BinDayDueSource(() => (bins, hour), clock);

        Assert.False(source.IsActive(null));
        hour = 18;
        Assert.True(source.IsActive(null));
        bins = "Garden|#333|Wed|1|2026-10-07";
        Assert.False(source.IsActive(null));
        bins = null;
        Assert.False(source.IsActive(null));
    }

    [Fact]
    public void BinDay_ReadsPersistedAppSettings_FallingBackToConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), "bin-day-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var storage = new LedMatrixOS.Core.AppSettingsStorage(path);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BinDay:Bins"] = "Config|#111|Mon|1|2026-10-05",
            }).Build();
            var clock = new FakeTime { Now = new DateTimeOffset(2026, 10, 4, 18, 0, 0, TimeSpan.Zero) };
            var source = new BinDayDueSource(storage, config, clock);

            Assert.True(source.IsActive(null));                                   // configuration fallback: Monday

            storage.UpdateAppSetting(BinDayDueSource.AppId, "bins", "Saved|#222|Wed|1|2026-10-07");
            Assert.False(source.IsActive(null));                                  // persisted settings win
            storage.UpdateAppSetting(BinDayDueSource.AppId, "bins", "Saved|#222|Mon|1|2026-10-05");
            storage.UpdateAppSetting(BinDayDueSource.AppId, "eveningHour", 19);
            Assert.False(source.IsActive(null));
            storage.UpdateAppSetting(BinDayDueSource.AppId, "eveningHour", 18.0);
            Assert.True(source.IsActive(null));

            source.SetReferenced([null]);
            Assert.True(source.IsReferenced);
            Assert.Equal("bin_day", source.Kind);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BinDay_IncludesCalendarOneOffs_AndCachesTheFeedForFifteenMinutes()
    {
        var clock = new FakeTime { Now = new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.Zero) };   // Friday evening
        int fetches = 0;
        string? keyword = "bulky";
        Task<List<CalEvent>> Fetch(CancellationToken _)
        {
            Interlocked.Increment(ref fetches);
            return Task.FromResult(new List<CalEvent>
            {
                new("Bulky waste collection", new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), true, null),
                new("Dentist", new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero), false, null),
            });
        }
        // The regular rule (Monday) is not due on a Friday evening or Saturday.
        var source = new BinDayDueSource(() => ("Black|#333|Mon|1|2026-10-05", null), () => keyword, Fetch, clock);

        Assert.False(source.IsActive(null));                       // feed not loaded yet; this starts the first background fetch
        for (int i = 0; i < 100 && Volatile.Read(ref fetches) < 1; i++) await Task.Delay(20);
        await Task.Delay(50);
        Assert.True(source.IsActive(null));                        // one-off on Saturday, evening before
        // Inside 15 minutes of the last fetch nothing is fetched again.
        clock.Now = new DateTimeOffset(2026, 10, 9, 18, 14, 0, TimeSpan.Zero);
        Assert.True(source.IsActive(null));
        await Task.Delay(50);
        Assert.Equal(1, fetches);

        // After 15 minutes a background refresh is started.
        clock.Now = new DateTimeOffset(2026, 10, 9, 18, 16, 0, TimeSpan.Zero);
        Assert.True(source.IsActive(null));
        for (int i = 0; i < 100 && Volatile.Read(ref fetches) < 2; i++) await Task.Delay(20);
        Assert.Equal(2, fetches);

        clock.Now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.True(source.IsActive(null));                        // the day itself
        clock.Now = new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);
        Assert.False(source.IsActive(null));                       // gone the day after

        // Without a keyword the calendar is ignored.
        keyword = "";
        Assert.False(source.IsActive(null));
    }

    // ---- coordinator + schedule -----------------------------------------------------------------------------------

    private sealed class RecordingSource(string kind) : ILazyAttentionSource
    {
        public List<string?[]> Calls { get; } = new();
        public string Kind => kind;
        public bool IsActive(string? argument) => false;
        public void SetReferenced(IReadOnlyCollection<string?> arguments) => Calls.Add(arguments.ToArray());
    }

    private static void AddRule(ScheduleService schedule, string? condition) =>
        schedule.AddRule(new ScheduleRule { PlaylistId = "p", Condition = condition });

    [Fact]
    public void Coordinator_HandsEachSourceItsReferencedArguments_AndFollowsReloads()
    {
        var schedule = new ScheduleService();
        AddRule(schedule, "line_disrupted:victoria");
        AddRule(schedule, "line_disrupted:central");
        AddRule(schedule, "ha_state:light.lamp=on");
        AddRule(schedule, null);

        var line = new RecordingSource("line_disrupted");
        var bus = new RecordingSource("bus_due");
        var ha = new RecordingSource("ha_state");
        using var coordinator = new AttentionCoordinator(schedule, [line, bus, ha]);
        coordinator.Start();

        Assert.Equal(new[] { "victoria", "central" }.OrderBy(x => x), line.Calls.Last().OrderBy(x => x));
        Assert.Empty(bus.Calls.Last());                       // nothing references bus_due: stays stopped
        Assert.Equal(new[] { "light.lamp=on" }, ha.Calls.Last());

        var doc = new ScheduleDocument
        {
            Playlists = { new PlaylistDocument { Name = "p", Entries = { new EntryDocument { AppId = "clock", DurationMs = 1000 } } } },
            Rules = { new RuleDocument { PlaylistId = "p", Condition = "bus_due:490000001A" } },
        };
        lock (schedule.Gate) schedule.Replace(doc);

        Assert.Empty(line.Calls.Last());
        Assert.Equal(new[] { "490000001A" }, bus.Calls.Last());
        Assert.Empty(ha.Calls.Last());

        coordinator.Dispose();
        lock (schedule.Gate) schedule.Replace(new ScheduleDocument());
        Assert.Empty(bus.Calls.Last());
    }

    [Fact]
    public async Task EndToEnd_RuleConditionStartsPollingAndEvaluatesThroughTheEvaluator()
    {
        var handler = new FakeHandler { Respond = _ => (HttpStatusCode.OK, LineJson(6, "Severe Delays")) };
        using var source = new LineDisruptionSource(new HttpClient(handler), (string?)null, new FakeTime());
        var evaluator = new AttentionEvaluator([source]);
        var schedule = new ScheduleService(attention: evaluator);
        using var coordinator = new AttentionCoordinator(schedule, [source]);
        coordinator.Start();

        Assert.False(source.IsPolling);                       // no rule yet
        Assert.False(evaluator.Evaluate("line_disrupted:victoria"));

        lock (schedule.Gate) AddRule(schedule, "line_disrupted:victoria");
        coordinator.Sync();
        Assert.True(source.IsPolling);
        await WaitFor(() => evaluator.Evaluate("line_disrupted:victoria"));

        lock (schedule.Gate) schedule.Clear();
        coordinator.Sync();
        Assert.False(source.IsPolling);
        Assert.False(evaluator.Evaluate("line_disrupted:victoria"));
    }

    [Fact]
    public void Coordinator_ASourceThatThrowsDoesNotBreakTheReload()
    {
        var schedule = new ScheduleService();
        AddRule(schedule, "bus_due:x");
        using var coordinator = new AttentionCoordinator(schedule, [new ThrowingSource(), new RecordingSource("bus_due")]);
        coordinator.Start();
        lock (schedule.Gate) schedule.Replace(new ScheduleDocument());
    }

    private sealed class ThrowingSource : ILazyAttentionSource
    {
        public string Kind => "bus_due";
        public bool IsActive(string? argument) => throw new InvalidOperationException();
        public void SetReferenced(IReadOnlyCollection<string?> arguments) => throw new InvalidOperationException();
    }
}
