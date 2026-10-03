using System.Numerics;
using LedMatrixOS.Apps.Rail;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// A UK station departures board: the station name on top, the next train emphasised (calling points scrolling beneath it) and the
/// following ones below, paged when there are more than fit. Delayed trains show their new time in amber, cancelled ones in red.
/// The bottom strip carries the clock, the minutes to the next train and a pulsing LAST TRAIN pill when the last service of the evening is
/// within 30 minutes. Departures come from an <see cref="IRailDepartureSource"/> (a hard-coded sample timetable until a real one is plugged in).
/// </summary>
/// <remarks>
/// The "last train home" overlay alert is not raised: apps have no hook into the engine's overlay manager (it is only reachable from
/// the REST endpoints), so the pulsing pill is the in-app signal.
/// </remarks>
public class RailDeparturesApp : WidgetApp
{
    public override string Id => "rail-departures";
    public override string Name => "Rail Departures";
    public override int FrameRate => 30;

    public const int MaxServicesLimit = 5;

    [Setting("Station", Description = "Station code (e.g. HVB).")]
    public string Station { get; set; } = HardcodedRailSource.SampleStationCode;

    [Setting("Max Services", Description = "Number of services listed on the board", Min = 1, Max = MaxServicesLimit)]
    public int MaxServices { get; set; } = MaxServicesLimit;

    [Setting("Platform Filter", Description = "Only show these platforms, comma separated (e.g. 1,2). Leave empty to show all.")]
    public string PlatformFilter { get; set; } = "";

    [Setting("Page Seconds", Description = "How long each page of services stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IRailDepartureSource _source;
    private readonly RailBoardModel _model = new();
    private CancellationTokenSource? _pollCts;
    private volatile ILiveData<RailService[]>? _services;
    private string _stationName = "";

    private Pager? _pager;
    private Label? _message;
    private Panel? _board;
    private Pill? _lastTrain;
    private bool _entered, _boardShown;

    public RailDeparturesApp(IRailDepartureSource? source = null)
    {
        _source = source ?? new HardcodedRailSource(() => Time.GetLocalNow());
        _stationName = StationLabel();
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var styles = new RailStyles();

        var header = new Panel
        {
            HAlign = Align.Stretch,
            Height = 9,
            Children =
            {
                new Block(new Pixel(16, 24, 70)),
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(3, 0),
                    Children =
                    {
                        new MarqueeLabel(() => _stationName) { Style = styles.Header, Grow = 1 },
                        new Label("Departures") { Style = styles.HeaderMuted },
                    },
                },
            },
        };

        var hero = new ListView<RailRow>(() => _model.Hero, r => new ScrollSlot(new RailHeroNode(r, styles), RailHeroNode.RowHeight), r => r.Key)
            { HAlign = Align.Stretch, EnterOffset = 0, EnterDuration = 350.Ms(), ExitDuration = 350.Ms() };

        _pager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Left) { Duration = 500.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch }
            .Bind(() => _model.Pages, token => BuildPage(token, styles));

        _board = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children = { new Stack(Orientation.Vertical) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { header, hero, _pager } } },
        };

        _message = new Label(() => MessageText()) { Style = styles.Message, HAlign = Align.Center, VAlign = Align.Center, Visible = false };

        var nextMinutes = new RollingNumber(() => Math.Max(0, _nextMinutes)) { Style = styles.Clock };
        var next = new Stack(Orientation.Horizontal, gap: 3)
        {
            CrossAlign = Align.Center,
            HAlign = Align.Center,
            VAlign = Align.Center,
            Children = { new Label("Next") { Style = styles.Strip }, nextMinutes, new Label("min") { Style = styles.Strip } },
        };
        _nextStrip = next;

        _lastTrain = new Pill("LAST TRAIN", RailStyles.Amber, pulse: true)
        {
            Style = new TextStyle(Fonts.QuiteSmall, Pixel.Black, Shadow: false),
            Height = 9,
            Radius = 3,
            PulsePeriod = TimeSpan.FromSeconds(0.9),
            HAlign = Align.End,
            VAlign = Align.Center,
            Visible = false,
        };

        var strip = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.End,
            Height = 12,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(3, 0),
                    Children =
                    {
                        new Clock("HH:mm", Time) { Style = styles.Clock },
                        new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { next } },
                        new Panel { Width = 52, HAlign = Align.End, VAlign = Align.Stretch, Children = { _lastTrain } },
                    },
                },
            },
        };

        var content = new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _board, _message } };
        return new Stack(Orientation.Vertical) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { content } }, strip } };
    }

    private Stack? _nextStrip;
    private int _nextMinutes;

    private Node BuildPage(RailPageToken token, RailStyles styles) => new ListView<RailRow>(
        () => _model.Page(token.Index), r => new ScrollSlot(new RailRowNode(r, styles), RailRowNode.RowHeight), r => r.Key)
        { HAlign = Align.Stretch, EnterOffset = 0, EnterDuration = 350.Ms(), ExitDuration = 350.Ms() };

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        var now = Time.GetLocalNow();
        _model.Refresh(now, _services?.Value, PlatformFilter, Math.Clamp(MaxServices, 1, MaxServicesLimit));

        bool any = _model.Visible.Count > 0;
        _message!.Visible = !any;
        _board!.Visible = any;

        _nextMinutes = _model.NextMinutes(now);
        _nextStrip!.Visible = _nextMinutes >= 0;

        var last = _model.LastTrainMinutes(now);
        bool pulsing = last is { } m && TimeSpan.FromMinutes(m) <= RailBoardModel.LastTrainWindow;
        _lastTrain!.Visible = pulsing;
        if (pulsing) _lastTrain.Background = last <= 15 ? new Pixel(255, 90, 60) : RailStyles.Amber;

        base.Update(context, cancellationToken);

        if (any && !_boardShown) Motion.SlideIn(_board, Animator, new Vector2(0, 16), TimeSpan.Zero, 380.Ms());
        _boardShown = any;
        if (!_entered) PlayEntrance();
    }

    private string MessageText()
    {
        var data = _services;
        if (data is { Value: null, Error: not null }) return "No data, retrying";
        if (data?.Value is null) return "Loading departures";
        return "No trains due";
    }

    private void PlayEntrance()
    {
        _entered = true;
        Motion.SlideIn(_message!, Animator, new Vector2(-120, 0), TimeSpan.Zero, 500.Ms(), Easing.OutBack);
    }

    // ---- settings ---------------------------------------------------------------------------------------------------------------

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "station": RestartPolling(); break;
            case "pageSeconds":
                if (_pager is not null) _pager.Interval = PageSeconds.Seconds();
                break;
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken cancellationToken)
    {
        _entered = _boardShown = false;
        RestartPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    private string StationLabel()
    {
        var code = (Station ?? "").Trim();
        return _source.StationName(code) ?? code.ToUpperInvariant();
    }

    private void RestartPolling()
    {
        _pollCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pollCts = cts;

        var code = (Station ?? "").Trim();
        _stationName = StationLabel();
        _services = Poll(RefreshInterval, ct => _source.GetDeparturesAsync(code, ct), cts.Token);
    }

    internal RailBoardModel Model => _model;
    internal Pager? ServicePager => _pager;
    internal Pill? LastTrainPill => _lastTrain;

    /// <summary>Test seam: replaces the polled data with a fixed source (call before the first frame).</summary>
    internal void UseData(ILiveData<RailService[]> services, string? stationName = null)
    {
        _pollCts?.Cancel();
        _services = services;
        if (stationName is not null) _stationName = stationName;
    }
}
