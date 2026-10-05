using System.Diagnostics;
using System.Net;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Commute;
using LedMatrixOS.Apps.Journey;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

internal sealed class FakeJourneySource(JourneyResult result) : IJourneySource
{
    public System.Collections.Concurrent.ConcurrentQueue<JourneyQuery> Queries { get; } = new();

    public Task<JourneyResult> GetAsync(JourneyQuery query, CancellationToken ct)
    {
        Queries.Enqueue(query);
        return Task.FromResult(result);
    }
}

public class JourneyAppTests(ITestOutputHelper output)
{
    // The fake clock reads 2026-01-02 13:45:07 local.
    private static readonly DateTime Now = new(2026, 1, 2, 13, 45, 7);

    private static string Leg(string mode, string line, string route, string from, int mins, string dep, string arr, bool disrupted = false) => $$$"""
        {"duration":{{{mins}}},"departureTime":"2026-01-02T{{{dep}}}","arrivalTime":"2026-01-02T{{{arr}}}","isDisrupted":{{{(disrupted ? "true" : "false")}}},
         "disruptions":[],"departurePoint":{"commonName":"{{{from}}}"},"mode":{"id":"{{{mode}}}","name":"{{{mode}}}"},
         "routeOptions":[{"name":"{{{route}}}","lineIdentifier":{"id":"{{{line}}}","name":"{{{route}}}"}}]}
        """;

    private static string Journey(string start, string arrive, int mins, params string[] legs) =>
        $$$"""{"startDateTime":"2026-01-02T{{{start}}}","arrivalDateTime":"2026-01-02T{{{arrive}}}","duration":{{{mins}}},"legs":[{{{string.Join(",", legs)}}}]}""";

    /// <summary>Walk, Victoria line, bus 73: boards the tube at <paramref name="board"/>.</summary>
    private static string Typical(string board, string arrive, int mins, bool disrupted = false, string sec = "00") => Journey(board[..2] + ":00", arrive, mins,
        Leg("walking", "", "", "Home", 4, board[..2] + ":00:00", board + ":" + sec),
        Leg("tube", "victoria", "Victoria", "Brixton Underground Station", 12, board + ":" + sec, arrive[..3] + "40:00", disrupted),
        Leg("bus", "73", "73", "Oxford Circus Stop D", 9, arrive[..3] + "40:00", arrive + ":00"));

    private static JourneyResult Parse(params string[] journeys) =>
        JourneyParser.Parse(HttpStatusCode.OK, "{\"journeys\":[" + string.Join(",", journeys) + "]}");

    private static JourneyResult Board(int boardMinute = 58, bool disrupted = false) => Parse(
        Typical($"13:{boardMinute}", "14:20", 22, disrupted),
        Typical($"14:{boardMinute - 50:00}", "14:34", 26));

