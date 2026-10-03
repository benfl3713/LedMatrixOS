using System.Globalization;
using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// Bin day and reminders. Bins come from the <c>Bins</c> rules (weekday, every N weeks from an anchor date, optional skip dates) and, optionally,
/// from Calendar feed events whose title contains <c>Calendar Keyword</c> (the feed URL stays configuration only: <c>Calendar:IcsUrl</c>).
/// From the evening before until the collection the display says which bins to put out; otherwise it lists the next collection of each bin.
/// A <c>Reminders</c> entry takes over the whole display with a pulse while its window is open. Local time comes from the app clock
/// (<see cref="WidgetApp.Time"/>), never the system clock directly.
/// </summary>
public class BinDayApp : WidgetApp
{
    public override string Id => "bin-day";
    public override string Name => "Bin Day";
    public override int FrameRate => 20;

    [Setting("Bins", Description = "Collection rules separated by ';' or new lines, fields by '|': " + BinParser.BinSyntax + ". Day is Mon-Sun, EveryNWeeks 1-4 (the anchor is a known collection date and sets the fortnightly week); a sixth field lists dates to skip, e.g. bank holidays: 2026-12-28,2027-01-04.")]
    public string Bins { get; set; } = "";

    [Setting("Calendar Keyword", Description = "Calendar feed events whose title contains this are shown as one-off collections. Empty turns it off.")]
    public string CalendarKeyword { get; set; } = "";

    [Setting("Reminders", Description = "Text cards shown while the time is inside a window, separated by ';' or new lines: " + BinParser.ReminderSyntax)]
    public string Reminders { get; set; } = "";

    [Setting("Evening Hour", Description = "From this hour the evening before, the display says to put the bin out.", Min = 0, Max = 23)]
    public int EveningHour { get; set; } = 17;

    [Setting("Page Seconds", Description = "How long each page of the collection list stays when there are more than two.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    private enum View { Message, Summary, Alert, Reminder }

    private static readonly IReadOnlyList<SummaryPage> NoPages = [];
    private const int RowsPerPage = 2;
    private const int MaxAlertIcons = 4;

    private readonly HttpClient? _http;
    private string _url = "";
    private volatile ILiveData<List<CalEvent>>? _data;
    private CancellationTokenSource? _pollCts;

    private BinStyles _styles = null!;
    private Pager _pager = null!;
    private Panel _alert = null!, _reminder = null!;
    private Stack _message = null!;
    private Pill _plate = null!;
    private readonly BinIcon[] _icons = new BinIcon[MaxAlertIcons];
    private Label _messageTitle = null!, _messageSub = null!;

    // Derived state, rebuilt only when inputs or the minute change.
    private string? _lastBins, _lastReminders, _lastKeyword;
    private int _lastEvening = -1, _lastMinute = -1;
    private DateOnly _lastDay;
    private List<CalEvent>? _lastEvents;
    private List<BinRule> _rules = [];
    private List<string> _binErrors = [];
    private List<Reminder> _reminders = [];
    private List<Collection> _extras = [];

    private View _view = View.Message;
    private string _headline = "", _reminderText = "", _title = "", _sub = "", _dateText = "";
    private Pixel _plateColour = Pixel.White;
    private string _plateText = "";
    private Collection[] _alertBins = [];
    private SummaryRowData[] _rows = [];
    private IReadOnlyList<SummaryPage> _pages = NoPages;
    private int _appliedPageSeconds = -1;
    private bool _dirty = true;

    public BinDayApp() { }

    public BinDayApp(HttpClient httpClient)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
        _http = httpClient;
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _styles = new BinStyles();
        var styles = _styles;

        // Alert: icons on the left, plate and headline on the right.
        var iconRow = new Stack(Orientation.Horizontal, gap: 4) { VAlign = Align.Center, Margin = new Thickness(8, 0, 4, 0) };
        for (int i = 0; i < MaxAlertIcons; i++)
        {
            _icons[i] = new BinIcon(mini: false, phaseSeconds: i * 0.7f);
            iconRow.Add(_icons[i]);
        }

        _plate = new Pill("TOMORROW", Pixel.White, pulse: true) { Style = styles.Plate, Radius = 2, HAlign = Align.Start };
        var right = new Stack(Orientation.Vertical, gap: 6)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Padding = new Thickness(4, 6, 8, 4),
            Children =
            {
                _plate,
                new MarqueeLabel(() => _headline) { Style = styles.Headline, HAlign = Align.Stretch },
                new Label(() => _sub) { Style = styles.Caption, HAlign = Align.Stretch },
            },
        };
        _alert = new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { new Dock { Left = iconRow, Fill = right } } };

        // Summary list.
        _pager = new Pager(1, PageSeconds.Seconds(), new SlideTransition(MoveDirection.Left) { Duration = 500.Ms() }, Easing.InOutCubic)
            { HAlign = Align.Stretch, VAlign = Align.Stretch, Margin = new Thickness(8, 4, 8, 0) }
            .Bind(() => _pages, BuildSummaryPage);

