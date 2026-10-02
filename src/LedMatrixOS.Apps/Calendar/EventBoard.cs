using System.Globalization;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Calendar;

internal enum WhenKind { Later, Soon, Imminent, Ongoing }

internal static class CalendarFormat
{
    public static (string Text, WhenKind Kind) When(CalEvent e, DateTimeOffset now)
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

        var time = e.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
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

/// <summary>
/// The next event big on the left (when, title, place) and the ones after it in a column on the right. Strings are rebuilt only when the
/// events or the minute change.
/// </summary>
internal sealed class EventBoard : Node
{
    private const int RightWidth = 92;

    private static readonly Pixel Cyan = new(120, 220, 240), Green = new(96, 230, 130), Amber = new(255, 190, 40), Red = new(255, 80, 60), Ink = new(225, 225, 225), Muted = new(130, 130, 140);

    private readonly TextRun _caption = new(), _when = new(), _title = new(), _place = new(), _msgTitle = new(), _msgSub = new();
    private readonly TextRun[] _nextTimes = new TextRun[3], _nextTitles = new TextRun[3];
    private IReadOnlyList<CalEvent> _events = [];
    private DateTimeOffset _now;
    private TimeSpan _time;
    private IReadOnlyList<CalEvent>? _builtFor;
    private long _builtMinute = -1;
    private int _first = -1;
    private WhenKind _kind;
    private BoardMessage _builtMessage = BoardMessage.None;

    public EventBoard()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        for (int i = 0; i < 3; i++) { _nextTimes[i] = new(); _nextTitles[i] = new(); }
    }

    public IReadOnlyList<CalEvent> Events { get => _events; set => _events = value; }
    public DateTimeOffset Now { get => _now; set => _now = value; }
    public BoardMessage Message { get; set; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (Message != BoardMessage.None)
        {
            DrawMessage(frame, bounds);
            return;
        }

        long minute = _now.ToUnixTimeSeconds() / 60;
        if (!ReferenceEquals(_events, _builtFor) || minute != _builtMinute || _builtMessage != BoardMessage.None) Rebuild(minute, bounds);
        if (_first < 0) return;

        float pulse = TubeGfxWave(_time, 0.7);
        var color = _kind switch
        {
            WhenKind.Ongoing => Green,
            WhenKind.Imminent => Pixel.Lerp(Red.WithBrightness(0.6f), Red, pulse),
            WhenKind.Soon => Amber,
            _ => Cyan,
        };

        int x = bounds.X + 4;
        frame.Fill(new Rectangle(bounds.X, bounds.Y + 2, 2, bounds.Height - 4), color.WithBrightness(0.7f));
        _caption.Draw(frame, x, bounds.Y + 1, Muted);
        _when.Draw(frame, x, bounds.Y + 10, color, shadow: true);
        _title.Draw(frame, x, bounds.Y + 32, Ink);
        _place.Draw(frame, x, bounds.Y + 47, Muted);

        int rx = bounds.Right - RightWidth;
        frame.Fill(new Rectangle(rx - 4, bounds.Y + 4, 1, bounds.Height - 8), new Pixel(28, 28, 36));
        int y = bounds.Y + 3;
        for (int i = 0; i < 3 && _nextTitles[i].Width > 0; i++)
        {
            _nextTimes[i].Draw(frame, rx, y, Amber.WithBrightness(0.85f));
            _nextTitles[i].Draw(frame, rx, y + 8, Ink);
            y += 20;
        }
    }

    private static float TubeGfxWave(TimeSpan t, double period) => 0.5f + 0.5f * MathF.Sin((float)(t.TotalSeconds / period * 2 * Math.PI));

    private void Rebuild(long minute, Rectangle bounds)
    {
        _builtFor = _events;
        _builtMinute = minute;
        _builtMessage = BoardMessage.None;
        _first = CalendarFormat.FirstUpcoming(_events, _now);
        if (_first < 0) return;

        var ev = _events[_first];
        var (text, kind) = CalendarFormat.When(ev, _now);
        _kind = kind;
        int leftRoom = bounds.Width - RightWidth - 12;

        _caption.Set(Fonts.QuiteSmall, kind == WhenKind.Ongoing ? "HAPPENING" : _first == 0 ? "NEXT UP" : "COMING UP");
        _when.Set(Fonts.Big, text);
        _title.Set(Fonts.Small, Fonts.Small.TruncateWithEllipsis(ev.Title, leftRoom));
        _place.Set(Fonts.QuiteSmall, ev.Location is { } loc ? Fonts.QuiteSmall.TruncateWithEllipsis(loc, leftRoom) : ev.AllDay ? "ALL DAY" : EndText(ev));

        for (int i = 0; i < 3; i++)
        {
            int idx = _first + 1 + i;
            if (idx < _events.Count)
            {
                var (t, _) = CalendarFormat.When(_events[idx], _now);
                _nextTimes[i].Set(Fonts.QuiteSmall, t);
                _nextTitles[i].Set(Fonts.QuiteSmall, Fonts.QuiteSmall.TruncateWithEllipsis(_events[idx].Title, RightWidth));
            }
            else
            {
                _nextTimes[i].Set(Fonts.QuiteSmall, "");
                _nextTitles[i].Set(Fonts.QuiteSmall, "");
            }
        }
    }

    private static string EndText(CalEvent e) => "until " + e.End.ToString("HH:mm", CultureInfo.InvariantCulture);

    private void DrawMessage(FrameBuffer frame, Rectangle bounds)
    {
        if (_builtMessage != Message)
        {
            _builtMessage = Message;
            _builtFor = null;
            var (title, sub) = Message switch
            {
                BoardMessage.NotConfigured => ("NO CALENDAR", "Set Calendar:IcsUrl"),
                BoardMessage.Loading => ("LOADING", "Fetching your calendar"),
                BoardMessage.Offline => ("NO DATA", "Calendar unreachable, retrying"),
                _ => ("ALL CLEAR", "Nothing coming up"),
            };
            _msgTitle.Set(Fonts.Big, title);
            _msgSub.Set(Fonts.Small, sub);
        }

        float breathe = TubeGfxWave(_time, 2.4);
        var color = Message == BoardMessage.Empty ? Green : Message == BoardMessage.Offline ? Red : Amber;
        int top = bounds.Y + (bounds.Height - _msgTitle.Height - 4 - _msgSub.Height) / 2;
        _msgTitle.Draw(frame, bounds.X + (bounds.Width - _msgTitle.Width) / 2, top, color.WithBrightness(0.7f + 0.3f * breathe), shadow: true);
        _msgSub.Draw(frame, bounds.X + (bounds.Width - _msgSub.Width) / 2, top + _msgTitle.Height + 4, Muted);
    }
}
