using System.Globalization;
using System.Text;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Calendar;

internal enum WhenKind { Later, Soon, Imminent, Ongoing }

internal static class CalendarFormat
{
    /// <summary>A clock time as HH:mm (24-hour) or h:mm AM/PM.</summary>
    public static string TimeText(DateTimeOffset t, bool hour24) =>
        t.ToString(hour24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);

    public static (string Text, WhenKind Kind) When(CalEvent e, DateTimeOffset now, bool hour24 = true)
    {
        bool ongoing = e.Start <= now && now < e.End;
        var today = now.Date;
        var day = e.Start.Date;

        if (e.AllDay)
        {
            if (ongoing || day == today) return ("TODAY", ongoing ? WhenKind.Ongoing : WhenKind.Later);
            return (day == today.AddDays(1) ? "TOMORROW" : e.Start.ToString("ddd d MMM", CultureInfo.InvariantCulture).ToUpperInvariant(), WhenKind.Later);
        }

        if (ongoing) return ("NOW", WhenKind.Ongoing);

        int minutes = (int)Math.Ceiling((e.Start - now).TotalMinutes);
        if (minutes < 60) return ($"{Math.Max(1, minutes)} MIN", minutes <= 5 ? WhenKind.Imminent : minutes <= 15 ? WhenKind.Soon : WhenKind.Later);

        var time = TimeText(e.Start, hour24);
        if (day == today) return (time, WhenKind.Later);
        if (day == today.AddDays(1)) return ("TMRW " + time, WhenKind.Later);
        return (e.Start.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant() + " " + time, WhenKind.Later);
    }

    /// <summary>Index of the first event that has not finished, or -1.</summary>
    public static int FirstUpcoming(IReadOnlyList<CalEvent> events, DateTimeOffset now)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].End > now) return i;
        return -1;
    }
}

internal enum BoardMessage { None, NotConfigured, Loading, Offline, Empty }

/// <summary>The pages the calendar can show: the next-event board and the today timeline.</summary>
internal enum CalPage { Board, Timeline }

/// <summary>One event on the timeline page: its span in hours since midnight (clipped to today), the lane it sits in and its title.</summary>
internal sealed class TimelineBar
{
    public readonly TextRun Title = new();
    public float StartHour, EndHour;
    public int Lane, Chars;
    public DateTimeOffset Start, End;
}

/// <summary>
/// Everything the calendar widgets display, derived from the events and the clock. The strings and lane layout are rebuilt only when the
/// events, the minute or the day change, so the per-frame path (progress, colours) does not allocate.
/// </summary>
internal sealed class CalendarModel
{
    public const int MaxLanes = 3;
    public const int MaxBars = 24;
    public const int NextCount = 3;
    public const double PageSeconds = 6;

    private static readonly Pixel Cyan = new(120, 220, 240), Green = new(96, 230, 130), Amber = new(255, 190, 40), Red = new(255, 80, 60);

    private IReadOnlyList<CalEvent> _events = [];
    private IReadOnlyList<CalEvent>? _builtFor;
    private long _builtMinute = -1;
    private int _builtPage = -1;
    private IReadOnlyList<CalEvent>? _timelineFor;
    private DateTime _timelineDay;
    private readonly StringBuilder _names = new();
    private readonly float[] _laneEnd = new float[MaxLanes];

    // ---- inputs ---------------------------------------------------------------------------------------------------------------

    public IReadOnlyList<CalEvent> Events => _events;
    public DateTimeOffset Now { get; private set; }
    public TimeSpan Time { get; private set; }
    public BoardMessage Message { get; private set; }

    /// <summary>Whole minutes since the epoch of <see cref="Now"/>; rows use it to refresh their wording once a minute.</summary>
    public long Minute { get; private set; }

    // ---- display options (set by the app each frame; a change rebuilds the strings) ------------------------------------------

    public int MaxEvents { get; private set; } = NextCount + 1;
    public bool Hour24 { get; private set; } = true;
    public bool ShowLocation { get; private set; } = true;
    public string TitleOverflow { get; private set; } = "Scroll";

