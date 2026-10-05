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
    private readonly ArrivalOptions _options;
    private readonly MarqueeLabel _destination;
    private bool _destinationShown = true;

    public BusRow(Departure dep, DepartureBoardModel model, BoardStyles styles, ArrivalOptions? options = null) : base(Orientation.Horizontal)
    {
        _dep = dep;
        _options = options ?? new ArrivalOptions();
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

        _destination = new MarqueeLabel(dep.Destination) { Style = styles.Small, Grow = 1, Margin = new Thickness(4, 0, 4, 0) };
        Add(badge);
        Add(_destination);
        Add(new ArrivalCell(dep, model, _options, styles.SmallAmber, styles.TinyAmber, new Thickness(2, 0, 0, 1), Fonts.Small, dueFilled: false, baseWidth: 44)
            { Margin = new Thickness(0, 0, 4, 0) });
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (_options.ShowDestination == _destinationShown) return;
        _destinationShown = _options.ShowDestination;
        _destination.Text = _destinationShown ? _dep.Destination : "";
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
