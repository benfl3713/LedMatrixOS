using System.Globalization;
using System.Numerics;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Ambient;

internal enum ChipKind { Weather, Event, Line, Bus }

/// <summary>
/// What the data chips say, derived from the polled feeds. Strings are rebuilt only when a feed delivers something new (or the
/// minute changes), so the per-frame <see cref="Refresh"/> allocates nothing. A chip with no usable data is simply not in <see cref="Visible"/>.
/// </summary>
internal sealed class HomeChips
{
    public const int BandX = 10, BandY = 48, BandWidth = 236, BandHeight = 12;

    public readonly List<ChipKind> Visible = new(4);

    public WeatherKind WeatherKind;
    public bool WeatherDay = true;
    public string WeatherTemp = "", WeatherText = "";

    public string EventWhen = "", EventTitle = "";
    public Pixel EventColor;

    public string LineName = "", LineText = "";
    public Pixel LineColor;

    public string BusRoute = "", BusEta = "";

    private WeatherSnapshot? _weather;
    private CalEvent? _event;
    private long _eventMinute = -1;
    private LineStatus[]? _lines;
    private TflArrival[]? _buses;
    private int _lineCount;

    private static readonly Pixel Amber = new(255, 176, 0), Green = new(0, 200, 90), Blue = new(70, 170, 235);

    public void Refresh(HomePageApp app, DateTimeOffset now)
    {
        Visible.Clear();
        if (app.ShowWeatherChip && RefreshWeather(app.WeatherFeed?.Data.Value)) Visible.Add(ChipKind.Weather);
        if (app.ShowEventChip && RefreshEvent(app.EventFeed?.Data.Value, now)) Visible.Add(ChipKind.Event);
        if (app.ShowLineChip && RefreshLine(app.LineFeed?.Data.Value)) Visible.Add(ChipKind.Line);
        if (app.ShowBusChip && RefreshBus(app.BusFeed?.Data.Value)) Visible.Add(ChipKind.Bus);
    }

    private bool RefreshWeather(WeatherSnapshot? w)
    {
        if (w is null) return false;
        if (!ReferenceEquals(w, _weather))
        {
            _weather = w;
            WeatherKind = w.Kind;
            WeatherDay = w.IsDay;
            WeatherTemp = ((int)Math.Round(w.Temp)).ToString(CultureInfo.InvariantCulture) + (w.Fahrenheit ? "F" : "C");
            WeatherText = WeatherCodes.Describe(w.Code, w.IsDay).ToUpperInvariant();
        }
        return true;
    }

    private bool RefreshEvent(List<CalEvent>? events, DateTimeOffset now)
    {
        if (events is null) return false;
        CalEvent? next = null;
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.AllDay || e.End <= now) continue;
            next = e;
            break;
        }
        if (next is null) return false;

        long minute = now.UtcTicks / TimeSpan.TicksPerMinute;
        if (!ReferenceEquals(next, _event) || minute != _eventMinute)
        {
            _event = next;
            _eventMinute = minute;
            var (text, kind) = CalendarFormat.When(next, now);
            EventWhen = text;
            EventColor = kind switch { WhenKind.Ongoing => Green, WhenKind.Imminent => Amber, WhenKind.Soon => Amber, _ => Blue };
            EventTitle = next.Title;
        }
        return true;
    }

    private bool RefreshLine(LineStatus[]? lines)
    {
        if (lines is null) return false;
        if (!ReferenceEquals(lines, _lines))
        {
            _lines = lines;
            LineStatus? worst = null;
            int count = 0;
            foreach (var s in lines)
            {
                if (!s.Health.NeedsAttention()) continue;
                count++;
                if (worst is null || s.Health > worst.Health) worst = s;
            }
            _lineCount = count;
            if (worst is not null)
            {
                LineName = worst.Name.ToUpperInvariant();
                LineText = (worst.Description.Length > 0 ? worst.Description : worst.Health.ToString()).ToUpperInvariant() + (count > 1 ? $"  +{count - 1} more" : "");
                LineColor = worst.Health.Color();
            }
        }
        return _lineCount > 0;
    }

    private bool RefreshBus(TflArrival[]? arrivals)
    {
        if (arrivals is null) return false;
        if (!ReferenceEquals(arrivals, _buses))
        {
            _buses = arrivals;
            TflArrival? best = null;
            foreach (var a in arrivals)
                if (a.TimeToStation >= 0 && (best is null || a.TimeToStation < best.TimeToStation)) best = a;
            if (best is null) { BusRoute = ""; BusEta = ""; }
            else
            {
                BusRoute = "Route " + best.LineName;
                BusEta = best.TimeToStation < 60 ? "due" : (best.TimeToStation / 60).ToString(CultureInfo.InvariantCulture) + " min";
            }
        }
        return BusRoute.Length > 0;
    }
}

