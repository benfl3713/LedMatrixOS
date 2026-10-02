using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Tube;

/// <summary>Identifies one page of the bus board: a stop, by position and id, so a changed stop list always rebuilds its pages.</summary>
internal readonly record struct StopToken(int Index, string StopId);

internal static class BusColors
{
    /// <summary>The red of a London bus roundel.</summary>
    public static readonly Pixel Day = new(224, 38, 36);

    /// <summary>Night routes (N-prefixed) get the night-bus blue, lifted so it stays legible on the panel.</summary>
    public static readonly Pixel Night = new(46, 84, 214);

    public static Pixel For(string route) => route.Length > 1 && (route[0] == 'N' || route[0] == 'n') && char.IsDigit(route[1]) ? Night : Day;
}

/// <summary>
/// One bus: a route badge, the destination (scrolling when long) and the minutes as a rolling number, or a flashing DUE under a minute.
/// 13px tall, so four rows plus a header fill the 64px panel.
/// </summary>
internal sealed class BusRow : Stack
{
    public const int RowHeight = 13;

    private readonly Departure _dep;
    private readonly DepartureBoardModel _model;
    private readonly RollingNumber _minutes;
    private readonly Stack _countdown;
    private readonly DueBadge _due;
    private bool _isDue;

    public BusRow(Departure dep, DepartureBoardModel model, BoardStyles styles) : base(Orientation.Horizontal)
    {
        _dep = dep;
        _model = model;
        CrossAlign = Align.Center;

        var badge = new Pill(dep.LineName, BusColors.For(dep.LineName))
        {
            Style = new TextStyle(Fonts.Small, Pixel.White, Shadow: false),
            Width = 25,
            Height = 11,
            Radius = 3,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
        };

        _minutes = new RollingNumber(() => dep.Minutes(model.Now)) { Style = styles.SmallAmber };
        _countdown = new Stack(Orientation.Horizontal)
        {
            Children = { _minutes, new Label("min") { Style = styles.TinyAmber, VAlign = Align.End, Margin = new Thickness(2, 0, 0, 1) } },
        };
        _due = new DueBadge(Fonts.Small, filled: false) { Visible = false };

        Add(badge);
        Add(new MarqueeLabel(dep.Destination) { Style = styles.Small, Grow = 1, Margin = new Thickness(4, 0, 4, 0) });
        Add(new Panel { Width = 44, Margin = new Thickness(0, 0, 4, 0), Children = { _countdown, _due } });
        _countdown.HAlign = Align.End;
        _countdown.VAlign = Align.Center;
        _due.HAlign = Align.End;
        _due.VAlign = Align.Center;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        bool due = _dep.Minutes(_model.Now) == 0 && !_minutes.IsRolling;
        if (due == _isDue) return;
        _isDue = due;
        _countdown.Visible = !due;
        _due.Visible = due;
    }
}

/// <summary>The live data and board model of one bus stop. Replaced wholesale when the stop list changes.</summary>
internal sealed class BusStopFeed(string stopId)
{
    public string StopId { get; } = stopId;
    public DepartureBoardModel Model { get; } = new() { FilterByRoute = true };
    public volatile ILiveData<TflArrival[]>? Arrivals;
    public volatile ILiveData<string>? Label;
}
