using BdfFontParser;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Calendar;

/// <summary>Colours and text styles of the calendar. Created in <c>Build</c>, once the fonts are loaded.</summary>
internal sealed class CalendarStyles
{
    public static readonly Pixel Amber = new(255, 190, 40), Green = new(96, 230, 130), Ink = new(225, 225, 225), Muted = new(130, 130, 140);

    public readonly TextStyle Caption = new(Fonts.QuiteSmall, Muted, Shadow: false);
    public readonly TextStyle Title = new(Fonts.Small, Ink, Shadow: false);
    public readonly TextStyle Tiny = new(Fonts.QuiteSmall, Ink, Shadow: false);
    public readonly TextStyle NextTime = new(Fonts.QuiteSmall, Amber.WithBrightness(0.85f), Shadow: false);
    public readonly TextStyle AllDay = new(Fonts.QuiteSmall, Amber, Shadow: false);
}

/// <summary>Text whose colour is evaluated every frame (the hero time pulses, the message breathes), with the optional 1px drop shadow.</summary>
internal sealed class TintedText : Node
{
    private readonly TextRun _run = new();
    private readonly BdfFont _font;
    private readonly Func<string> _text;
    private readonly Func<Pixel> _color;
    private readonly bool _shadow;

    public TintedText(BdfFont font, Func<string> text, Func<Pixel> color, bool shadow = false)
    {
        _font = font;
        _text = text;
        _color = color;
        _shadow = shadow;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_run.Set(_font, _text())) InvalidateLayout();
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        _run.Set(_font, _text());
        return new Size(_run.Width, _run.Height);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds) => _run.Draw(frame, bounds.X, bounds.Y, _color(), _shadow);
}

/// <summary>The coloured spine down the left edge of the hero, tinted like the time.</summary>
internal sealed class Spine : Node
{
    private readonly CalendarModel _model;

    public Spine(CalendarModel model)
    {
        _model = model;
        Width = 2;
        Margin = new Thickness(0, 2);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds) => frame.Fill(bounds, _model.HeroColor.WithBrightness(0.7f));
}

/// <summary>
/// The event on the left: caption, the time to go in big type, the title (a marquee when it is too long), the place and,
/// while the event is happening, a bar of how much of it has passed.
/// </summary>
internal sealed class HeroNode : Stack
{
    private readonly CalendarModel _model;
    private readonly ProgressBar _progress;
    private readonly MarqueeLabel _title, _place;
    private readonly WrapLabel _wrapTitle;

    public HeroNode(CalendarModel model, CalendarStyles styles) : base(Orientation.Vertical)
    {
        _model = model;
        CrossAlign = Align.Stretch;
        Margin = new Thickness(2, 1, 4, 0);

        _progress = new ProgressBar(() => model.Progress)
        {
            Thickness = 3,
            Fill = CalendarStyles.Green,
            Background = new Pixel(20, 36, 26),
            Margin = new Thickness(0, 3, 0, 0),
            Visible = false,
        };

        Add(new Label(() => model.Caption) { Style = styles.Caption });
        Add(new TintedText(Fonts.Big, () => model.When, () => model.HeroColor, shadow: true) { Margin = new Thickness(0, 2, 0, 0) });
        _title = new MarqueeLabel(() => model.Title) { Style = styles.Title, Margin = new Thickness(0, 4, 0, 0) };
        _wrapTitle = new WrapLabel(Fonts.Small, () => model.Title, () => CalendarStyles.Ink, () => model.Place.Length == 0 ? 2 : 1) { Margin = new Thickness(0, 4, 0, 0), Visible = false };
        _place = new MarqueeLabel(() => model.Place) { Style = styles.Caption, Margin = new Thickness(0, 3, 0, 0) };
        Add(_title);
        Add(_wrapTitle);
        Add(_place);
        Add(_progress);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _progress.Visible = _model.ShowProgress;

        // Title Overflow: Scroll marquees a long title, Clip leaves it cut off, Wrap uses a second line when the place line is hidden.
        bool wrap = _model.TitleOverflow == "Wrap";
        _title.Visible = !wrap;
        _wrapTitle.Visible = wrap;
        _title.Speed = _model.TitleOverflow == "Clip" ? 0f : 30f;
        _place.Visible = _model.Place.Length > 0;
    }
}

/// <summary>A following event: when (kept current each minute) over its title, which scrolls if it does not fit.</summary>
internal sealed class EventRow : Stack
{
    public const int RowHeight = 15;
    private const int TitleWidth = 90;

    private readonly CalendarModel _model;
    private CalEvent _event;
    private long _minute = long.MinValue;
    private string _when = "", _title = "";
    private readonly MarqueeLabel _titleLabel;

