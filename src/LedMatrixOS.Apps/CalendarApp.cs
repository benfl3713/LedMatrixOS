using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// The next calendar event from an iCalendar (.ics) feed, big, with the following few beside it. The feed URL is configuration only
/// (<c>Calendar:IcsUrl</c>) because private feed URLs carry a secret; it is polled every 15 minutes and expanded 30 days ahead.
/// The event in progress shows how far through it you are. With <see cref="TimelineSeconds"/> set, a "Today" page (event bars on an hour axis)
/// alternates with the board.
/// </summary>
public class CalendarApp : WidgetApp
{
    public override string Id => "calendar";
    public override string Name => "Calendar";
    public override int FrameRate => 15;

    [Setting("Show All Day Events", Description = "Include all-day events (birthdays, holidays) in the list.")]
    public bool ShowAllDay { get; set; } = true;

    [Setting("Timeline Seconds", Description = "Alternate with a Today timeline page, each page staying this long. 0 keeps the next-event board only.", Min = 0, Max = 60)]
    public int TimelineSeconds { get; set; }

    [Setting("Title Overflow", Description = "What happens to a title too long for its space: Scroll marquees it, Wrap uses two lines when the location line is hidden (otherwise it is cut with an ellipsis), Clip cuts it off.", Options = ["Scroll", "Wrap", "Clip"])]
    public string TitleOverflow { get; set; } = "Scroll";

    [Setting("Max Events", Description = "How many upcoming events to show in all, the big one plus the list beside it (1-8). The list shows three at a time and rotates when there are more.", Min = 1, Max = 8)]
    public int MaxEvents { get; set; } = 4;

    [Setting("Look Ahead Days", Description = "Ignore events further away than this many days (1-14).", Min = 1, Max = 14)]
    public int LookAheadDays { get; set; } = 14;

    [Setting("Hide Declined", Description = "Hide events you declined. Set Calendar:SelfEmail in config so your own answer is the one that counts; without it only events every attendee declined are hidden.")]
    public bool HideDeclined { get; set; }

    [Setting("Show Location", Description = "Show the location (or the end time) under the title.")]
    public bool ShowLocation { get; set; } = true;

    [Setting("24-Hour Format", Description = "Times as 14:30 instead of 2:30 PM.")]
    public bool Show24Hour { get; set; } = true;

    private static readonly IReadOnlyList<CalEvent> None = [];
    private static readonly IReadOnlyList<CalPage> BoardOnly = [CalPage.Board], BoardAndTimeline = [CalPage.Board, CalPage.Timeline];

    private readonly HttpClient _http;
    private string _url = "";
    private volatile ILiveData<List<CalEvent>>? _data;
    private CancellationTokenSource? _pollCts;
    private readonly CalendarModel _model = new();
    private CalendarStyles? _styles;
    private Pager? _pager;
    private Stack? _message;
    private IReadOnlyList<CalPage> _pages = BoardOnly;
    private int _appliedTimelineSeconds = -1;
    private List<CalEvent>? _lastSource;
    private IReadOnlyList<CalEvent> _filtered = None;
    private bool _lastShowAll = true, _lastHideDeclined;
    private int _lastLookAhead = -1;
    private DateTime _lastDay;
    private string _selfEmail = "";

    /// <summary>Zone events are shown in; replaced by tests for deterministic output.</summary>
    internal TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Local;

    public CalendarApp(HttpClient httpClient)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
        _http = httpClient;
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _styles = new CalendarStyles();

        _pager = new Pager(1, TimeSpan.FromDays(1), new SlideTransition(MoveDirection.Left) { Duration = 500.Ms() }, Easing.InOutCubic)
            .Bind(() => _pages, page => page == CalPage.Board ? BuildBoard() : BuildTimeline());

