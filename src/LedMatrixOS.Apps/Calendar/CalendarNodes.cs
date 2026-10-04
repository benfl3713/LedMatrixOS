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
        Add(new MarqueeLabel(() => model.Title) { Style = styles.Title, Margin = new Thickness(0, 4, 0, 0) });
        Add(new MarqueeLabel(() => model.Place) { Style = styles.Caption, Margin = new Thickness(0, 3, 0, 0) });
        Add(_progress);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _progress.Visible = _model.ShowProgress;
    }
}

/// <summary>A following event: when (kept current each minute) over its title, which scrolls if it does not fit.</summary>
internal sealed class EventRow : Stack
{
    public const int RowHeight = 15;

    private readonly CalendarModel _model;
    private CalEvent _event;
    private long _minute = long.MinValue;
    private string _when = "", _title = "";

    public EventRow(CalEvent ev, CalendarModel model, CalendarStyles styles) : base(Orientation.Vertical)
    {
        _model = model;
        _event = ev;
        CrossAlign = Align.Stretch;
        Add(new Label(() => _when) { Style = styles.NextTime });
        Add(new MarqueeLabel(() => _title) { Style = styles.Tiny, Margin = new Thickness(0, 1, 0, 0) });
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
        if (_minute == _model.Minute) return;
        _minute = _model.Minute;
        _when = CalendarFormat.When(_event, _model.Now).Text;
        _title = _event.Title;
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

    public TimelineCanvas(CalendarModel model)
    {
        _model = model;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        for (int h = 0; h <= 24; h++)
        {
            _hours[h] = new TextRun();
            _hours[h].Set(Fonts.QuiteSmall, h.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
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
        int step = perHour >= 13f ? 1 : 2;
        for (int h = first; h <= last; h++)
        {
            int x = XOf(x0, h, start, perHour);
            bool labelled = h % step == 0;
            frame.Fill(new Rectangle(x, top + AxisY + 1, 1, labelled ? 3 : 2), AxisColor);
            if (labelled)
            {
                var run = _hours[Math.Clamp(h, 0, 24)];
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
