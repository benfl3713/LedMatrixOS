using System.Globalization;
using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Apps.Commute;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Briefing;

/// <summary>The cards of the briefing, in the order they are shown.</summary>
internal enum BriefingPage { Greeting, Weather, Calendar, Commute, Bins, SignOff }

/// <summary>Everything the model derives its text and page list from. A struct, so passing it each frame allocates nothing.</summary>
internal readonly record struct BriefingInputs(
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    TimeSpan FrameTime,
    WeatherSnapshot? Weather,
    List<CalEvent>? Events,
    TflArrival[]? Arrivals,
    LineStatus[]? Statuses,
    bool ShowWeather,
    bool ShowCalendar,
    bool ShowCommute,
    bool ShowBins,
    string StationId,
    int WalkMinutes,
    string Bins,
    string GreetingName = "",
    int ActiveFrom = 5,
    int ActiveUntil = 12,
    string CardOrder = BriefingGreeting.DefaultOrder);

internal static class BriefingGreeting
{
    public const string DefaultOrder = "Weather, Calendar, Commute, Bins";

    /// <summary>The longest name that still fits next to the weather glyph on the greeting card; longer names are cut.</summary>
    public const int MaxNameLength = 10;

    private static readonly BriefingPage[] DefaultPages = [BriefingPage.Weather, BriefingPage.Calendar, BriefingPage.Commute, BriefingPage.Bins];

    public static string ForHour(int hour) => hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";

