using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Rail;

/// <summary>Colours and text styles of the rail board. Created in <c>Build</c>, once the fonts are loaded.</summary>
internal sealed class RailStyles
{
    public static readonly Pixel Green = new(70, 225, 105);
    public static readonly Pixel Red = new(240, 60, 55);
    public static readonly Pixel Amber = TubeGfx.Amber;
    public static readonly Pixel Dim = new(120, 120, 130);
    public static readonly Pixel Ink = new(235, 235, 235);

    public readonly TextStyle Header = new(Fonts.QuiteSmall, Pixel.White, Shadow: false);
    public readonly TextStyle HeaderMuted = new(Fonts.QuiteSmall, new Pixel(150, 160, 200), Shadow: false);
    public readonly TextStyle Strip = new(Fonts.QuiteSmall, new Pixel(190, 190, 200), Shadow: false);
    public readonly TextStyle Clock = new(Fonts.Small, Pixel.White, Shadow: false);
    public readonly TextStyle Message = new(Fonts.Small, Amber, Shadow: false);
    public readonly TextStyle Calling = new(Fonts.QuiteSmall, new Pixel(190, 200, 235), Shadow: false);
    public readonly TextStyle CallingCancelled = new(Fonts.QuiteSmall, Red, Shadow: false);

    private readonly TextStyle[] _small = new TextStyle[3], _tiny = new TextStyle[3], _smallText = new TextStyle[3], _tinyText = new TextStyle[3];

    public RailStyles()
    {
        for (int i = 0; i < 3; i++)
        {
            var status = ((RailStatus)i) switch { RailStatus.OnTime => Green, RailStatus.Delayed => Amber, _ => Red };
            var text = ((RailStatus)i) == RailStatus.Cancelled ? Dim : Ink;
            _small[i] = new TextStyle(Fonts.Small, status, Shadow: false);
            _tiny[i] = new TextStyle(Fonts.QuiteSmall, status, Shadow: false);
            _smallText[i] = new TextStyle(Fonts.Small, text, Shadow: false);
            _tinyText[i] = new TextStyle(Fonts.QuiteSmall, text, Shadow: false);
        }
    }

    public TextStyle StatusSmall(RailStatus s) => _small[(int)s];
    public TextStyle StatusTiny(RailStatus s) => _tiny[(int)s];
    public TextStyle TextSmall(RailStatus s) => _smallText[(int)s];
    public TextStyle TextTiny(RailStatus s) => _tinyText[(int)s];
}

/// <summary>Display options of the rail board shared by its rows (see <see cref="Tube.ArrivalOptions"/>), updated by the app each frame.</summary>
internal sealed class RailOptions
{
    public ArrivalFormat Format { get; set; } = ArrivalFormat.Clock;
    public bool ShowDestination { get; set; } = true;
    public bool ShowPlatform { get; set; } = true;
    public bool ShowCallingPoints { get; set; } = true;
}

/// <summary>A following train: scheduled time, destination, platform and status on one 7px line.</summary>
internal sealed class RailRowNode : Stack
{
    public const int RowHeight = 7;

    private readonly RailRow _row;
    private readonly RailStyles _styles;
    private readonly Label _time, _platform, _status;
    private readonly MarqueeLabel _destination;
    private readonly Panel _timeCell, _platformCell;
    private readonly RailOptions _options;
    private int _version = -1;
    private ArrivalFormat _format = (ArrivalFormat)(-1);
    private bool _platformShown = true;

    public RailRowNode(RailRow row, RailStyles styles, RailOptions? options = null) : base(Orientation.Horizontal)
    {
        _row = row;
        _styles = styles;
        _options = options ?? new RailOptions();
        CrossAlign = Align.Center;

        _time = new Label(() => row.Time) { Style = styles.TextTiny(row.State) };
        _destination = new MarqueeLabel(() => _options.ShowDestination ? row.Destination : "") { Style = styles.TextTiny(row.State), Grow = 1, Margin = new Thickness(4, 0, 4, 0) };
        _platform = new Label(() => row.Platform) { Style = styles.TextTiny(row.State), HAlign = Align.End };
        _status = new Label(() => row.Status) { Style = styles.StatusTiny(row.State), HAlign = Align.End };

        _timeCell = new Panel { Width = 28, Margin = new Thickness(3, 0, 0, 0), Children = { _time } };
        _platformCell = new Panel { Width = 8, Margin = new Thickness(0, 0, 6, 0), Children = { _platform } };
        Add(_timeCell);
        Add(_destination);
        Add(_platformCell);
        Add(new Panel { Width = 50, Margin = new Thickness(0, 0, 4, 0), Children = { _status } });
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_options.Format != _format)
        {
            _format = _options.Format;
            _timeCell.Width = _format switch { ArrivalFormat.Both => 62, ArrivalFormat.Minutes => 34, _ => 28 };
        }
        if (_options.ShowPlatform != _platformShown)
        {
            _platformShown = _options.ShowPlatform;
            _platformCell.Visible = _platformShown;
        }
        if (_version == _row.Version) return;
        _version = _row.Version;
        _time.Style = _destination.Style = _platform.Style = _styles.TextTiny(_row.State);
        _status.Style = _styles.StatusTiny(_row.State);
    }
}

