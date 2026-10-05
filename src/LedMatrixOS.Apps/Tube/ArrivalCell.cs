using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Tube;

/// <summary>
/// The right-hand cell of a departure row: the rolling minutes with their "min" unit, the arrival clock time, or both (see <see cref="ArrivalFormat"/>),
/// and a flashing DUE in place of them once the train is under the due threshold. The cell widens by the clock's width when both are shown.
/// </summary>
internal sealed class ArrivalCell : Panel
{
    private readonly Departure _dep;
    private readonly DepartureBoardModel _model;
    private readonly ArrivalOptions _options;
    private readonly Stack _row;
    private readonly Stack _countdown;
    private readonly Label _clock;
    private readonly DueBadge _due;
    private TextStyle _clockOnly, _clockBeside;
    private readonly int _baseWidth, _extraWidth;
    private ArrivalFormat _format = (ArrivalFormat)(-1);
    private bool _isDue, _applied;

    /// <param name="minutesStyle">Style of the rolling number (and of the clock when it is shown alone).</param>
    /// <param name="unitStyle">Style of the "min" unit (and of the clock when it sits beside the minutes).</param>
    public ArrivalCell(Departure dep, DepartureBoardModel model, ArrivalOptions options, TextStyle minutesStyle, TextStyle unitStyle, Thickness unitMargin,
        BdfFontParser.BdfFont dueFont, bool dueFilled, int baseWidth)
    {
        _dep = dep;
        _model = model;
        _options = options;
        _baseWidth = baseWidth;
        _clockOnly = minutesStyle;
        _clockBeside = unitStyle;

        Minutes = new RollingNumber(() => model.MinutesOf(dep)) { Style = minutesStyle };
        _countdown = new Stack(Orientation.Horizontal)
        {
            Children = { Minutes, new Label("min") { Style = unitStyle, VAlign = Align.End, Margin = unitMargin } },
        };
        _clock = new Label(() => dep.ClockText(model.Now, options.LocalNow())) { Style = minutesStyle, VAlign = Align.Center };
        _row = new Stack(Orientation.Horizontal, gap: 3) { CrossAlign = Align.Center, Children = { _clock, _countdown }, HAlign = Align.End, VAlign = Align.Center };
        _due = new DueBadge(dueFont, dueFilled) { Visible = false, HAlign = Align.End, VAlign = Align.Center };
        Width = baseWidth;
        Children.Add(_row);
        Children.Add(_due);

        var probe = new TextRun();
        probe.Set(unitStyle.Font, "00:00");
        _extraWidth = probe.Width + 3;
    }

    public RollingNumber Minutes { get; }

    /// <summary>True while the train is under the due threshold and the minutes have finished rolling.</summary>
    public bool IsDue => _isDue;

    /// <summary>Restyles the number, the unit and the clock (used when "colour departures by line" flips).</summary>
    public void SetStyles(TextStyle minutes)
    {
        _clockOnly = minutes;
        Minutes.Style = minutes;
        _clock.Style = _format == ArrivalFormat.Both ? _clockBeside : minutes;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        bool due = _model.MinutesOf(_dep) == 0 && !Minutes.IsRolling;
        var format = _options.Format;
        if (_applied && due == _isDue && format == _format) return;
        _applied = true;
        _isDue = due;
        if (format != _format)
        {
            _format = format;
            Width = _baseWidth + (format == ArrivalFormat.Both ? _extraWidth : 0);
            _clock.Style = format == ArrivalFormat.Both ? _clockBeside : _clockOnly;
        }

        _row.Visible = !due;
        _due.Visible = due;
        _clock.Visible = format != ArrivalFormat.Minutes;
        _countdown.Visible = format != ArrivalFormat.Clock;
    }
}
