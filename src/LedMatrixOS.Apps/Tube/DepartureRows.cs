using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Tube;

/// <summary>Fonts and text styles of the departures board. Created in <c>Build</c>, once the fonts are loaded.</summary>
internal sealed class BoardStyles
{
    public readonly TextStyle Big = new(Fonts.Big, new Pixel(245, 245, 245));
    public readonly TextStyle BigAmber = new(Fonts.Big, TubeGfx.Amber);
    public readonly TextStyle Small = new(Fonts.Small, TubeGfx.Ink, Shadow: false);
    public readonly TextStyle SmallAmber = new(Fonts.Small, TubeGfx.Amber, Shadow: false);
    public readonly TextStyle Tiny = new(Fonts.QuiteSmall, TubeGfx.Muted, Shadow: false);
    public readonly TextStyle TinyAmber = new(Fonts.QuiteSmall, TubeGfx.Amber, Shadow: false);
    public readonly TextStyle Strip = new(Fonts.QuiteSmall, new Pixel(190, 190, 200), Shadow: false);
    public readonly TextStyle Clock = new(Fonts.Small, new Pixel(245, 245, 245), Shadow: false);

    /// <summary>Text colour for a row when "colour departures by line" is on: the line colour, lifted so it stays legible.</summary>
    public static Pixel Tinted(Pixel line) => TubeColors.Lift(line, 170f);
}

/// <summary>
/// The next train, shown big: a line-coloured glow that fades to black, platform badge, destination, and the minutes as a rolling number.
/// Under a minute the number gives way to a flashing DUE plate and the glow pulses.
/// </summary>
internal sealed class HeroRow : Stack
{
    public const int RowHeight = 23;

    private readonly Departure _dep;
    private readonly DepartureBoardModel _model;
    private readonly Func<bool> _byLine;
    private readonly BoardStyles _styles;
    private readonly MarqueeLabel _destination;
    private readonly RollingNumber _minutes;
    private readonly Stack _countdown;
    private readonly DueBadge _due;
    private readonly Label _unit;
    private bool _tinted;
    private bool _isDue;
    private TimeSpan _time;

    public HeroRow(Departure dep, DepartureBoardModel model, BoardStyles styles, Func<bool> byLine) : base(Orientation.Horizontal)
    {
        _dep = dep;
        _model = model;
        _styles = styles;
        _byLine = byLine;
        CrossAlign = Align.Center;

        _destination = new MarqueeLabel(dep.Destination) { Style = styles.Big, Grow = 1, Margin = new Thickness(7, 0, 4, 0) };
        _minutes = new RollingNumber(() => dep.Minutes(model.Now)) { Style = styles.BigAmber };
        _unit = new Label("min") { Style = styles.TinyAmber, VAlign = Align.End, Margin = new Thickness(2, 0, 0, 4) };
        _countdown = new Stack(Orientation.Horizontal) { Children = { _minutes, _unit } };
        _due = new DueBadge(Fonts.Big, filled: true) { Visible = false };

        Add(new Block(dep.Color, width: 4));
        if (dep.PlatformNumber.Length > 0)
        {
            Add(new Pill(dep.PlatformNumber, new Pixel(235, 235, 240))
            {
                Style = new TextStyle(Fonts.Small, Pixel.Black, Shadow: false),
                Width = 15,
                Height = 15,
                Radius = 3,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
            });
        }
        else _destination.Margin = new Thickness(8, 0, 4, 0);

        Add(_destination);
        Add(new Panel { Width = 66, Margin = new Thickness(0, 0, 4, 0), Children = { _countdown, _due } });
        _countdown.HAlign = Align.End;
        _countdown.VAlign = Align.Center;
        _due.HAlign = Align.End;
        _due.VAlign = Align.Center;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;

        bool due = _dep.Minutes(_model.Now) == 0 && !_minutes.IsRolling;
        if (due != _isDue)
        {
            _isDue = due;
            _countdown.Visible = !due;
            _due.Visible = due;
        }

        bool tint = _byLine();
        if (tint != _tinted)
        {
            _tinted = tint;
            var accent = BoardStyles.Tinted(_dep.Color);
            _destination.Style = tint ? new TextStyle(Fonts.Big, accent) : _styles.Big;
            _minutes.Style = tint ? new TextStyle(Fonts.Big, accent) : _styles.BigAmber;
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float strength = _tinted ? 0.55f : 0.36f;
        if (_isDue) strength *= 0.85f + 0.75f * TubeGfx.Wave(_time, 0.9);
        TubeGfx.FillRamp(frame, bounds, _dep.Color.WithBrightness(strength), new Pixel(6, 6, 8), 0.8f);
    }
}

/// <summary>A following train: line stripe, platform, destination and minutes on one 14px line.</summary>
internal sealed class CompactRow : Stack
{
    public const int RowHeight = 14;

    private readonly Departure _dep;
    private readonly DepartureBoardModel _model;
    private readonly Func<bool> _byLine;
    private readonly BoardStyles _styles;
    private readonly MarqueeLabel _destination;
    private readonly RollingNumber _minutes;
    private readonly Stack _countdown;
    private readonly DueBadge _due;
    private bool _tinted;
    private bool _isDue;

    public CompactRow(Departure dep, DepartureBoardModel model, BoardStyles styles, Func<bool> byLine) : base(Orientation.Horizontal)
    {
        _dep = dep;
        _model = model;
        _styles = styles;
        _byLine = byLine;
        CrossAlign = Align.Center;

        _destination = new MarqueeLabel(dep.Destination) { Style = styles.Small, Grow = 1, Margin = new Thickness(2, 0, 4, 0) };
        _minutes = new RollingNumber(() => dep.Minutes(model.Now)) { Style = styles.SmallAmber };
        _countdown = new Stack(Orientation.Horizontal)
        {
            Children = { _minutes, new Label("min") { Style = styles.TinyAmber, VAlign = Align.End, Margin = new Thickness(2, 0, 0, 1) } },
        };
        _due = new DueBadge(Fonts.Small, filled: false) { Visible = false };

        Add(new Block(dep.Color, width: 3) { Margin = new Thickness(0, 1, 0, 1) });
        Add(new Label(dep.PlatformNumber) { Style = styles.Tiny, TextAlignment = TextAlign.Center, Width = 13 });
        Add(_destination);
        Add(new Panel { Width = 48, Margin = new Thickness(0, 0, 4, 0), Children = { _countdown, _due } });
        _countdown.HAlign = Align.End;
        _countdown.VAlign = Align.Center;
        _due.HAlign = Align.End;
        _due.VAlign = Align.Center;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        bool due = _dep.Minutes(_model.Now) == 0 && !_minutes.IsRolling;
        if (due != _isDue)
        {
            _isDue = due;
            _countdown.Visible = !due;
            _due.Visible = due;
        }

        bool tint = _byLine();
        if (tint != _tinted)
        {
            _tinted = tint;
            var accent = BoardStyles.Tinted(_dep.Color);
            _destination.Style = tint ? new TextStyle(Fonts.Small, accent, Shadow: false) : _styles.Small;
            _minutes.Style = tint ? new TextStyle(Fonts.Small, accent, Shadow: false) : _styles.SmallAmber;
        }
    }
}
