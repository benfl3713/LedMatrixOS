using LedMatrixOS.Apps.PlaneSpotter;
using System.Numerics;
using LedMatrixOS.Apps.Journey;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// "Leave by": the best TfL Journey Planner route from <see cref="From"/> to <see cref="To"/> as a row of line pills with the duration and
/// arrival time, and a Commute-style "LEAVE IN n MIN" hero counting down to the first vehicle (less your walk buffer). The countdown follows
/// the app clock between polls, so it keeps running while the data refreshes once a minute.
/// </summary>
public class JourneyApp : WidgetApp
{
    public override string Id => "journey";
    public override string Name => "Journey Planner";
    public override int FrameRate => 30;

    private const int HeroWidth = 72;
    private const int StripHeight = 12;
    private const int HeaderHeight = 12;
    private static readonly IReadOnlyList<JourneyToken> NoTokens = [];

    [Setting("From", Description = "Where you start: search for a place, station or postcode.", Search = true)]
    public string From { get; set; } = "";

    [Setting("To", Description = "Where you are going: search for a place, station or postcode.", Search = true)]
    public string To { get; set; } = "";

    [Setting("Destination Label", Description = "Short name shown in the header (e.g. Work). Defaults to the To value.")]
    public string DestinationLabel { get; set; } = "";

    [Setting("Walk Buffer", Description = "Minutes it takes you to reach the first stop; the countdown ends this long before the vehicle leaves.", Min = 0, Max = 30)]
    public int WalkBuffer { get; set; } = 5;

    [Setting("Journeys", Description = "How many alternative journeys to cycle through.", Min = 1, Max = 3)]
    public int Journeys { get; set; } = 2;

    [Setting("Modes", Description = "Which kinds of transport to plan with.", Options = ["All", "Tube only", "Bus only", "Rail"])]
    public string Modes { get; set; } = "All";

    [Setting("Page Seconds", Description = "How long each journey stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    [Setting("Show Walking Legs", Description = "Show the walking legs as grey pills in each journey.")]
    public bool ShowWalkingLegs { get; set; } = true;

    [Setting("Preference", Description = "What TfL optimises for: the quickest journey, the fewest changes or the least walking.", Options = ["Fastest", "Fewest changes", "Least walking"])]
    public string Preference { get; set; } = "Fastest";

    [Setting("Leave After", Description = "Plan for this many minutes from now, e.g. when you are not leaving straight away.", Min = 0, Max = 120)]
    public int LeaveAfter { get; set; }

    private readonly TflApi _api;
    private IJourneySource _source;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);

    private volatile ILiveData<JourneyResult>? _data;
    private CancellationTokenSource? _cts;
    private bool _active;

    private sealed record View(JourneyResult? Result, int Count, JourneyOption[] Options, IReadOnlyList<JourneyToken> Tokens);
    private View _view = new(null, 0, [], NoTokens);
    private JourneyPlan _plan = JourneyPlan.None;
    private DateTime _anchorLocal;
    private TimeSpan _anchorTime;
    private bool _anchored, _shownWalking = true;

    private string _headerText = "", _fromText = "";
    private JourneyHero? _hero;
    private JourneyStateScreen? _state;
    private Pager? _pager;
    private Pill? _disruption;
    private bool _disrupted;
    private Node? _body, _strip;
    private bool _entered, _bodyShown;

    [ActivatorUtilitiesConstructor]
    public JourneyApp(HttpClient httpClient) : this(httpClient, null) { }

    internal JourneyApp(HttpClient httpClient, IJourneySource? source)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(15);
        _api = new TflApi(httpClient);
        _source = source ?? new TflJourneySource(_api);
        RefreshLabels();
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _hero = new JourneyHero { Width = HeroWidth };
        _state = new JourneyStateScreen();

        var transition = new SlideTransition(MoveDirection.Left) { Duration = 500.Ms() };
        _pager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: transition, easing: Easing.InOutCubic) { Grow = 1 }
            .Bind(() => _view.Tokens, token => new JourneyPage(_view.Options[token.Index], token.Index, _view.Options.Length, token.ShowWalking));

        var header = new Label(() => _headerText) { Style = new TextStyle(Fonts.Small, TubeGfx.Amber, Shadow: false), Height = HeaderHeight, Padding = new Thickness(4, 0, 0, 0) };