    /// <summary>Bumped when a display option changes, so rows know to rewrite their text.</summary>
    public int Revision { get; private set; }

    /// <summary>Changes once a minute or when an option changes: rows refresh their wording when it does.</summary>
    public long RefreshKey => Minute * 1000 + Revision;

    public void Configure(int maxEvents, bool hour24, bool showLocation, string titleOverflow)
    {
        maxEvents = Math.Clamp(maxEvents, 1, 8);
        if (maxEvents == MaxEvents && hour24 == Hour24 && showLocation == ShowLocation && titleOverflow == TitleOverflow) return;
        MaxEvents = maxEvents;
        Hour24 = hour24;
        ShowLocation = showLocation;
        TitleOverflow = titleOverflow;
        Revision++;
        _builtFor = null;
        _timelineFor = null;
    }

    // ---- board ----------------------------------------------------------------------------------------------------------------

    public int First { get; private set; } = -1;
    public WhenKind Kind { get; private set; }
    public string Caption { get; private set; } = "";
    public string When { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Place { get; private set; } = "";
    public IReadOnlyList<CalEvent> Next { get; private set; } = [];

    /// <summary>Elapsed share (0-1) of the event happening now; only meaningful while <see cref="ShowProgress"/>.</summary>
    public float Progress { get; private set; }
    public bool ShowProgress { get; private set; }

    /// <summary>Colour of the hero time and the spine: green while happening, pulsing red when imminent, amber when soon, cyan otherwise.</summary>
    public Pixel HeroColor { get; private set; } = Cyan;

    public string MessageTitle { get; private set; } = "";
    public string MessageSub { get; private set; } = "";
    public Pixel MessageColor { get; private set; }

    // ---- timeline -------------------------------------------------------------------------------------------------------------

    public readonly TimelineBar[] Bars = CreateBars();
    public int BarCount { get; private set; }
    public int Lanes { get; private set; } = 1;
    public float StartHour { get; private set; } = 7;
    public float EndHour { get; private set; } = 23;
    public int AllDayCount { get; private set; }
    public string AllDayText { get; private set; } = "";
    public string TodayText { get; private set; } = "";

    private static TimelineBar[] CreateBars()
    {
        var bars = new TimelineBar[MaxBars];
        for (int i = 0; i < bars.Length; i++) bars[i] = new TimelineBar();
        return bars;
    }

    public void Update(IReadOnlyList<CalEvent> events, DateTimeOffset now, TimeSpan time, BoardMessage message)
    {
        _events = events;
        Now = now;
        Time = time;
        Message = message;
        Minute = now.ToUnixTimeSeconds() / 60;

        if (!ReferenceEquals(events, _builtFor) || Minute != _builtMinute || PageAt(time) != _builtPage) RebuildBoard();
        if (!ReferenceEquals(events, _timelineFor) || now.Date != _timelineDay) RebuildTimeline();

        if (First >= 0)
        {
            var ev = events[First];
            ShowProgress = Kind == WhenKind.Ongoing && !ev.AllDay && ev.End > ev.Start;
            Progress = ShowProgress ? (float)Math.Clamp((now - ev.Start).TotalSeconds / (ev.End - ev.Start).TotalSeconds, 0, 1) : 0f;
            HeroColor = Kind switch
            {
                WhenKind.Ongoing => Green,
                WhenKind.Imminent => Pixel.Lerp(Red.WithBrightness(0.6f), Red, TubeGfx.Wave(time, 0.7)),
                WhenKind.Soon => Amber,
                _ => Cyan,
            };
        }
        else ShowProgress = false;

        if (message != BoardMessage.None)
        {
            (MessageTitle, MessageSub) = message switch
            {
                BoardMessage.NotConfigured => ("NO CALENDAR", "Set Calendar:IcsUrl"),
                BoardMessage.Loading => ("LOADING", "Fetching your calendar"),
                BoardMessage.Offline => ("NO DATA", "Calendar unreachable, retrying"),
                _ => ("ALL CLEAR", "Nothing coming up"),
            };
            var color = message == BoardMessage.Empty ? Green : message == BoardMessage.Offline ? Red : Amber;
            MessageColor = color.WithBrightness(0.7f + 0.3f * TubeGfx.Wave(time, 2.4));
        }
    }

    // The following events show three at a time; with more than three (Max Events above 4) the window moves on every few seconds.
    private int PageAt(TimeSpan time)
    {
        int shown = Math.Min(MaxEvents - 1, Math.Max(0, _events.Count - Math.Max(First, 0) - 1));
        int pages = Math.Max(1, (shown + NextCount - 1) / NextCount);
        return pages <= 1 ? 0 : (int)(time.TotalSeconds / PageSeconds) % pages;
    }

    private void RebuildBoard()
    {
        _builtFor = _events;
        _builtMinute = Minute;
        First = CalendarFormat.FirstUpcoming(_events, Now);
        if (First < 0)
        {
            Caption = When = Title = Place = "";
            Next = [];
            return;
        }

        var ev = _events[First];
        var (text, kind) = CalendarFormat.When(ev, Now, Hour24);
        Kind = kind;
        Caption = kind == WhenKind.Ongoing ? "HAPPENING" : First == 0 ? "NEXT UP" : "COMING UP";
        When = text;
        Title = ev.Title;
        Place = !ShowLocation ? "" : ev.Location is { } loc ? loc : ev.AllDay ? "ALL DAY" : "until " + CalendarFormat.TimeText(ev.End, Hour24);

        int shown = Math.Min(MaxEvents - 1, _events.Count - First - 1);
        int page = PageAt(Time);
        _builtPage = page;
        var next = new List<CalEvent>(NextCount);
        for (int i = 1 + page * NextCount; i <= shown && i <= (page + 1) * NextCount; i++) next.Add(_events[First + i]);
        Next = next;
    }

    private void RebuildTimeline()
    {
        _timelineFor = _events;
        var day = _timelineDay = Now.Date;
        TodayText = Now.ToString("ddd d MMM", CultureInfo.InvariantCulture).ToUpperInvariant();

        BarCount = 0;
        AllDayCount = 0;
        _names.Clear();
        Array.Fill(_laneEnd, -1f);
        float startH = 7f, endH = 23f;
        int lanes = 1;

        foreach (var e in _events)
        {
            if (e.AllDay)
            {
                if (e.Start.Date <= day && e.End.Date > day)
                {
                    AllDayCount++;
                    if (_names.Length > 0) _names.Append(", ");
                    _names.Append(e.Title);
                }
                continue;
            }

            bool today = e.Start.Date <= day && (e.End.Date > day || (e.End.Date == day && (e.End.TimeOfDay > TimeSpan.Zero || e.End == e.Start)));
            if (!today || BarCount >= MaxBars) continue;

            float sh = e.Start.Date < day ? 0f : (float)e.Start.TimeOfDay.TotalHours;
            float eh = e.End.Date > day ? 24f : (float)e.End.TimeOfDay.TotalHours;
            if (eh < sh) eh = sh;
            startH = Math.Min(startH, MathF.Floor(sh));
            endH = Math.Max(endH, MathF.Ceiling(eh));

            int lane = -1;
            for (int l = 0; l < MaxLanes && lane < 0; l++)
                if (_laneEnd[l] <= sh) lane = l;
            if (lane < 0)
            {
                lane = 0;
                for (int l = 1; l < MaxLanes; l++)
                    if (_laneEnd[l] < _laneEnd[lane]) lane = l;
            }
            _laneEnd[lane] = Math.Max(_laneEnd[lane], eh);
            lanes = Math.Max(lanes, lane + 1);

            var bar = Bars[BarCount++];
            bar.StartHour = sh;
            bar.EndHour = eh;
            bar.Lane = lane;
            bar.Start = e.Start;
            bar.End = e.End;
            bar.Title.Set(Fonts.QuiteSmall, e.Title);
            bar.Chars = e.Title.Length;
        }

        StartHour = startH;
        EndHour = endH;
        Lanes = lanes;
        AllDayText = _names.Length > 0 ? _names.ToString() : BarCount == 0 ? "Nothing scheduled" : "";
    }
}