/// <summary>A pager whose pages cross-fade themselves, so the band never paints an opaque transition rectangle over the backdrop.</summary>
internal sealed class ChipPager : Pager
{
    public const float Fade = 0.4f;

    public ChipPager() : base(1, TimeSpan.FromSeconds(6), new CrossfadeTransition { Duration = TimeSpan.FromMilliseconds(Fade * 1000) }) { }

    /// <summary>True while <paramref name="page"/> is the one being left.</summary>
    public bool IsLeaving(Node page) => IsTransitioning && ReferenceEquals(CurrentPage, page);

    protected override void PaintChildren(FrameBuffer frame)
    {
        var kids = Children;
        for (int i = 0; i < kids.Count; i++) kids[i].Paint(frame, ScreenBounds.X, ScreenBounds.Y);
    }
}

/// <summary>One chip: fades and drifts in when it appears, fades out and drifts up when the pager moves on, and centres itself when the clock is centred.</summary>
internal sealed class ChipPage : Panel
{
    private readonly HomeState _s;
    private readonly Node _inner;
    private float _born = -1f, _leaveAt = -1f;

    public ChipPage(HomeState state, Node inner)
    {
        _s = state;
        _inner = inner;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        inner.HAlign = Align.Start;
        inner.VAlign = Align.Center;
        Add(inner);
    }

    public Action? Tick { get; init; }

    public override void Update(FrameContext ctx)
    {
        Tick?.Invoke();
        base.Update(ctx);
        float t = (float)ctx.Time.TotalSeconds;
        if (_born < 0f) _born = t;
        float inA = Gfx.Saturate((t - _born) / ChipPager.Fade);
        float outA = 0f;
        // Bound pages sit in a one-cell grid, which is what the pager actually holds.
        if (Parent is { Parent: ChipPager pager } grid && pager.IsLeaving(grid))
        {
            if (_leaveAt < 0f) _leaveAt = t;
            outA = Gfx.Saturate((t - _leaveAt) / ChipPager.Fade);
        }
        Opacity = Gfx.Smooth(inA) * (1f - Gfx.Smooth(outA));
        _inner.Position = new Vector2(
            MathF.Round((1f - _s.ChipLeft) * MathF.Max(0f, (HomeChips.BandWidth - _inner.DesiredSize.Width) / 2f)),
            MathF.Round((1f - Gfx.Smooth(inA)) * 3f - Gfx.Smooth(outA) * 3f));
    }
}

internal static class ChipNodes
{
    private static readonly Pixel Text = new(228, 238, 250), Muted = new(150, 175, 205), Ink = new(8, 10, 16);

    public static Node Build(ChipKind kind, HomeChips m, HomeState state)
    {
        var shadow = new Pixel(0, 0, 0);
        var big = new TextStyle(Fonts.Small, Text, true, shadow);
        var small = new TextStyle(Fonts.QuiteSmall, Muted, true, shadow);
        var pillStyle = new TextStyle(Fonts.QuiteSmall, Ink, false);

        Stack Row(params Node[] items)
        {
            var row = new Stack(Orientation.Horizontal, gap: 4) { CrossAlign = Align.Center };
            foreach (var n in items) row.Add(n);
            return row;
        }

        switch (kind)
        {
            case ChipKind.Weather:
                return new ChipPage(state, Row(
                    new WeatherGlyph(() => (m.WeatherKind, m.WeatherDay), 12),
                    new Label(() => m.WeatherTemp) { Style = big },
                    new Label(() => m.WeatherText) { Style = small }));

            case ChipKind.Event:
            {
                var when = new Pill("", Muted) { Style = pillStyle, Height = 10 };
                return new ChipPage(state, Row(when, new MarqueeLabel(() => m.EventTitle) { Style = big, Width = 120 }))
                {
                    Tick = () => { when.Text = m.EventWhen; when.Background = m.EventColor; },
                };
            }

            case ChipKind.Line:
            {
                var pill = new Pill("", Muted) { Style = pillStyle, Height = 10 };
                return new ChipPage(state, Row(pill, new MarqueeLabel(() => m.LineText) { Style = big, Width = 120 }))
                {
                    Tick = () => { pill.Text = m.LineName; pill.Background = m.LineColor; },
                };
            }

            default:
                return new ChipPage(state, Row(
                    new Pill("BUS", BusColors.Day) { Style = new TextStyle(Fonts.QuiteSmall, Pixel.White, false), Height = 10 },
                    new Label(() => m.BusRoute) { Style = big },
                    new Label(() => m.BusEta) { Style = new TextStyle(Fonts.Small, new Pixel(255, 190, 90), true, shadow) }));
        }
    }
}
