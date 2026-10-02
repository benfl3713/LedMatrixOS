using System.Numerics;
using LedMatrixOS.Apps.Clocks;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// The everyday wall clock. Huge anti-aliased gradient digits use the full height of the panel and roll when they change,
/// a pulsing colon and a seconds sweep along the bottom edge keep time, and the weekday and date sit in a side column.
/// The colours drift a little further along the palette every minute, and a dim colour field with a drifting light beam
/// gives the background a pulse of its own.
/// </summary>
public sealed class ClockApp : WidgetApp
{
    public override string Id => "clock";
    public override string Name => "Clock";

    [Setting("Show Seconds", Description = "Display seconds (digits and the sweep along the bottom edge)")]
    public bool ShowSeconds { get; set; } = true;

    [Setting("24-Hour Format", Description = "Use 24-hour format instead of 12-hour")]
    public bool Show24Hour { get; set; } = true;

    [Setting("Time Color", Description = "Palette uses the colour gradient of the chosen palette; a named colour overrides the digits", Options = ["Palette", "White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta"])]
    public string TimeColor { get; set; } = "Palette";

    [Setting("Palette", Description = "Colour theme", Options = ["Sunset", "Ocean", "Neon", "Aurora", "Ember", "Mono"])]
    public string Palette { get; set; } = "Sunset";

    [Setting("Show Date", Description = "Show the weekday and date next to the time")]
    public bool ShowDate { get; set; } = true;

    private ClockState _state = null!;
    private LiveTheme _theme = null!;
    private Label? _dayLabel, _dateLabel, _ampmLabel;
    private int _lastMinute = -1;

    protected override void OnSettingChanged(string key)
    {
        if (Root is null) return;
        switch (key)
        {
            case "palette":
                _theme.SetPalette(ClockPalette.ByName(Palette), Animator);
                ApplyLabelStyles();
                break;
            case "timeColor":
                _theme.DigitOverride = DigitOverride();
                _theme.Refresh();
                break;
            case "show24Hour":
                break; // read live by the hour digits
            default:
                Host.Root = Build(); // layout-affecting settings rebuild the tree (and replay the entrance)
                break;
        }
    }

    private Pixel? DigitOverride() => TimeColor == "Palette" ? null : ClockPalette.ByColorName(TimeColor);

    protected override Node Build()
    {
        _state = new ClockState(Time);
        _state.Refresh();
        _theme = new LiveTheme(ClockPalette.ByName(Palette)) { DigitOverride = DigitOverride() };
        _theme.Refresh();
        _lastMinute = -1;

        var big = GlyphAtlas.Get(27, 50, 7f, 2);
        var small = GlyphAtlas.Get(13, 24, 4f, 2);
        var st = _state;

        GlyphDigit Big(Func<int> src, int order) => Make(new GlyphDigit(big, src) { Theme = _theme, HaloAlpha = 0.3f }, order);
        GlyphDigit Small(Func<int> src, int order) => Make(new GlyphDigit(small, src) { Theme = _theme, HaloAlpha = 0.28f, RollDuration = TimeSpan.FromMilliseconds(380) }, order);
        static GlyphDigit Make(GlyphDigit d, int order)
        {
            d.StartEntrance(TimeSpan.FromMilliseconds(120 + order * 110));
            return d;
        }

        var row = new Stack(Orientation.Horizontal)
        {
            Margin = new Thickness(18, 5, 0, 0),
            Height = 54,
            CrossAlign = Align.Center,
            Children =
            {
                Big(() => Show24Hour ? st.Hour / 10 : (st.Hour12 >= 10 ? 1 : -1), 0),
                Big(() => (Show24Hour ? st.Hour : st.Hour12) % 10, 1),
                new ColonNode(st, () => Pixel.Lerp(_theme.Top, _theme.Bottom, 0.5f), 54, 3.6f),
                Big(() => st.Minute / 10, 2),
                Big(() => st.Minute % 10, 3),
            },
        };
        row.HAlign = Align.Start;

        var root = new Panel
        {
            new ThemeNode(_theme),
            new ClockStateNode(_state),
            new AmbientBackdrop(_state, _theme) { Level = 1.3f },
        };

        // Minute-by-minute drift of the digit gradient along the palette.
        root.Add(new MinuteDrift(_state, _theme));
        root.Add(row);

        var side = new Stack(Orientation.Vertical, gap: 0)
        {
            Margin = new Thickness(166, 4, 0, 0),
            HAlign = Align.Start,
            VAlign = Align.Start,
        };

        if (ShowSeconds)
        {
            var secRow = new Stack(Orientation.Horizontal, gap: 0) { Height = 28, CrossAlign = Align.Center };
            secRow.Add(Small(() => st.Second / 10, 4));
            secRow.Add(Small(() => st.Second % 10, 5));
            if (!Show24Hour)
            {
                _ampmLabel = new Label(() => st.IsPm ? "PM" : "AM") { Margin = new Thickness(5, 0, 0, 0), VAlign = Align.Start };
                secRow.Add(_ampmLabel);
            }
            else _ampmLabel = null;
            side.Add(secRow);
        }

        if (ShowDate)
        {
            _dayLabel = new Label(() => st.DayName) { Margin = new Thickness(2, ShowSeconds ? 3 : 8, 0, 0) };
            _dateLabel = new Label(() => st.DateText) { Margin = new Thickness(2, 1, 0, 0) };
            side.Add(_dayLabel);
            side.Add(_dateLabel);
            if (!ShowSeconds && !Show24Hour)
            {
                _ampmLabel = new Label(() => st.IsPm ? "PM" : "AM") { Margin = new Thickness(2, 1, 0, 0) };
                side.Add(_ampmLabel);
            }
        }
        else if (!ShowSeconds)
        {
            _dayLabel = new Label(() => st.DayName) { Margin = new Thickness(2, 14, 0, 0) };
            side.Add(_dayLabel);
        }

        root.Add(side);
        if (ShowSeconds) root.Add(new SecondsSweep(_state, _theme));
        ApplyLabelStyles();

        // Labels glide in from below after the digits have landed.
        foreach (var l in new Node?[] { _dayLabel, _dateLabel, _ampmLabel })
        {
            if (l is null) continue;
            l.Position = new Vector2(0, 40);
            l.AnimatePosition(Vector2.Zero, TimeSpan.FromMilliseconds(900), Easing.OutBack);
        }

        return root;
    }

    private void ApplyLabelStyles()
    {
        var p = _theme.Target;
        var day = new TextStyle(Fonts.Small, p.G0);
        var date = new TextStyle(Fonts.Small, new Pixel(235, 235, 245));
        if (_dayLabel is not null) _dayLabel.Style = day;
        if (_dateLabel is not null) _dateLabel.Style = date;
        if (_ampmLabel is not null) _ampmLabel.Style = new TextStyle(Fonts.Small, p.Accent);
    }
}

/// <summary>Eases the palette gradient toward minute/59 whenever the minute changes.</summary>
internal sealed class MinuteDrift : Node
{
    private readonly ClockState _state;
    private readonly LiveTheme _theme;
    private int _last = -1;

    public MinuteDrift(ClockState state, LiveTheme theme)
    {
        _state = state;
        _theme = theme;
        Width = 0;
        Height = 0;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_state.Minute == _last || Host is null) return;
        _last = _state.Minute;
        _theme.ShiftTo(_state.Minute / 59f * 0.9f, Host.Animator);
    }
}
