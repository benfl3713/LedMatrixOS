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
    private readonly ArrivalOptions _options;
    private readonly MarqueeLabel _destination;
    private readonly ArrivalCell _arrival;
    private readonly Pill _platform;
    private bool _tinted;
    private bool _platformShown;
    private bool _destinationShown = true;
    private TimeSpan _time;

    public HeroRow(Departure dep, DepartureBoardModel model, BoardStyles styles, Func<bool> byLine, ArrivalOptions? options = null) : base(Orientation.Horizontal)
    {
        _dep = dep;
        _model = model;
        _styles = styles;
        _byLine = byLine;
        _options = options ?? new ArrivalOptions();
        CrossAlign = Align.Center;

        _destination = new MarqueeLabel(dep.Destination) { Style = styles.Big, Grow = 1, Margin = new Thickness(8, 0, 4, 0) };
        _arrival = new ArrivalCell(dep, model, _options, styles.BigAmber, styles.TinyAmber, new Thickness(2, 0, 0, 4), Fonts.Big, dueFilled: true, baseWidth: 66)
            { Margin = new Thickness(0, 0, 4, 0) };

        Add(new Block(dep.Color, width: 4));
        // The platform badge only appears where the route is served from several platforms
        _platform = new Pill(dep.PlatformNumber, new Pixel(235, 235, 240))
        {
            Style = new TextStyle(Fonts.Small, Pixel.Black, Shadow: false),
            Width = 15,
            Height = 15,
            Radius = 3,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            Visible = false,
        };
        Add(_platform);

        Add(_destination);
        Add(_arrival);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;

        if (_options.ShowDestination != _destinationShown)
        {
            _destinationShown = _options.ShowDestination;
            _destination.Text = _destinationShown ? _dep.Destination : "";
        }

        bool showPlatform = _dep.ShowPlatform && _dep.PlatformNumber.Length > 0;
        if (showPlatform != _platformShown)
        {
            _platformShown = showPlatform;
            _platform.Visible = showPlatform;
            _destination.Margin = showPlatform ? new Thickness(7, 0, 4, 0) : new Thickness(8, 0, 4, 0);
        }

        bool tint = _byLine();
        if (tint != _tinted)
        {
            _tinted = tint;
            var accent = BoardStyles.Tinted(_dep.Color);
            _destination.Style = tint ? new TextStyle(Fonts.Big, accent) : _styles.Big;
            _arrival.SetStyles(tint ? new TextStyle(Fonts.Big, accent) : _styles.BigAmber);
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float strength = _tinted ? 0.55f : 0.36f;
        if (_arrival.IsDue) strength *= 0.85f + 0.75f * TubeGfx.Wave(_time, 0.9);
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
    private readonly ArrivalOptions _options;
    private readonly FitLabel _destination;
    private readonly ArrivalCell _arrival;
    private readonly Label _platform;
    private bool _tinted;
    private bool _platformShown;
    private bool _destinationShown = true;

    public CompactRow(Departure dep, DepartureBoardModel model, BoardStyles styles, Func<bool> byLine, int countdownWidth = 48, int platformWidth = 13, ArrivalOptions? options = null)
        : base(Orientation.Horizontal)
    {
        _dep = dep;
        _model = model;
        _styles = styles;
        _byLine = byLine;
        _options = options ?? new ArrivalOptions();
        CrossAlign = Align.Center;

        // A destination that does not fit is abbreviated ("Walthamstow C.") rather than cut mid-word.
        _destination = new FitLabel(dep.Destination, styles.Small) { Grow = 1, Margin = new Thickness(4, 0, 4, 0) };
        _arrival = new ArrivalCell(dep, model, _options, styles.SmallAmber, styles.TinyAmber, new Thickness(2, 0, 0, 1), Fonts.Small, dueFilled: false, baseWidth: countdownWidth)
            { Margin = new Thickness(0, 0, 4, 0) };

        Add(new Block(dep.Color, width: 3) { Margin = new Thickness(0, 1, 0, 1) });
        // The platform only takes room where the route is served from several platforms
        _platform = new Label(dep.PlatformNumber) { Style = styles.Tiny, TextAlignment = TextAlign.Center, Width = platformWidth, Visible = false };
        Add(_platform);
        Add(_destination);
        Add(_arrival);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        if (_options.ShowDestination != _destinationShown)
        {
            _destinationShown = _options.ShowDestination;
            _destination.Text = _destinationShown ? _dep.Destination : "";
        }

        bool showPlatform = _dep.ShowPlatform && _dep.PlatformNumber.Length > 0;
        if (showPlatform != _platformShown)
        {
            _platformShown = showPlatform;
            _platform.Visible = showPlatform;
            _destination.Margin = showPlatform ? new Thickness(2, 0, 4, 0) : new Thickness(4, 0, 4, 0);
        }

        bool tint = _byLine();
        if (tint != _tinted)
        {
            _tinted = tint;
            var accent = BoardStyles.Tinted(_dep.Color);
            _destination.Style = tint ? new TextStyle(Fonts.Small, accent, Shadow: false) : _styles.Small;
            _arrival.SetStyles(tint ? new TextStyle(Fonts.Small, accent, Shadow: false) : _styles.SmallAmber);
        }
    }
}

/// <summary>
/// A row of the classic platform sign: amber 5x7 dot-matrix text on black, "1  Brixton  3 min". Under a minute the minutes give way to a
/// pulsing DUE. With "colour by line" the destination takes the line colour.
/// </summary>
internal sealed class PlatformRow : Stack
{
    public const int RowHeight = 8;

    private readonly Departure _dep;
    private readonly DepartureBoardModel _model;
    private readonly Func<bool> _byLine;
    private readonly BoardStyles _styles;
    private readonly ArrivalOptions _options;
    private readonly MarqueeLabel _destination;
    private readonly ArrivalCell _arrival;
    private readonly Label _platform;
    private bool _tinted;
    private bool _destinationShown = true;

    public PlatformRow(Departure dep, DepartureBoardModel model, BoardStyles styles, Func<bool> byLine, ArrivalOptions? options = null) : base(Orientation.Horizontal)
    {
        _dep = dep;
        _model = model;
        _styles = styles;
        _byLine = byLine;
        _options = options ?? new ArrivalOptions();
        CrossAlign = Align.Center;

        _destination = new MarqueeLabel(dep.Destination) { Style = styles.TinyAmber, Grow = 1, Margin = new Thickness(0, 0, 4, 0), VAlign = Align.Center };
        _arrival = new ArrivalCell(dep, model, _options, styles.TinyAmber, styles.TinyAmber, new Thickness(2, 0, 0, 0), Fonts.QuiteSmall, dueFilled: false, baseWidth: 40)
            { Margin = new Thickness(0, 0, 6, 0) };

        // Line colour stripe, then a platform cell that is only filled where the route is served from several platforms
        Add(new Block(dep.Color.WithBrightness(0.8f), width: 2) { Margin = new Thickness(6, 1, 0, 1) });
        _platform = new Label(() => _dep.ShowPlatform ? _dep.PlatformNumber : "") { Style = styles.TinyAmber, Width = 12, TextAlignment = TextAlign.Center, VAlign = Align.Center };
        Add(_platform);
        Add(_destination);
        Add(_arrival);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        if (_options.ShowDestination != _destinationShown)
        {
            _destinationShown = _options.ShowDestination;
            _destination.Text = _destinationShown ? _dep.Destination : "";
        }

        bool tint = _byLine();
        if (tint != _tinted)
        {
            _tinted = tint;
            _destination.Style = tint ? new TextStyle(Fonts.QuiteSmall, BoardStyles.Tinted(_dep.Color), Shadow: false) : _styles.TinyAmber;
        }
    }
}