    /// <summary>"Good morning" or "Good morning, Ben" (the name is trimmed and cut to <see cref="MaxNameLength"/>).</summary>
    public static string Greeting(int hour, string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length > MaxNameLength) n = n[..MaxNameLength].TrimEnd();
        return n.Length == 0 ? ForHour(hour) : ForHour(hour) + ", " + n;
    }

    /// <summary>
    /// True when <paramref name="hour"/> (0-23) is inside the window from <paramref name="from"/> (inclusive) to <paramref name="until"/> (exclusive).
    /// A window that ends before it starts wraps over midnight (22 to 4); equal values mean always.
    /// </summary>
    public static bool InWindow(int hour, int from, int until)
    {
        from = Math.Clamp(from, 0, 24) % 24;
        until = Math.Clamp(until, 0, 24) % 24;
        if (from == until) return true;
        return from < until ? hour >= from && hour < until : hour >= from || hour < until;
    }

    /// <summary>
    /// The movable cards in the order given by a comma separated list of Weather, Calendar, Commute and Bins (any case). Unknown or repeated
    /// names are ignored and cards the list leaves out follow in the default order, so the Show switches stay the only way to hide a card.
    /// </summary>
    public static BriefingPage[] ParseOrder(string? order)
    {
        var result = new List<BriefingPage>(4);
        foreach (var part in (order ?? "").Split([',', ';', '>', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            BriefingPage? page = part.ToLowerInvariant() switch
            {
                "weather" => BriefingPage.Weather,
                "calendar" => BriefingPage.Calendar,
                "commute" => BriefingPage.Commute,
                "bins" => BriefingPage.Bins,
                _ => null,
            };
            if (page is { } p && !result.Contains(p)) result.Add(p);
        }

        foreach (var p in DefaultPages)
            if (!result.Contains(p)) result.Add(p);
        return result.ToArray();
    }
}

/// <summary>
/// What the briefing cards say, and which cards exist. A card whose data is missing (or whose switch is off) is left out of
/// <see cref="Pages"/>, so no card is ever shown empty. Strings are rebuilt only when a feed delivers something new, a setting changes or the
/// minute rolls over (the commute card follows the frame clock), so the per-frame <see cref="Refresh"/> allocates nothing.
/// </summary>
internal sealed class BriefingModel
{
    public const int MaxEvents = 3;
    public const int MaxBins = 4;
    public const int RainHours = 12;

    private static readonly Pixel Green = new(0, 200, 90), Amber = new(255, 176, 0), Red = new(255, 80, 60);

    /// <summary>The cards to show, in order. Changes only when the set of available cards changes.</summary>
    public readonly List<BriefingPage> Pages = new(6);

    // greeting
    public string GreetingText = "", DateText = "", TempText = "";
    /// <summary>True when the greeting is too long for the big font (a name was added), so the card uses the small one.</summary>
    public bool GreetingSmall;
    public bool HasWeather;
    public WeatherKind WeatherKind;
    public bool WeatherDay = true;

    // weather
    public string HighLowText = "", WeatherDescText = "", RainText = "";
    public float[] RainValues = [];

    // calendar
    public int EventCount;
    public readonly string[] EventTimes = new string[MaxEvents], EventTitles = new string[MaxEvents];
    public string NothingText = "";

    // commute
    public string LeaveText = "", TrainText = "", LineName = "", LineText = "";
    public Pixel LeaveColor = Green, LineColor = Green;
    public bool HasLineStatus;

    // bins
    public int BinCount;
    public readonly Pixel[] BinColors = new Pixel[MaxBins];
    public string BinHeadline = "", BinNames = "", BinSecond = "";

    private readonly DepartureBoardModel _board = new();
    private readonly List<BriefingPage> _scratch = new(6);
    private string? _bins;
    private CollectionSchedule _schedule = new([]);
    private bool _hasBinRules;
    private bool _binsDue;

    private DateOnly _day;
    private long _minute = -1;
    private int _hour = -1;
    private WeatherSnapshot? _weather;
    private List<CalEvent>? _events;
    private LineStatus[]? _statuses;
    private TflArrival[]? _arrivals;
    private bool _showWeather, _showCalendar, _showBins;
    private bool _eventsOk, _weatherOk;
    private string? _name, _orderText;
    private BriefingPage[] _order = [BriefingPage.Weather, BriefingPage.Calendar, BriefingPage.Commute, BriefingPage.Bins];

    public BriefingModel()
    {
        for (int i = 0; i < MaxEvents; i++) EventTimes[i] = EventTitles[i] = "";
    }

    public CommutePlan Plan { get; private set; } = CommutePlan.None;

    public void Reset()
    {
        _day = default;
        _minute = -1;
        _hour = -1;
        _weather = null;
        _events = null;
        _statuses = null;
        _arrivals = null;
        _bins = null;
        _name = null;
        _lastTrain = null;
        _lastMinutes = -1;
        Pages.Clear();
        _board.Reset();
    }

    public void Refresh(in BriefingInputs i)
    {
        var day = DateOnly.FromDateTime(i.Now.DateTime);
        long minute = day.DayNumber * 1440L + i.Now.Hour * 60 + i.Now.Minute;

        bool changed = minute != _minute || !ReferenceEquals(i.Weather, _weather) || !ReferenceEquals(i.Events, _events)
            || !ReferenceEquals(i.Statuses, _statuses) || i.ShowWeather != _showWeather || i.ShowCalendar != _showCalendar
            || i.ShowBins != _showBins || !string.Equals(i.Bins, _bins, StringComparison.Ordinal)
            || !string.Equals(i.GreetingName, _name, StringComparison.Ordinal);

        if (changed)
        {
            if (day != _day || i.Now.Hour != _hour || !string.Equals(i.GreetingName, _name, StringComparison.Ordinal))
            {
                GreetingText = BriefingGreeting.Greeting(i.Now.Hour, i.GreetingName);
                GreetingSmall = GreetingText.Length > 17;
                DateText = i.Now.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
                _day = day;
                _hour = i.Now.Hour;
            }

            RefreshWeather(i);
            RefreshEvents(i);
            RefreshLines(i.Statuses);
            RefreshBins(i, day);
            _minute = minute;
            _weather = i.Weather;
            _events = i.Events;
            _statuses = i.Statuses;
            _showWeather = i.ShowWeather;
            _showCalendar = i.ShowCalendar;
            _showBins = i.ShowBins;
            _bins = i.Bins;
            _name = i.GreetingName;
        }

        if (!string.Equals(i.CardOrder, _orderText, StringComparison.Ordinal))
        {
            _orderText = i.CardOrder;
            _order = BriefingGreeting.ParseOrder(i.CardOrder);
        }

        RefreshCommute(i);
        RefreshPages(i);
    }

    // ---- weather ------------------------------------------------------------------------------------------------------------------

    private void RefreshWeather(in BriefingInputs i)
    {
        var w = i.Weather;
        _weatherOk = i.ShowWeather && w is not null;
        HasWeather = _weatherOk;
        if (w is null) return;

        WeatherKind = w.Kind;
        WeatherDay = w.IsDay;
        string unit = w.Fahrenheit ? "F" : "C";
        TempText = ((int)Math.Round(w.Temp)).ToString(CultureInfo.InvariantCulture) + unit;
        HighLowText = "H" + ((int)Math.Round(w.High)).ToString(CultureInfo.InvariantCulture) + "  L" + ((int)Math.Round(w.Low)).ToString(CultureInfo.InvariantCulture) + unit;
        WeatherDescText = WeatherCodes.Describe(w.Code, w.IsDay);
        RainText = "Rain chance " + w.PrecipChance.ToString(CultureInfo.InvariantCulture) + "%";

        // The next RainHours forecast hours, starting with the hour we are in.
        var hourly = w.Hourly;
        int start = 0;
        var hourStart = i.Now.DateTime.Date.AddHours(i.Now.Hour);
        while (start < hourly.Count && hourly[start].LocalTime < hourStart) start++;
        if (start >= hourly.Count) start = 0;
        int n = Math.Min(RainHours, hourly.Count - start);
        var values = new float[Math.Max(0, n)];
        for (int k = 0; k < n; k++) values[k] = hourly[start + k].PrecipChance;
        RainValues = values;
    }

    // ---- calendar -----------------------------------------------------------------------------------------------------------------

    private void RefreshEvents(in BriefingInputs i)
    {
        _eventsOk = false;
        EventCount = 0;
        for (int k = 0; k < MaxEvents; k++) EventTimes[k] = EventTitles[k] = "";
        var events = i.Events;
        if (!i.ShowCalendar || events is null) return;

        _eventsOk = true;
        var today = i.Now.Date;

        // Timed events first (in feed order), then all-day ones if there is room.
        for (int pass = 0; pass < 2 && EventCount < MaxEvents; pass++)
        {
            foreach (var e in events)
            {
                if (EventCount >= MaxEvents) break;
                if (e.AllDay != (pass == 1)) continue;
                if (TimeZoneInfo.ConvertTime(e.Start, i.Zone).Date != today && !(e.AllDay && e.Start.Date == today)) continue;
                if (!e.AllDay && e.End <= i.Now) continue;
                EventTimes[EventCount] = e.AllDay ? "ALL DAY" : TimeZoneInfo.ConvertTime(e.Start, i.Zone).ToString("HH:mm", CultureInfo.InvariantCulture);
                EventTitles[EventCount] = e.Title;
                EventCount++;
            }
        }

        NothingText = EventCount == 0 ? "Nothing on today" : "";
    }

    // ---- commute ------------------------------------------------------------------------------------------------------------------

    private void RefreshLines(LineStatus[]? lines)
    {
        HasLineStatus = lines is { Length: > 0 };
        if (!HasLineStatus) { LineName = LineText = ""; return; }

        LineStatus? worst = null;
        foreach (var s in lines!)
            if (worst is null || s.Health > worst.Health) worst = s;

        if (worst!.Health.NeedsAttention())
        {
            LineName = worst.Name.ToUpperInvariant();
            LineText = worst.Description.Length > 0 ? worst.Description : worst.Health.ToString();
            LineColor = worst.Health.Color();
        }
        else
        {
            LineName = "LINES";
            LineText = "Good service on all lines";
            LineColor = Green;
        }
    }

    private void RefreshCommute(in BriefingInputs i)
    {
        if (!i.ShowCommute || string.IsNullOrWhiteSpace(i.StationId)) { Plan = CommutePlan.None; return; }

        _board.Refresh(i.FrameTime, i.Arrivals, "", 12);
        Plan = CommutePlanner.Plan(_board.Visible, i.FrameTime, i.WalkMinutes);
        if (_board.Visible.Count == 0) return;

        var plan = Plan;
        int minutes = plan.LeaveInSeconds <= CommutePlanner.NowThresholdSeconds ? 0 : Math.Max(1, plan.LeaveInSeconds / 60);
        if (_lastTrain == plan.Train && _lastMinutes == minutes && _lastMissed == plan.AllMissed && _lastWalk == i.WalkMinutes) return;
        _lastTrain = plan.Train;
        _lastMinutes = minutes;
        _lastMissed = plan.AllMissed;
        _lastWalk = i.WalkMinutes;

        if (plan.Train is null)
        {
            LeaveText = plan.AllMissed ? "Next train too soon" : "No trains";
            TrainText = plan.AllMissed ? "You will miss the next one" : "";
            LeaveColor = Amber;
            return;
        }

        LeaveText = minutes == 0 ? "Leave now" : "Leave in " + minutes.ToString(CultureInfo.InvariantCulture) + " min";
        string line = plan.Train.LineName.Length > 0 ? plan.Train.LineName + " to " : "To ";
        TrainText = line + plan.Train.Destination + ", " + i.WalkMinutes.ToString(CultureInfo.InvariantCulture) + " min walk";
        LeaveColor = plan.Urgency switch { Urgency.Now => Red, Urgency.Soon => Amber, _ => Green };
    }

    private Departure? _lastTrain;
    private int _lastMinutes = -1, _lastWalk = -1;
    private bool _lastMissed;

    // ---- bins ---------------------------------------------------------------------------------------------------------------------

    private void RefreshBins(in BriefingInputs i, DateOnly today)
    {
        if (!string.Equals(i.Bins, _bins, StringComparison.Ordinal) || _bins is null)
        {
            var (rules, _) = BinParser.ParseBins(i.Bins);
            _hasBinRules = rules.Count > 0;
            _schedule = new CollectionSchedule(rules);
        }

        _binsDue = false;
        BinCount = 0;
        if (!i.ShowBins || !_hasBinRules) return;

        var todays = _schedule.On(today);
        var tomorrows = _schedule.On(today.AddDays(1));
        if (todays.Count == 0 && tomorrows.Count == 0) return;

        _binsDue = true;
        var primary = todays.Count > 0 ? todays : tomorrows;
        BinCount = Math.Min(MaxBins, primary.Count);
        for (int k = 0; k < BinCount; k++) BinColors[k] = primary[k].Colour;
        BinHeadline = todays.Count > 0 ? "Bins out today" : "Bins out tomorrow";
        BinNames = string.Join(", ", primary.Select(c => c.Name));
        BinSecond = todays.Count > 0 && tomorrows.Count > 0 ? "Tomorrow: " + string.Join(", ", tomorrows.Select(c => c.Name)) : "";
    }

    // ---- pages --------------------------------------------------------------------------------------------------------------------

    private void RefreshPages(in BriefingInputs i)
    {
        _scratch.Clear();
        // The greeting card belongs to the active hours; outside them the briefing still plays, from the first data card.
        if (BriefingGreeting.InWindow(i.Now.Hour, i.ActiveFrom, i.ActiveUntil)) _scratch.Add(BriefingPage.Greeting);
        foreach (var page in _order)
        {
            bool available = page switch
            {
                BriefingPage.Weather => _weatherOk,
                BriefingPage.Calendar => _eventsOk,
                BriefingPage.Commute => i.ShowCommute && !string.IsNullOrWhiteSpace(i.StationId) && _board.Visible.Count > 0,
                BriefingPage.Bins => _binsDue,
                _ => false,
            };
            if (available) _scratch.Add(page);
        }

        _scratch.Add(BriefingPage.SignOff);

        bool same = _scratch.Count == Pages.Count;
        for (int k = 0; same && k < _scratch.Count; k++) same = _scratch[k] == Pages[k];
        if (same) return;
        Pages.Clear();
        Pages.AddRange(_scratch);
    }
}