    private static (JourneyApp App, AppStage Stage) Stage(JourneyResult? result, string from = "SW9 8LQ", string to = "W1D 3QU", Exception? error = null)
    {
        Fonts.Load();
        var app = new JourneyApp(new HttpClient(new TflStubHandler()), new FakeJourneySource(JourneyResult.Of(JourneyStatus.Offline)))
            { Time = new FakeTime(), From = from, To = to, DestinationLabel = "Work" };
        app.UseData(new MutableLive<JourneyResult> { Value = result, Error = error });
        var stage = new AppStage(app);
        stage.Step(33, 45);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- parsing ------------------------------------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsWalkingTubeAndBusLegs()
    {
        var result = Parse(Typical("13:58", "14:20", 22, disrupted: true));

        Assert.Equal(JourneyStatus.Ok, result.Status);
        var j = Assert.Single(result.Journeys);
        Assert.Equal(22, j.Minutes);
        Assert.Equal(new DateTime(2026, 1, 2, 14, 20, 0), j.Arrival);
        Assert.Equal(["WALK 4", "VIC", "73"], j.Legs.Select(l => l.Label).ToArray());
        Assert.Equal([true, false, false], j.Legs.Select(l => l.Walking).ToArray());
        Assert.Equal("Brixton Underground Station", j.FirstTransit!.From);
        Assert.Equal(new DateTime(2026, 1, 2, 13, 58, 0), j.BoardAt);
        Assert.True(j.Disrupted);
        Assert.Equal("victoria", j.Legs[1].LineId);
    }

    [Fact]
    public void Parse_KeepsOnlyTheFirstThreeJourneys()
    {
        var result = Parse(Typical("13:58", "14:20", 22), Typical("14:03", "14:25", 22), Typical("14:08", "14:30", 22), Typical("14:13", "14:35", 22));
        Assert.Equal(3, result.Journeys.Length);
    }

    [Fact]
    public void Parse_DisambiguationBody_IsAmbiguous()
    {
        const string body = """
            {"$type":"Tfl.Api.Presentation.Entities.ItineraryResult, Tfl.Api.Presentation.Entities",
             "toLocationDisambiguation":{"matchStatus":"list","disambiguationOptions":[{"parameterValue":"1000000","uri":"x","place":{"commonName":"Victoria"},"matchQuality":900}]},
             "fromLocationDisambiguation":{"matchStatus":"identified","disambiguationOptions":[]}}
            """;
        Assert.Equal(JourneyStatus.Ambiguous, JourneyParser.Parse(HttpStatusCode.MultipleChoices, body).Status);
        Assert.Equal(JourneyStatus.Ambiguous, JourneyParser.Parse(HttpStatusCode.OK, body).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "")]
    public void Parse_ErrorsAreOffline_AndNeverThrow(HttpStatusCode status, string body) =>
        Assert.Equal(JourneyStatus.Offline, JourneyParser.Parse(status, body).Status);

    [Fact]
    public void Parse_NoJourneys_IsNoRoute() =>
        Assert.Equal(JourneyStatus.NoRoute, JourneyParser.Parse(HttpStatusCode.OK, """{"journeys":[]}""").Status);

    [Fact]
    public async Task Api_BuildsTheJourneyUrl_AndMapsHttpFailureToOffline()
    {
        var handler = new StatusHandler(HttpStatusCode.NotFound);
        var api = new TflApi(new HttpClient(handler)) { AppKey = "k" };

        var result = await api.GetJourneysAsync("SW9 8LQ", "51.5,-0.12", Now, "tube,walking", CancellationToken.None);

        Assert.Equal(JourneyStatus.Offline, result.Status);
        var url = handler.Requests.Single();
        Assert.Contains("/Journey/JourneyResults/SW9 8LQ/to/51.5,-0.12", url);
        Assert.Contains("time=1345", url);
        Assert.Contains("timeIs=Departing", url);
        Assert.Contains("mode=tube,walking", url);
        Assert.Contains("app_key=k", url);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("") });
        }
    }

    // ---- leave-in -----------------------------------------------------------------------------------------------

    [Fact]
    public void Planner_SubtractsTheWalkBuffer()
    {
        var options = Board(58).Journeys;   // boards at 13:58:00, 12:53 away
        var plan = JourneyPlanner.Plan(options, Now, 5);

        Assert.Equal(0, plan.Index);
        Assert.Equal(7 * 60 + 53, plan.LeaveInSeconds);
        Assert.Equal(Urgency.Relaxed, plan.Urgency);
        Assert.Equal(12 * 60 + 53, JourneyPlanner.Plan(options, Now, 0).LeaveInSeconds);
    }

    [Fact]
    public void Planner_SkipsMissedJourneys_AndReportsMissedWhenAllGone()
    {
        var options = Board(58).Journeys;   // 13:58 and 14:08
        var plan = JourneyPlanner.Plan(options, Now, 11);   // 13:58 needs leaving at 13:47

        Assert.Equal(0, plan.Index);
        Assert.Equal(1 * 60 + 53, plan.LeaveInSeconds);
        Assert.Equal(Urgency.Soon, plan.Urgency);

        var late = JourneyPlanner.Plan(options, Now, 14);   // first is gone, second boards 14:08, leave 13:54
        Assert.Equal(1, late.Index);

        var none = JourneyPlanner.Plan(options, Now, 30);
        Assert.True(none.Missed);
        Assert.False(JourneyPlanner.Plan([], Now, 5).Missed);
    }

    [Fact]
    public void Planner_CountsDownWithTheClock()
    {
        var options = Board(58).Journeys;
        Assert.Equal(7 * 60 + 53 - 60, JourneyPlanner.Plan(options, Now.AddMinutes(1), 5).LeaveInSeconds);
    }

    // ---- settings and polling -----------------------------------------------------------------------------------

    [Fact]
    public void Settings_KeepTheirKeysAndTypes()
    {
        Fonts.Load();
        var app = new JourneyApp(new HttpClient(new TflStubHandler()));
        var settings = app.GetSettings().ToList();

        Assert.Equal(new[] { "from", "to", "destinationLabel", "walkBuffer", "journeys", "modes", "pageSeconds" }, settings.Select(s => s.Key).ToArray());
        Assert.Equal("journey", app.Id);
        Assert.Equal("Journey Planner", app.Name);
        var walk = settings.Single(s => s.Key == "walkBuffer");
        Assert.Equal(0, walk.MinValue);
        Assert.Equal(30, walk.MaxValue);
        var journeys = settings.Single(s => s.Key == "journeys");
        Assert.Equal(1, journeys.MinValue);
        Assert.Equal(3, journeys.MaxValue);
        Assert.Equal(new[] { "All", "Tube only", "Bus only", "Rail" }, settings.Single(s => s.Key == "modes").Options);
    }

    [Theory]
    [InlineData("All", "")]
    [InlineData("Tube only", "tube,walking")]
    [InlineData("Bus only", "bus,walking")]
    public void Modes_MapToTflModes(string setting, string expected) => Assert.Equal(expected, JourneyApp.ModesParameter(setting));

    [Fact]
    public async Task Activation_PollsTheSource()
    {
        Fonts.Load();
        var source = new FakeJourneySource(Board());
        var app = new JourneyApp(new HttpClient(new TflStubHandler()), source) { Time = new FakeTime(), From = "SW9 8LQ", To = "W1D 3QU", Modes = "Bus only" };
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);
        var stage = new AppStage(app);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (app.CurrentPlan.Index < 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "journeys were not fetched in time");
            stage.Step(33);
            await Task.Delay(10);
        }

        var query = source.Queries.First();
        Assert.Equal(("SW9 8LQ", "W1D 3QU", "bus,walking"), (query.From, query.To, query.Modes));
        Assert.Equal(Now, query.When);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    // ---- rendering ----------------------------------------------------------------------------------------------

    [Fact]
    public void Snapshots()
    {
        var (best, bestStage) = Stage(Board(58, disrupted: true));
        Assert.Equal(Urgency.Relaxed, best.CurrentPlan.Urgency);
        Golden(bestStage, "journey_best");

        var (goNow, goStage) = Stage(Parse(Typical("13:50", "14:13", 22, sec: "40"), Typical("14:01", "14:23", 22)));
        Assert.Equal(Urgency.Now, goNow.CurrentPlan.Urgency);
        Golden(goStage, "journey_go_now");

        var (missed, missedStage) = Stage(Parse(Typical("13:48", "14:10", 22)));
        Assert.True(missed.CurrentPlan.Missed);
        Golden(missedStage, "journey_missed");

        var (_, notConfigured) = Stage(null, from: "", to: "");
        Golden(notConfigured, "journey_not_configured");

        var (_, check) = Stage(JourneyResult.Of(JourneyStatus.Ambiguous));
        Golden(check, "journey_check_address");

        var (_, offline) = Stage(null, error: new HttpRequestException("down"));
        Golden(offline, "journey_offline");
    }

    [Fact]
    public void AlternativesPage()
    {
        var (app, stage) = Stage(Board());
        Assert.Equal(2, app.JourneyPager!.PageCount);
        stage.Step(33, 6 * 30 + 40);
        Assert.Equal(1, app.JourneyPager.PageIndex);
    }

    // ---- performance --------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage) = Stage(Board(58));
        var run = stage.MeasureSteadyAllocation(windows: 12, warmFrames: 400, beginWindow: () => { int page = app.JourneyPager!.PageIndex; int minute = app.CurrentPlan.LeaveInSeconds / 60; return () => page == app.JourneyPager.PageIndex && !app.JourneyPager.IsTransitioning && minute == app.CurrentPlan.LeaveInSeconds / 60; });

        output.WriteLine($"journey: {run.MsPerFrame:F3} ms/frame, {run.Measured} steady windows");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");    }
}