    public EventRow(CalEvent ev, CalendarModel model, CalendarStyles styles) : base(Orientation.Vertical)
    {
        _model = model;
        _event = ev;
        CrossAlign = Align.Stretch;
        Add(new Label(() => _when) { Style = styles.NextTime });
        _titleLabel = new MarqueeLabel(() => _title) { Style = styles.Tiny, Margin = new Thickness(0, 1, 0, 0) };
        Add(_titleLabel);
        Refresh();
    }

    public void Set(CalEvent ev)
    {
        _event = ev;
        _minute = long.MinValue;
        Refresh();
    }

    public override void Update(FrameContext ctx)
    {
        Refresh();
        base.Update(ctx);
    }

    private void Refresh()
    {
        // Title Overflow here: Scroll marquees, Clip cuts the text off, Wrap shortens it with an ellipsis (a row has no room for a second line).
        _titleLabel.Speed = _model.TitleOverflow == "Clip" ? 0f : 30f;
        if (_minute == _model.RefreshKey) return;
        _minute = _model.RefreshKey;
        _when = CalendarFormat.When(_event, _model.Now, _model.Hour24).Text;
        _title = _model.TitleOverflow == "Wrap" ? Fonts.QuiteSmall.TruncateWithEllipsis(_event.Title, TitleWidth) : _event.Title;
    }
}

/// <summary>
/// The "Today" axis: all-day strip on top, event bars in up to three lanes under it (sized by start and end), hour ticks and labels along the
/// bottom and a marker for the current time. Past events dim and the one in progress turns green. It draws straight onto the frame from
/// <see cref="CalendarModel"/>'s prepared lane layout, so a frame allocates nothing.
/// </summary>
internal sealed class TimelineCanvas : Node
{
    private const int Margin = 6, StripHeight = 3, LaneTop = 13, LaneArea = 38, AxisY = 52, MaxLaneHeight = 16;

    private static readonly Pixel[] Palette = [new(120, 220, 240), new(170, 140, 255), new(255, 190, 40), new(100, 160, 255)];
    private static readonly Pixel Grid = new(18, 18, 26), AxisColor = new(70, 70, 84), LabelColor = new(120, 120, 135), NowColor = new(255, 80, 60);

    private readonly CalendarModel _model;
    private readonly TextRun[] _hours = new TextRun[25];
    private readonly TextRun[] _hours12 = new TextRun[25];

