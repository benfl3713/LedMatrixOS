using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Cycle;

/// <summary>Identifies one page of the dock pager: a docking station by position and id, so a changed dock list always rebuilds its pages.</summary>
internal readonly record struct DockToken(int Index, string DockId);

/// <summary>The live data of one docking station. Replaced wholesale when the dock list changes.</summary>
internal sealed class DockFeed(string dockId)
{
    public string DockId { get; } = dockId;
    public volatile ILiveData<BikePointInfo>? Info;
}

/// <summary>Text styles of the Cycle Hub. Created in <c>Build</c>, once the fonts are loaded.</summary>
internal sealed class CycleStyles
{
    public static readonly Pixel Free = new(70, 150, 255);
    public static readonly Pixel Neutral = new(130, 130, 140);

    /// <summary>Big bike count, by availability: red (none), amber (1-4), green (5+).</summary>
    public readonly TextStyle[] Count =
    [
        new(WeatherFonts.Digits, LineHealth.SevereColor),
        new(WeatherFonts.Digits, LineHealth.MinorColor),
        new(WeatherFonts.Digits, LineHealth.GoodColor),
    ];

    /// <summary>The verdict word, indexed by <see cref="Ride"/>, with a last neutral entry for "no forecast yet".</summary>
    public readonly TextStyle[] Verdict =
    [
        Word(RideVerdict.ColorOf(Ride.Good)), Word(RideVerdict.ColorOf(Ride.Ok)), Word(RideVerdict.ColorOf(Ride.Wet)),
        Word(RideVerdict.ColorOf(Ride.Windy)), Word(RideVerdict.ColorOf(Ride.Avoid)), Word(Neutral),
    ];

    public readonly TextStyle Name = new(Fonts.QuiteSmall, new Pixel(245, 245, 245), Shadow: false);
    public readonly TextStyle Bikes = new(Fonts.QuiteSmall, new Pixel(200, 200, 210), Shadow: false);
    public readonly TextStyle EBikes = new(Fonts.QuiteSmall, new Pixel(120, 230, 255), Shadow: false);
    public readonly TextStyle Docks = new(Fonts.QuiteSmall, Free, Shadow: false);
    public readonly TextStyle Muted = new(Fonts.QuiteSmall, new Pixel(150, 150, 160), Shadow: false);
    public readonly TextStyle Message = new(Fonts.Small, TubeGfx.Amber, Shadow: false);
    public readonly TextStyle Clock = new(Fonts.Small, new Pixel(245, 245, 245), Shadow: false);
    public readonly TextStyle PillText = new(Fonts.QuiteSmall, Pixel.White, Shadow: false);

    private static TextStyle Word(Pixel color) => new(WeatherFonts.Bold, color);
}

/// <summary>
/// One docking station: its name, a big rolling count of bikes available (red none, amber 1-4, green 5+), the e-bikes among them and
/// a bar showing how many docks are free.
/// </summary>
internal sealed class DockPage : Stack
{
    private readonly DockFeed _feed;
    private readonly CycleStyles _styles;
    private readonly RollingNumber _count;
    private int _level = -1;

    public DockPage(DockFeed feed, CycleStyles styles) : base(Orientation.Vertical, gap: 1)
    {
        _feed = feed;
        _styles = styles;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        Padding = new Thickness(6, 2, 4, 0);

        BikePointInfo? Info() => _feed.Info?.Value;

        var name = new Memo<BikePointInfo?>(Info, i => i?.Name ?? "");
        var eBikes = new Memo<int>(() => Info()?.NbEBikes ?? 0, n => n + (n == 1 ? " e-bike" : " e-bikes"));
        var free = new Memo<int>(() => Info()?.NbEmptyDocks ?? 0, n => n + " free docks");

        _count = new RollingNumber(() => Info()?.NbBikes ?? 0) { Spacing = 0, VAlign = Align.Center };

        Add(new MarqueeLabel(name.Get) { Style = styles.Name, HAlign = Align.Stretch });
        Add(new Stack(Orientation.Horizontal, gap: 7)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Grow = 1,
            CrossAlign = Align.Center,
            Children =
            {
                _count,
                new Stack(Orientation.Vertical, gap: 2)
                {
                    Grow = 1,
                    VAlign = Align.Center,
                    Children =
                    {
                        new Label("bikes") { Style = styles.Bikes },
                        new Label(eBikes.Get) { Style = styles.EBikes },
                        new Label(free.Get) { Style = styles.Docks },
                        new ProgressBar(() => Info() is { Capacity: > 0 } i ? (float)i.NbEmptyDocks / i.Capacity : 0f)
                            { Fill = CycleStyles.Free, Background = new Pixel(28, 28, 36), Thickness = 3 },
                    },
                },
            },
        });
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        int bikes = _feed.Info?.Value?.NbBikes ?? 0;
        int level = bikes >= 5 ? 2 : bikes >= 1 ? 1 : 0;
        if (level == _level) return;
        _level = level;
        _count.Style = _styles.Count[level];
    }
}