        // Reminder card.
        _reminder = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Visible = false,
            Children =
            {
                new PulseBlock(),
                new MarqueeLabel(() => _reminderText)
                {
                    Style = styles.Reminder,
                    TextAlignment = TextAlign.Center,
                    HAlign = Align.Stretch,
                    VAlign = Align.Center,
                    Margin = new Thickness(8, 0),
                },
            },
        };

        _messageTitle = new Label(() => _title) { Style = styles.Message, HAlign = Align.Center };
        _messageSub = new Label(() => _sub) { Style = styles.Caption, HAlign = Align.Center };
        _message = new Stack(Orientation.Vertical, gap: 5)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Center,
            CrossAlign = Align.Center,
            Children = { _messageTitle, _messageSub },
        };

        var strip = new Panel
        {
            HAlign = Align.Stretch,
            Height = 12,
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
                        new Clock("HH:mm", Time) { Style = styles.Clock },
                        new Label(() => _dateText) { Style = styles.Caption, Grow = 1, HAlign = Align.Stretch, TextAlignment = TextAlign.Right },
                    },
                },
            },
        };

        var main = new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _pager, _alert, _reminder, _message } };
        return new Dock { Bottom = strip, Fill = main };
    }

    private Node BuildSummaryPage(SummaryPage page)
    {
        var stack = new Stack(Orientation.Vertical, gap: 2) { HAlign = Align.Stretch, VAlign = Align.Center };
        for (int i = page.Index * RowsPerPage; i < Math.Min(_rows.Length, (page.Index + 1) * RowsPerPage); i++)
            stack.Add(new SummaryRow(_rows[i], _styles));
        return stack;
    }

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;
        EnsureCalendarPolling();

        var now = Time.GetLocalNow().DateTime;
        var events = _data?.Value;
        int minute = now.Hour * 60 + now.Minute;
        if (_dirty || !ReferenceEquals(events, _lastEvents) || minute != _lastMinute || DateOnly.FromDateTime(now) != _lastDay
            || Bins != _lastBins || Reminders != _lastReminders || CalendarKeyword != _lastKeyword || EveningHour != _lastEvening)
        {
            Rebuild(now, events);
        }

        SetVisible(_message, _view == View.Message);
        SetVisible(_pager, _view == View.Summary);
        SetVisible(_alert, _view == View.Alert);
        SetVisible(_reminder, _view == View.Reminder);

        if (_view == View.Alert)
        {
            for (int i = 0; i < MaxAlertIcons; i++)
            {
                bool shown = i < _alertBins.Length;
                SetVisible(_icons[i], shown);
                if (shown) _icons[i].Colour = _alertBins[i].Colour;
            }
        }

        int seconds = Math.Clamp(PageSeconds, 3, 30);
        if (seconds != _appliedPageSeconds)
        {
            _appliedPageSeconds = seconds;
            _pager.Interval = seconds.Seconds();
        }

        base.Update(context, cancellationToken);
    }

    // Visible is a layout property: setting it every frame, even to the same value, would re-lay-out every frame.
    private static void SetVisible(Node node, bool visible)
    {
        if (node.Visible != visible) node.Visible = visible;
    }

    private void Rebuild(DateTime now, List<CalEvent>? events)
    {
        bool binsChanged = _dirty || Bins != _lastBins;
        if (binsChanged)
        {
            (_rules, _binErrors) = BinParser.ParseBins(Bins);
            _lastBins = Bins;
        }
        if (_dirty || Reminders != _lastReminders)
        {
            (_reminders, _) = BinParser.ParseReminders(Reminders);
            _lastReminders = Reminders;
        }

        var today = DateOnly.FromDateTime(now);
        var keyword = (CalendarKeyword ?? "").Trim();
        if (_dirty || !ReferenceEquals(events, _lastEvents) || keyword != _lastKeyword || today != _lastDay)
            _extras = ExtrasFrom(events, keyword);

        _lastEvents = events;
        _lastKeyword = CalendarKeyword;
        _lastEvening = EveningHour;
        _lastMinute = now.Hour * 60 + now.Minute;
        _lastDay = today;
        _dirty = false;
        _dateText = now.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        var schedule = new CollectionSchedule(_rules, _extras);

        // Reminder cards win over everything.
        foreach (var reminder in _reminders)
        {
            if (!reminder.IsActive(now)) continue;
            _reminderText = reminder.Text;
            _view = View.Reminder;
            return;
        }

        // Evening before / collection day.
        var due = ChooseDue(schedule, now, today, out bool tomorrow);
        if (due.Count > 0)
        {
            _alertBins = due.ToArray();
            _headline = Headline(due);
            _plateText = !tomorrow ? "TODAY" : now.Hour >= Math.Clamp(EveningHour, 0, 23) ? "TONIGHT" : "TOMORROW";
            _plateColour = !tomorrow ? new Pixel(255, 90, 60) : _plateText == "TONIGHT" ? BinStyles.Amber : new Pixel(120, 200, 255);
            _plate.Text = _plateText;
            _plate.Background = _plateColour;
            _sub = "";
            _view = View.Alert;
            return;
        }

        // Calm summary.
        var next = schedule.NextPerBin(today);
        if (next.Count > 0)
        {
            _rows = next.Take(8).Select(c => Row(c, today)).ToArray();
            int pages = (_rows.Length + RowsPerPage - 1) / RowsPerPage;
            var sig = string.Join('|', _rows.Select(r => r.Name + r.Date));
            _pages = Enumerable.Range(0, pages).Select(i => new SummaryPage(i, sig)).ToArray();
            _view = View.Summary;
            return;
        }

        _rows = [];
        _pages = NoPages;
        _view = View.Message;
        if (_rules.Count == 0 && _binErrors.Count == 0)
        {
            _title = "Bin Day: nothing set up";
            _sub = "Bins: Name|#colour|Mon|2|2026-10-05; ...";
        }
        else if (_rules.Count == 0)
        {
            _title = "Bin Day: check Bins";
            _sub = Truncate("Ignored " + _binErrors[0], 52);
        }
        else
        {
            _title = "No collections found";
            _sub = "";
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    /// <summary>Today's bins until midday (or when nothing is due tomorrow), otherwise tomorrow's.</summary>
    private static List<Collection> ChooseDue(CollectionSchedule schedule, DateTime now, DateOnly today, out bool tomorrow)
    {
        var todays = schedule.On(today);
        var tomorrows = schedule.On(today.AddDays(1));
        tomorrow = false;
        if (todays.Count > 0 && (now.Hour < 12 || tomorrows.Count == 0)) return todays;
        if (tomorrows.Count == 0) return todays;
        tomorrow = true;
        return tomorrows;
    }

    private static string Headline(List<Collection> bins)
    {
        var names = bins.Select(b => b.Name.ToUpperInvariant()).ToList();
        bool hasBinWord = names.Any(n => n.Contains("BIN"));
        string joined = names.Count == 1 ? names[0]
            : string.Join(", ", names.Take(names.Count - 1)) + " AND " + names[^1];
        return "PUT OUT THE " + joined + (hasBinWord ? "" : names.Count == 1 ? " BIN" : " BINS");
    }

    private SummaryRowData Row(Collection c, DateOnly today)
    {
        int days = c.Date.DayNumber - today.DayNumber;
        string rel = days switch { 0 => "today", 1 => "tomorrow", _ => "in " + days + " days" };
        return new SummaryRowData(c.Colour, c.Name, c.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture), rel, days <= 1);
    }

    private List<Collection> ExtrasFrom(List<CalEvent>? events, string keyword)
    {
        if (events is null || keyword.Length == 0) return [];
        var grey = new Pixel(120, 120, 130);
        var zone = Time.LocalTimeZone;
        return events
            .Where(e => e.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(e => new Collection(e.Title, grey, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Start, zone).DateTime)))
            .ToList();
    }

    // ---- calendar feed ----------------------------------------------------------------------------------------------------------

    private void EnsureCalendarPolling()
    {
        if (_data != null || _url.Length == 0 || _http == null || (CalendarKeyword ?? "").Trim().Length == 0) return;
        StartCalendarPolling();
    }

    // Separate method: the lambda's closure would otherwise be allocated on every call of the per-frame check above.
    private void StartCalendarPolling()
    {
        var url = _url.StartsWith("webcal", StringComparison.OrdinalIgnoreCase) ? "https" + _url[6..] : _url;
        var cts = _pollCts = new CancellationTokenSource();
        _data = Poll(TimeSpan.FromMinutes(30), ct => FetchAsync(url, ct), cts.Token);
    }

    private async Task<List<CalEvent>> FetchAsync(string url, CancellationToken ct)
    {
        var text = await _http!.GetStringAsync(url, ct);
        var now = DateTimeOffset.UtcNow;
        return IcsParser.Parse(text, now.AddDays(-1), now.AddDays(60), Time.LocalTimeZone);
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        _url = (configuration["Calendar:IcsUrl"] ?? "").Trim();
        if (Bins.Length == 0 && configuration["BinDay:Bins"] is { Length: > 0 } bins) Bins = bins;
        _pollCts?.Cancel();
        _data = null;
        _dirty = true;
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _pollCts?.Cancel();
        _data = null;
        await base.OnDeactivatedAsync(cancellationToken);
    }

    internal SummaryPage[] Pages => _pages.ToArray();
    internal Pager? Pager => _pager;
    internal string CurrentView => _view.ToString();
    internal string PlateText => _plateText;
    internal string HeadlineText => _headline;

    /// <summary>Test seam: calendar events as if the feed had loaded.</summary>
    internal void UseCalendar(ILiveData<List<CalEvent>> events, string url = "https://example.com/cal.ics")
    {
        _url = url;
        _data = events;
        _dirty = true;
    }
}