        _body = new Stack(Orientation.Horizontal, gap: 2)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            CrossAlign = Align.Stretch,
            Children =
            {
                _hero,
                new Stack(Orientation.Vertical)
                {
                    Grow = 1,
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    Children = { header, _pager },
                },
            },
        };

        var clock = new TextStyle(Fonts.Small, new Pixel(245, 245, 245), Shadow: false);
        // "DELAYS" in the small font reads at a glance; while it shows, the From label gives way to it.
        _disruption = new Pill("DELAYS", TubeGfx.Amber, pulse: true)
            { Style = new TextStyle(Fonts.Small, Pixel.Black, Shadow: false), Height = 10, Padding = new Thickness(3, 0), Visible = false };
        _strip = new Panel
        {
            Height = StripHeight,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 4)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(4, 1),
                    Children =
                    {
                        new Clock("HH:mm", Time) { Style = clock },
                        new Label(() => _disrupted ? "" : _fromText) { Style = new TextStyle(Fonts.QuiteSmall, new Pixel(150, 150, 160), Shadow: false), Grow = 1 },
                        _disruption,
                    },
                },
            },
        };

        return new Dock { Bottom = _strip, Fill = new Panel { _body, _state } };
    }

    // ---- per frame --------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        if (!ReferenceEquals(From, _labelFrom) || !ReferenceEquals(To, _labelTo) || !ReferenceEquals(DestinationLabel, _labelName)) RefreshLabels();
        var state = Refresh(context.Time);
        bool hasBoard = state is null;
        if (hasBoard)
        {
            var now = _anchorLocal + (context.Time - _anchorTime);
            _plan = JourneyPlanner.Plan(_view.Options, now, WalkBuffer);
            _hero!.Plan = _plan;
            var shown = _view.Options[Math.Max(0, _plan.Index)];
            _hero.Spine = shown.FirstTransit is { } leg ? JourneyParser.ColorOf(leg) : TubeGfx.Muted;
            _disrupted = _disruption!.Visible = shown.Disrupted;
        }
        else
        {
            _plan = JourneyPlan.None;
            _disrupted = _disruption!.Visible = false;
            _state!.State = state!.Value;
        }

        _state!.Visible = !hasBoard;
        _body!.Visible = hasBoard;

        base.Update(context, cancellationToken);

        if (hasBoard && !_bodyShown) Motion.SlideIn(_body, Animator, new Vector2(0, 14), TimeSpan.Zero, 380.Ms());
        _bodyShown = hasBoard;
        if (!_entered)
        {
            _entered = true;
            Motion.SlideIn(_strip!, Animator, new Vector2(0, 14), 150.Ms());
            Motion.SlideIn(_state, Animator, new Vector2(-120, 0), TimeSpan.Zero, 500.Ms(), Easing.OutBack);
        }
    }

    /// <summary>Picks up new data (re-anchoring the countdown to the app clock) and returns the state to show, or null when there are journeys.</summary>
    private JourneyState? Refresh(TimeSpan frameTime)
    {
        if (string.IsNullOrWhiteSpace(From) || string.IsNullOrWhiteSpace(To)) return JourneyState.NotConfigured;

        var data = _data;
        var result = data?.Value;
        if (result is null) return data?.Error is not null ? JourneyState.Offline : JourneyState.Loading;

        if (!ReferenceEquals(result, _view.Result) || Journeys != _view.Count || ShowWalkingLegs != _shownWalking)
        {
            if (!ReferenceEquals(result, _view.Result) || !_anchored)
            {
                _anchorLocal = Time.GetLocalNow().DateTime;
                _anchorTime = frameTime;
                _anchored = true;
            }

            _shownWalking = ShowWalkingLegs;
            var options = result.Journeys.Take(Math.Clamp(Journeys, 1, JourneyParser.MaxJourneys)).ToArray();
            _view = new View(result, Journeys, options, options.Select((o, i) => new JourneyToken(i, o.Key, ShowWalkingLegs)).ToArray());
        }

        return result.Status switch
        {
            JourneyStatus.Ok when _view.Options.Length > 0 => null,
            JourneyStatus.Ok or JourneyStatus.NoRoute => JourneyState.NoRoute,
            JourneyStatus.Ambiguous => JourneyState.CheckAddress,
            _ => JourneyState.Offline,
        };
    }

    // ---- settings & lifecycle -------------------------------------------------------------------------------------------------------

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "from" or "to" or "destinationLabel": RefreshLabels(); break;
            case "pageSeconds":
                if (_pager is not null) _pager.Interval = PageSeconds.Seconds();
                break;
        }

        if (_active && key is "from" or "to" or "modes" or "preference" or "leaveAfter") RestartPolling();
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _api.AppKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_api.AppKey)) Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");

        if (string.IsNullOrEmpty(From) && configuration["Journey:From"] is { Length: > 0 } from) From = from;
        if (string.IsNullOrEmpty(To) && configuration["Journey:To"] is { Length: > 0 } to) To = to;
        if (string.IsNullOrEmpty(DestinationLabel) && configuration["Journey:Label"] is { Length: > 0 } label) DestinationLabel = label;
        RefreshLabels();

        _active = true;
        _entered = _bodyShown = false;
        RestartPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        CancelPoll(ref _cts);
        return base.OnDeactivatedAsync(cancellationToken);
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    private string? _labelFrom, _labelTo, _labelName;

    private void RefreshLabels()
    {
        (_labelFrom, _labelTo, _labelName) = (From, To, DestinationLabel);
        var label = string.IsNullOrWhiteSpace(DestinationLabel) ? PlaceGeocoder.DisplayName(To) : DestinationLabel;
        _headerText = string.IsNullOrWhiteSpace(label) ? "JOURNEY" : "TO " + label.Trim().ToUpperInvariant();
        _fromText = string.IsNullOrWhiteSpace(From) ? "" : "From " + PlaceGeocoder.DisplayName(From.Trim());
    }

    /// <summary>What the Journey Planner is asked for: a place picked in the app ("Name|lat,lon") goes as its coordinates, anything else as typed.</summary>
    internal static string PlannerPoint(string value) =>
        PlaceGeocoder.TryParseEncoded(value.Trim(), out _, out var c)
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{c.Lat},{c.Lon}")
            : value.Trim();

    internal static string ModesParameter(string? modes) => modes switch
    {
        "Tube only" => "tube,walking",
        "Bus only" => "bus,walking",
        "Rail" => "national-rail,overground,elizabeth-line,dlr,walking",
        _ => "",
    };

    internal static string PreferenceParameter(string? preference) => preference switch
    {
        "Fewest changes" => "LeastInterchange",
        "Least walking" => "LeastWalking",
        _ => "LeastTime",
    };

    private void RestartPolling()
    {
        CancelPoll(ref _cts);
        _data = null;
        _view = new View(null, 0, [], NoTokens);
        _anchored = false;
        if (string.IsNullOrWhiteSpace(From) || string.IsNullOrWhiteSpace(To)) return;

        string from = PlannerPoint(From), to = PlannerPoint(To), modes = ModesParameter(Modes), preference = PreferenceParameter(Preference);
        var leaveAfter = TimeSpan.FromMinutes(Math.Clamp(LeaveAfter, 0, 120));
        var source = _source;
        JourneyResult? lastGood = null;
        _data = RestartPoll(ref _cts, _interval, async ct =>
        {
            var result = await source.GetAsync(new JourneyQuery(from, to, modes, Time.GetLocalNow().DateTime + leaveAfter, preference), ct);
            if (result.Status == JourneyStatus.Ok) lastGood = result;
            // A blip in the connection should not blank a board the traveller is still using.
            return result.Status == JourneyStatus.Offline && lastGood is not null ? lastGood : result;
        });
    }

    internal JourneyPlan CurrentPlan => _plan;
    internal Pager? JourneyPager => _pager;

    /// <summary>Test seam: replaces the polled data with a fixed source (call before the first frame).</summary>
    internal void UseData(ILiveData<JourneyResult>? data)
    {
        CancelPoll(ref _cts);
        _data = data;
        _view = new View(null, 0, [], NoTokens);
        _anchored = false;
    }
}

/// <summary>Identifies one page of the journey pager: the option's position and content, so changed journeys rebuild their pages.</summary>
internal readonly record struct JourneyToken(int Index, string Key, bool ShowWalking = true);

/// <summary>The real thing: TfL Journey Planner over HTTP.</summary>
internal sealed class TflJourneySource(TflApi api) : IJourneySource
{
    public Task<JourneyResult> GetAsync(JourneyQuery query, CancellationToken ct) =>
        api.GetJourneysAsync(query.From, query.To, query.When, query.Modes, ct, query.Preference);
}