        _message = new Stack(Orientation.Vertical, gap: 4)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Center,
            CrossAlign = Align.Center,
            Visible = false,
            Children =
            {
                new TintedText(Fonts.Big, () => _model.MessageTitle, () => _model.MessageColor, shadow: true),
                new TintedText(Fonts.Small, () => _model.MessageSub, () => CalendarStyles.Muted),
            },
        };

        return new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _pager, _message } };
    }

    private Node BuildBoard()
    {
        var styles = _styles!;
        var upcoming = new ListView<CalEvent>(() => _model.Next, ev => new ScrollSlot(new EventRow(ev, _model, styles), EventRow.RowHeight), ev => (ev.Title, ev.Start))
        {
            HAlign = Align.Stretch,
            VAlign = Align.Start,
            Gap = 5,
            Margin = new Thickness(4, 3, 0, 0),
            ItemChanged = (node, ev) => ((EventRow)((ScrollSlot)node).Content).Set(ev),
        };

        return new Dock
        {
            Left = new Spine(_model),
            Right = new Panel
            {
                Width = 96,
                Children = { new Block(new Pixel(28, 28, 36), width: 1) { Margin = new Thickness(0, 4) }, upcoming },
            },
            Fill = new HeroNode(_model, styles),
        };
    }

    private Node BuildTimeline()
    {
        var styles = _styles!;
        var header = new Stack(Orientation.Horizontal, gap: 4)
        {
            Height = 7,
            HAlign = Align.Stretch,
            VAlign = Align.Start,
            Margin = new Thickness(6, 4, 6, 0),
            Children =
            {
                new Label("TODAY") { Style = styles.Tiny },
                new Label(() => _model.TodayText) { Style = styles.Caption },
                new MarqueeLabel(() => _model.AllDayText) { Style = styles.AllDay, Grow = 1, TextAlignment = TextAlign.Right },
            },
        };
        return new Panel { Children = { new TimelineCanvas(_model), header } };
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;
        var data = _data;
        var source = data?.Value;
        var now = TimeZoneInfo.ConvertTime(Time.GetUtcNow(), Zone);
        int lookAhead = Math.Clamp(LookAheadDays, 1, 14);
        if (!ReferenceEquals(source, _lastSource) || ShowAllDay != _lastShowAll || HideDeclined != _lastHideDeclined
            || lookAhead != _lastLookAhead || now.Date != _lastDay)
        {
            _lastSource = source;
            _lastShowAll = ShowAllDay;
            _lastHideDeclined = HideDeclined;
            _lastLookAhead = lookAhead;
            _lastDay = now.Date;
            var horizon = now.Date.AddDays(lookAhead + 1);
            bool allDay = ShowAllDay, hideDeclined = HideDeclined;
            _filtered = source == null ? None
                : source.Where(e => (allDay || !e.AllDay) && !(hideDeclined && e.Declined) && e.Start.DateTime < horizon).ToList();
        }

        _model.Configure(MaxEvents, Show24Hour, ShowLocation, TitleOverflow);
        var message =
            _url.Length == 0 ? BoardMessage.NotConfigured :
            source == null ? (data is { Error: not null } ? BoardMessage.Offline : BoardMessage.Loading) :
            CalendarFormat.FirstUpcoming(_filtered, now) < 0 ? BoardMessage.Empty : BoardMessage.None;
        _model.Update(_filtered, now, context.Time, message);

        _pager!.Visible = message == BoardMessage.None;
        _message!.Visible = message != BoardMessage.None;

        int seconds = Math.Clamp(TimelineSeconds, 0, 60);
        if (seconds != _appliedTimelineSeconds)
        {
            _appliedTimelineSeconds = seconds;
            _pages = seconds > 0 ? BoardAndTimeline : BoardOnly;
            _pager.Interval = seconds > 0 ? seconds.Seconds() : TimeSpan.FromDays(1);
        }

        base.Update(context, cancellationToken);
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        _url = (configuration["Calendar:IcsUrl"] ?? "").Trim();
        _selfEmail = (configuration["Calendar:SelfEmail"] ?? "").Trim();
        CancelPoll(ref _pollCts);
        _data = null;
        _lastSource = null;
        if (_url.Length == 0) return;

        var url = _url.StartsWith("webcal", StringComparison.OrdinalIgnoreCase) ? "https" + _url[6..] : _url;
        _data = RestartPoll(ref _pollCts, TimeSpan.FromMinutes(15), ct => FetchAsync(url, ct));
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        CancelPoll(ref _pollCts);
        await base.OnDeactivatedAsync(cancellationToken);
    }

    private async Task<List<CalEvent>> FetchAsync(string url, CancellationToken ct)
    {
        var text = await _http.GetStringAsync(url, ct);
        var now = DateTimeOffset.UtcNow;
        return IcsParser.Parse(text, now.AddDays(-1), now.AddDays(30), Zone, _selfEmail);
    }

    internal CalendarModel Model => _model;
    internal Pager? Pages => _pager;

    /// <summary>Test seam: fixed events, as if the feed had loaded (call before the first frame).</summary>
    internal void UseData(ILiveData<List<CalEvent>>? events, string url = "https://example.com/cal.ics")
    {
        _url = url;
        _data = events;
        _lastSource = null;
    }
}