/// <summary>
/// The next train, emphasised: bigger text over a soft blue glow, with the calling points scrolling beneath it.
/// </summary>
internal sealed class RailHeroNode : Stack
{
    public const int RowHeight = 22;

    private readonly RailRow _row;
    private readonly RailStyles _styles;
    private readonly Label _time, _status;
    private readonly MarqueeLabel _destination, _calling;
    private readonly Pill _platform;
    private readonly Block _stripe;
    private readonly Panel _timeCell;
    private readonly RailOptions _options;
    private int _version = -1;
    private ArrivalFormat _format = (ArrivalFormat)(-1);

    public RailHeroNode(RailRow row, RailStyles styles, RailOptions? options = null) : base(Orientation.Vertical)
    {
        _row = row;
        _styles = styles;
        _options = options ?? new RailOptions();

        _stripe = new Block(RailStyles.Green, width: 3);
        _time = new Label(() => row.Time) { Style = styles.TextSmall(row.State) };
        _destination = new MarqueeLabel(() => _options.ShowDestination ? row.Destination : "") { Style = styles.TextSmall(row.State), Grow = 1, Margin = new Thickness(4, 0, 4, 0) };
        _platform = new Pill(row.Platform, new Pixel(235, 235, 240))
        {
            Style = new TextStyle(Fonts.Small, Pixel.Black, Shadow: false),
            Width = 13,
            Height = 13,
            Radius = 3,
            Padding = new Thickness(0),
        };
        _status = new Label(() => row.Status) { Style = styles.StatusSmall(row.State), HAlign = Align.End };
        _calling = new MarqueeLabel(() => row.Info) { Style = styles.Calling, Height = 7, Margin = new Thickness(7, 0, 4, 0), HAlign = Align.Stretch };

        _timeCell = new Panel { Width = 36, Margin = new Thickness(4, 0, 0, 0), Children = { _time } };
        var top = new Stack(Orientation.Horizontal)
        {
            Height = 15,
            HAlign = Align.Stretch,
            CrossAlign = Align.Center,
            Children =
            {
                _stripe,
                _timeCell,
                _destination,
                _platform,
                new Panel { Width = 56, Margin = new Thickness(4, 0, 4, 0), Children = { _status } },
            },
        };
        _stripe.Height = 13;
        Add(top);
        Add(_calling);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_options.Format != _format)
        {
            _format = _options.Format;
            _timeCell.Width = _format switch { ArrivalFormat.Both => 88, ArrivalFormat.Minutes => 48, _ => 36 };
        }
        _platform.Visible = _options.ShowPlatform;
        _calling.Visible = _options.ShowCallingPoints;
        if (_version == _row.Version) return;
        _version = _row.Version;
        _time.Style = _destination.Style = _styles.TextSmall(_row.State);
        _status.Style = _styles.StatusSmall(_row.State);
        _stripe.Color = _row.State switch { RailStatus.OnTime => RailStyles.Green, RailStatus.Delayed => RailStyles.Amber, _ => RailStyles.Red };
        _platform.Text = _row.Platform;
        _platform.Background = _row.State == RailStatus.Cancelled ? new Pixel(90, 90, 96) : new Pixel(235, 235, 240);
        _calling.Style = _row.State == RailStatus.Cancelled ? _styles.CallingCancelled : _styles.Calling;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds) =>
        TubeGfx.FillRamp(frame, bounds, new Pixel(22, 30, 62), new Pixel(6, 6, 10), 0.85f);
}
