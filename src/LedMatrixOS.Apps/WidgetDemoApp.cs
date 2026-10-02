using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Showcase and smoke test for the widget layer: a departures board whose rows reshuffle every few seconds (ListView keyed diffing),
/// a pager of rows, a rolling counter, a marquee and a clock. All data is fake and driven by frame time, so renders are deterministic.
/// </summary>
public sealed class WidgetDemoApp : WidgetApp
{
    private static readonly TimeSpan ShuffleEvery = TimeSpan.FromSeconds(4);

    private sealed record Departure(int Id, string Line, Pixel Color, string Destination, int Minutes);

    private static readonly Departure[] AllDepartures =
    [
        new(1, "VIC", new Pixel(0, 160, 232), "Brixton", 2),
        new(2, "CEN", new Pixel(220, 36, 31), "Epping", 4),
        new(3, "NOR", new Pixel(90, 90, 90), "High Barnet", 5),
        new(4, "JUB", new Pixel(134, 143, 152), "Stanmore", 7),
        new(5, "DIS", new Pixel(0, 125, 50), "Upminster", 9),
        new(6, "PIC", new Pixel(0, 25, 168), "Cockfosters", 11),
    ];

    private static readonly string[] PagerRows =
    [
        "Pager row one", "Slides every 3s", "Rows come from data", "Fourth row here", "Fifth page row", "And the last one",
    ];

    private IReadOnlyList<Departure> _board = AllDepartures.Take(4).ToList();
    private IReadOnlyList<string> _pagerRows = PagerRows;
    private int _boardEpoch;

    public override string Id => "widget-demo";
    public override string Name => "Widget Demo";

    protected override Node Build()
    {
        var text = new TextStyle(Fonts.Small, new Pixel(220, 220, 220));
        var accent = new TextStyle(Fonts.Small, new Pixel(255, 170, 40));
        var tiny = new TextStyle(Fonts.QuiteSmall, Pixel.White, Shadow: false);

        return new Dock
        {
            Bottom = new Stack(Orientation.Horizontal, gap: 3)
            {
                CrossAlign = Align.Center,
                Padding = new Thickness(2, 0),
                Height = 12,
                Children =
                {
                    new MarqueeLabel("Widget layer demo: keyed list diffing, pager transitions, rolling digits and this marquee")
                        { Style = accent, Grow = 1 },
                    new Clock("HH:mm:ss", Time) { Style = text },
                },
            },
            Left = new ListView<Departure>(() => Board(), d => DepartureRow(d, text, tiny), d => d.Id)
            {
                Width = 130,
                Gap = 1,
                Padding = new Thickness(2, 1),
            },
            Fill = new Stack(Orientation.Vertical, gap: 2)
            {
                Padding = new Thickness(4, 1),
                Children =
                {
                    new RollingNumber(() => (int)(Frame.Time.TotalSeconds * 2)) { MinDigits = 4, Style = new TextStyle(Fonts.Big, new Pixel(255, 90, 60)) },
                    new Divider(),
                    new Pager(pageSize: 2, interval: TimeSpan.FromSeconds(3), transition: new Core.Transitions.SlideTransition(Core.Transitions.MoveDirection.Up))
                        { Grow = 1 }
                        .Bind(() => _pagerRows, row => new Label(row) { Style = text }),
                },
            },
        };
    }

    // Reordering the six services every few seconds makes rows slide, and the four-row window makes others fade in and out.
    private IReadOnlyList<Departure> Board()
    {
        int epoch = (int)(Frame.Time.Ticks / ShuffleEvery.Ticks);
        if (epoch != _boardEpoch)
        {
            _boardEpoch = epoch;
            var order = AllDepartures.ToArray();
            var rng = new Random(epoch);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            _board = order.Take(4).ToList();
        }
        return _board;
    }

    private static Node DepartureRow(Departure d, TextStyle text, TextStyle tiny) =>
        new Stack(Orientation.Horizontal, gap: 3)
        {
            CrossAlign = Align.Center,
            Children =
            {
                new Pill(d.Line, d.Color) { Style = tiny, Width = 24, Height = 11 },
                new Label(d.Destination) { Style = text, Grow = 1 },
                new Label($"{d.Minutes}m") { Style = text, TextAlignment = TextAlign.Right, Width = 22 },
            },
        };
}