    public TimelineCanvas(CalendarModel model)
    {
        _model = model;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        for (int h = 0; h <= 24; h++)
        {
            _hours[h] = new TextRun();
            _hours[h].Set(Fonts.QuiteSmall, h.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
            _hours12[h] = new TextRun();
            int h12 = h % 12 == 0 ? 12 : h % 12;
            _hours12[h].Set(Fonts.QuiteSmall, h12 + (h is 0 or 24 or < 12 ? "a" : "p"));
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var m = _model;
        int x0 = bounds.X + Margin, w = bounds.Width - Margin * 2, top = bounds.Y;
        float start = m.StartHour, span = Math.Max(1f, m.EndHour - m.StartHour);
        float perHour = w / span;

        DrawAllDayStrip(frame, x0, top, w);

        // hour grid behind the bars
        int first = (int)MathF.Ceiling(start), last = (int)MathF.Floor(m.EndHour);
        for (int h = first; h <= last; h++)
            frame.Fill(new Rectangle(XOf(x0, h, start, perHour), top + LaneTop, 1, LaneArea), Grid);

        // bars
        int lanes = Math.Max(1, m.Lanes);
        int laneH = Math.Min(MaxLaneHeight, (LaneArea - (lanes - 1)) / lanes);
        int laneTop = top + LaneTop + (LaneArea - (lanes * laneH + lanes - 1)) / 2;
        for (int i = 0; i < m.BarCount; i++)
        {
            var bar = m.Bars[i];
            int bx = XOf(x0, bar.StartHour, start, perHour);
            int bw = Math.Max(3, XOf(x0, bar.EndHour, start, perHour) - bx);
            int by = laneTop + bar.Lane * (laneH + 1);
            var rect = new Rectangle(bx, by, bw, laneH);

            bool ongoing = bar.Start <= m.Now && m.Now < bar.End;
            bool past = bar.End <= m.Now;
            var color = ongoing ? CalendarStyles.Green : Palette[i % Palette.Length];
            frame.Fill(rect, past ? color.WithBrightness(0.28f) : color);

            // The title, cut at a whole character when the bar is too short for it (the font is fixed width); bars under two letters stay plain
            int advance = bar.Chars > 0 ? bar.Title.Width / bar.Chars : 1;
            int room = bw - 3;
            int textWidth = room >= bar.Title.Width ? bar.Title.Width : room / Math.Max(1, advance) * Math.Max(1, advance);
            if (textWidth >= advance * 2)
            {
                frame.PushClip(new Rectangle(bx + 2, by, textWidth, laneH));
                bar.Title.Draw(frame, bx + 2, by + (laneH - bar.Title.Height) / 2, past ? color.WithBrightness(0.8f) : Pixel.Black);
                frame.PopClip();
            }
        }

        // axis, ticks and labels
        frame.Fill(new Rectangle(x0, top + AxisY, w + 1, 1), AxisColor);
        int step = perHour >= (m.Hour24 ? 13f : 16f) ? 1 : 2;
        for (int h = first; h <= last; h++)
        {
            int x = XOf(x0, h, start, perHour);
            bool labelled = h % step == 0;
            frame.Fill(new Rectangle(x, top + AxisY + 1, 1, labelled ? 3 : 2), AxisColor);
            if (labelled)
            {
                var run = (m.Hour24 ? _hours : _hours12)[Math.Clamp(h, 0, 24)];
                run.Draw(frame, x - run.Width / 2, top + AxisY + 5, LabelColor);
            }
        }

        // now marker
        float nowHour = (float)m.Now.TimeOfDay.TotalHours;
        if (nowHour >= start && nowHour <= m.EndHour)
        {
            int nx = XOf(x0, nowHour, start, perHour);
            frame.Fill(new Rectangle(nx, top + LaneTop - 2, 1, AxisY - LaneTop + 3), NowColor);
            frame.Fill(new Rectangle(nx - 1, top + LaneTop - 3, 3, 1), NowColor);
        }
    }

    private void DrawAllDayStrip(FrameBuffer frame, int x0, int top, int w)
    {
        int n = Math.Min(_model.AllDayCount, 3);
        if (n == 0) return;
        int segment = (w + 1 - (n - 1) * 2) / n;
        for (int i = 0; i < n; i++)
            frame.Fill(new Rectangle(x0 + i * (segment + 2), top, segment, StripHeight), CalendarStyles.Amber.WithBrightness(0.8f));
    }

    private static int XOf(int x0, float hour, float start, float perHour) => x0 + (int)MathF.Round((hour - start) * perHour);
}

/// <summary>
/// A text block that word-wraps to the width it is given, up to <c>maxLines</c> lines; whatever does not fit ends the last line with an ellipsis.
/// Lines are rebuilt only when the text, the width or the line limit changes.
/// </summary>
internal sealed class WrapLabel : Node
{
    private const int MaxRuns = 3;
    private readonly BdfFont _font;
    private readonly Func<string> _text;
    private readonly Func<Pixel> _color;
    private readonly Func<int> _maxLines;
    private readonly TextRun[] _runs = new TextRun[MaxRuns];
    private string _laidOutText = "\u0001";
    private int _laidOutWidth = -1, _laidOutLines = -1, _count;
    private int _width;

    public WrapLabel(BdfFont font, Func<string> text, Func<Pixel> color, Func<int> maxLines)
    {
        _font = font;
        _text = text;
        _color = color;
        _maxLines = maxLines;
        for (int i = 0; i < MaxRuns; i++) _runs[i] = new TextRun();
    }

    public int LineCount => _count;

    private void Layout(int width)
    {
        string text = _text();
        int lines = Math.Clamp(_maxLines(), 1, MaxRuns);
        if (width == _laidOutWidth && lines == _laidOutLines && string.Equals(text, _laidOutText, StringComparison.Ordinal)) return;
        _laidOutWidth = width;
        _laidOutLines = lines;
        _laidOutText = text;
        _width = width;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int count = 0, i = 0;
        string current = "";
        while (i < words.Length)
        {
            string candidate = current.Length == 0 ? words[i] : current + " " + words[i];
            if (current.Length == 0 || _font.MeasureText(candidate) <= width)
            {
                current = candidate;
                i++;
                continue;
            }
            if (count == lines - 1) break;   // the last line takes everything that is left and is cut with an ellipsis
            _runs[count++].Set(_font, _font.TruncateWithEllipsis(current, width));
            current = "";
        }

        string last = current;
        for (int j = i; j < words.Length; j++) last += (last.Length == 0 ? "" : " ") + words[j];
        if (last.Length > 0) _runs[count++].Set(_font, _font.TruncateWithEllipsis(last, width));
        _count = count;
        for (int k = count; k < MaxRuns; k++) _runs[k].Set(_font, "");
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        Layout(Math.Max(8, availW));
        return new Size(Math.Max(8, availW), Math.Max(1, _count) * _font.BoundingBox.Y);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        string text = _text();
        int lines = Math.Clamp(_maxLines(), 1, MaxRuns);
        if (_width > 0 && (lines != _laidOutLines || !string.Equals(text, _laidOutText, StringComparison.Ordinal)))
        {
            Layout(_width);
            InvalidateLayout();
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var color = _color();
        int lineHeight = _font.BoundingBox.Y;
        for (int i = 0; i < _count; i++) _runs[i].Draw(frame, bounds.X, bounds.Y + i * lineHeight, color);
    }
}
