using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Weather;

/// <summary>The info panel's background: the live sky colours, darkened so text on top stays crisp.</summary>
internal sealed class PanelBackdrop : Node
{
    private readonly Func<(Pixel Top, Pixel Bottom)> _sky;

    public PanelBackdrop(Func<(Pixel, Pixel)> sky)
    {
        _sky = sky;
        HAlign = VAlign = Align.Stretch;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var (top, bottom) = _sky();
        top = Pixel.Lerp(Pixel.Black, top, 0.38f);
        bottom = Pixel.Lerp(Pixel.Black, bottom, 0.5f);
        for (int y = 0; y < b.Height; y++)
            f.Fill(new Rectangle(b.X, b.Y + y, b.Width, 1), Pixel.Lerp(top, bottom, y / (float)Math.Max(1, b.Height - 1)));
    }
}

/// <summary>Placeholder shown while loading or offline: soft bars with a highlight sweeping across them.</summary>
internal sealed class Skeleton : Node
{
    private readonly Func<(Pixel Top, Pixel Bottom)> _sky;
    private readonly Func<bool> _animated;
    private float _t;

    public Skeleton(Func<(Pixel, Pixel)> sky, Func<bool> animated)
    {
        _sky = sky;
        _animated = animated;
        HAlign = VAlign = Align.Stretch;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _t = (float)ctx.Time.TotalSeconds;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var (top, bottom) = _sky();
        for (int y = 0; y < b.Height; y++)
            f.Fill(new Rectangle(b.X, b.Y + y, b.Width, 1), Pixel.Lerp(Pixel.Lerp(Pixel.Black, top, 0.38f), Pixel.Lerp(Pixel.Black, bottom, 0.5f), y / (float)b.Height));

        bool live = _animated();
        float sweep = live ? (_t * 0.7f % 1.4f) * (b.Width + 40) - 20 : -100;
        for (int row = 0; row < 3; row++)
        {
            var r = new Rectangle(b.X + 8, b.Y + 14 + row * 15, (row == 0 ? 70 : row == 1 ? 54 : 62), 9);
            UiRoundedFill(f, r, new Pixel(40, 52, 82));
            for (int x = r.Left; x < r.Right; x++)
            {
                float d = MathF.Abs(x - (b.X + sweep) - row * 6) / 14f;
                if (d >= 1f) continue;
                for (int y = r.Top; y < r.Bottom; y++) f.BlendPixel(x, y, new Pixel(120, 150, 210), (1f - d) * 0.55f);
            }
        }
    }

    private static void UiRoundedFill(FrameBuffer f, Rectangle r, Pixel c)
    {
        f.Fill(new Rectangle(r.X + 1, r.Y, r.Width - 2, r.Height), c);
        f.Fill(new Rectangle(r.X, r.Y + 1, r.Width, r.Height - 2), c);
    }
}

/// <summary>Three dots showing which page of the pager is up.</summary>
internal sealed class PageDots : Node
{
    private readonly Pager _pager;

    public PageDots(Pager pager)
    {
        _pager = pager;
        Width = 14;
        Height = 4;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        int active = _pager.IsTransitioning ? (_pager.PageIndex + 1) % Math.Max(1, _pager.PageCount) : _pager.PageIndex;
        for (int i = 0; i < _pager.PageCount; i++)
            f.Fill(new Rectangle(b.X + i * 5, b.Y, 4, 3), i == active ? new Pixel(255, 255, 255) : new Pixel(70, 82, 110));
    }
}

internal sealed class MinusBar : Node
{
    private readonly Func<Pixel> _color;

    public MinusBar(Func<Pixel> color)
    {
        _color = color;
        Width = 9;
        Height = 36;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var c = _color();
        f.Fill(new Rectangle(b.X, b.Y + 16, 8, 5), c);
        f.Fill(new Rectangle(b.X + 1, b.Y + 17, 8, 5), Pixel.Black);
        f.Fill(new Rectangle(b.X, b.Y + 16, 8, 5), c);
    }
}

/// <summary>The degree ring that sits beside the big temperature.</summary>
internal sealed class DegreeRing : Node
{
    private readonly Func<Pixel> _color;

    public DegreeRing(Func<Pixel> color)
    {
        _color = color;
        Width = 8;
        Height = 8;
    }

    private static readonly string[] Rows = ["..####..", ".######.", "###..###", "##....##", "##....##", "###..###", ".######.", "..####.."];

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var c = _color();
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                if (Rows[y][x] == '#') f.SetPixel(b.X + x, b.Y + y, c);
    }
}

internal sealed class DropIcon : Node
{
    private static readonly string[] Rows = ["...##...", "...##...", "..####..", "..####..", ".######.", "########", "########", "########", ".######.", "..####.."];

    public DropIcon()
    {
        Width = 8;
        Height = 10;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        for (int y = 0; y < Rows.Length; y++)
            for (int x = 0; x < 8; x++)
                if (Rows[y][x] == '#')
                    f.SetPixel(b.X + x, b.Y + y, y > 4 && x is 2 or 3 && y < 8 ? new Pixel(170, 215, 255) : new Pixel(60, 150, 255));
    }
}

/// <summary>Arrow showing where the wind is blowing to (the API gives the direction it comes from).</summary>
internal sealed class WindArrow : Node
{
    private readonly Func<int> _from;

    public WindArrow(Func<int> from)
    {
        _from = from;
        Width = 12;
        Height = 12;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        float a = (_from() + 180 - 90) * MathF.PI / 180f;
        float cx = b.X + 5.5f, cy = b.Y + 5.5f, c = MathF.Cos(a), s = MathF.Sin(a);
        int tx = (int)MathF.Round(cx + c * 5.5f), ty = (int)MathF.Round(cy + s * 5.5f);
        int bx = (int)MathF.Round(cx - c * 5.5f), by = (int)MathF.Round(cy - s * 5.5f);
        var col = new Pixel(215, 235, 255);
        f.DrawLine(bx, by, tx, ty, col);
        for (int side = -1; side <= 1; side += 2)
        {
            float ha = a + MathF.PI + side * 0.5f;
            f.DrawLine(tx, ty, (int)MathF.Round(tx + MathF.Cos(ha) * 4f), (int)MathF.Round(ty + MathF.Sin(ha) * 4f), col);
        }
    }
}

/// <summary>Builds the three forecast pages of the info panel from a bound snapshot.</summary>
internal static class WeatherPages
{
    private static readonly string[] Compass = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
    private static readonly Pixel Dim = new(150, 168, 205);
    private static readonly Pixel Warm = new(255, 160, 70);
    private static readonly Pixel Cool = new(110, 195, 255);

    public static Node Now(Func<WeatherSnapshot?> snap, Func<DateTimeOffset> now, Func<(Pixel, Pixel)> sky)
    {
        var small = new TextStyle(Fonts.Small, Pixel.White);
        var tiny = new TextStyle(Fonts.QuiteSmall, Dim, Shadow: false);

        var chance = new Memo<WeatherSnapshot?>(snap, s => s is null ? "" : s.PrecipChance + "%");
        var wind = new Memo<WeatherSnapshot?>(snap, s => s is null ? "" : Math.Round(s.WindSpeed) + (s.Fahrenheit ? " mph" : " kph"));
        var dir = new Memo<WeatherSnapshot?>(snap, s => s is null ? "" : Compass[(int)Math.Round(s.WindDirection / 45.0) % 8]);
        var sun = new Memo<(WeatherSnapshot?, int)>(() => (snap(), (int)now().TimeOfDay.TotalMinutes), SunText);

        Node bar = new ProgressBar(() => (snap()?.PrecipChance ?? 0) / 100f) { Fill = new Pixel(60, 150, 255), Background = new Pixel(18, 28, 52), Thickness = 5, Grow = 1, VAlign = Align.Center };
        return Page("TODAY", sky, new Stack(Orientation.Vertical, gap: 4)
        {
            Children =
            {
                Row(new DropIcon(), new Label(chance.Get) { Style = small, Width = 25 }, bar),
                Row(new WindArrow(() => snap()?.WindDirection ?? 0), new Label(wind.Get) { Style = small }, new Label(dir.Get) { Style = tiny, VAlign = Align.Center }),
                Row(new WeatherGlyph(() => (WeatherKind.Clear, true), 12), new Label(sun.Get) { Style = small }),
            },
        });
    }

    public static Node Hours(Func<WeatherSnapshot?> snap, Func<(Pixel, Pixel)> sky)
    {
        var cols = new Grid("*", "*,*,*,*");
        for (int c = 0; c < 4; c++) cols.Add(HourColumn(snap, (c + 1) * 3), 0, c);
        return Page("NEXT HOURS", sky, cols);
    }

    public static Node Days(Func<WeatherSnapshot?> snap, Func<(Pixel, Pixel)> sky)
    {
        var cols = new Grid("*", "*,*,*");
        for (int c = 0; c < 3; c++) cols.Add(DayColumn(snap, c + 1), 0, c);
        return Page("NEXT DAYS", sky, cols);
    }

    private static Node HourColumn(Func<WeatherSnapshot?> snap, int index)
    {
        HourlyPoint? Point() => snap() is { } s && index < s.Hourly.Count ? s.Hourly[index] : null;
        var time = new Memo<HourlyPoint?>(Point, p => p?.LocalTime.ToString("HH:mm") ?? "");
        var temp = new Memo<HourlyPoint?>(Point, p => p is null ? "" : Math.Round(p.Temp) + "°");
        var rain = new Memo<HourlyPoint?>(Point, p => p is { PrecipChance: >= 25 } ? p.PrecipChance + "%" : "");
        return new Stack(Orientation.Vertical, gap: 2)
        {
            CrossAlign = Align.Center,
            Children =
            {
                new Label(time.Get) { Style = new TextStyle(Fonts.ExtraSmall, Dim, Shadow: false) },
                new WeatherGlyph(() => Point() is { } p ? (WeatherCodes.KindOf(p.Code), p.IsDay) : (WeatherKind.Cloudy, true), 16),
                new Label(temp.Get) { Style = new TextStyle(Fonts.Small, Pixel.White) },
                new Label(rain.Get) { Style = new TextStyle(Fonts.ExtraSmall, Cool, Shadow: false) },
            },
        };
    }

    private static Node DayColumn(Func<WeatherSnapshot?> snap, int index)
    {
        DailyPoint? Point() => snap() is { } s && index < s.Daily.Count ? s.Daily[index] : null;
        var name = new Memo<DailyPoint?>(Point, p => p?.Date.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant() ?? "");
        var hi = new Memo<DailyPoint?>(Point, p => p is null ? "" : Math.Round(p.High) + "°");
        var lo = new Memo<DailyPoint?>(Point, p => p is null ? "" : Math.Round(p.Low) + "°");
        return new Stack(Orientation.Vertical, gap: 2)
        {
            CrossAlign = Align.Center,
            Children =
            {
                new Label(name.Get) { Style = new TextStyle(Fonts.QuiteSmall, Dim, Shadow: false) },
                new WeatherGlyph(() => Point() is { } p ? (WeatherCodes.KindOf(p.Code), true) : (WeatherKind.Cloudy, true), 16),
                new Label(hi.Get) { Style = new TextStyle(Fonts.Small, Warm) },
                new Label(lo.Get) { Style = new TextStyle(Fonts.Small, Cool) },
            },
        };
    }

    private static string SunText((WeatherSnapshot? Snap, int Minute) k)
    {
        if (k.Snap is not { } s) return "";
        var tod = TimeSpan.FromMinutes(k.Minute);
        return tod < s.Sunrise ? $"▲ {s.Sunrise:hh\\:mm}" : tod < s.Sunset ? $"▼ {s.Sunset:hh\\:mm}" : $"▲ {s.Sunrise:hh\\:mm}";
    }

    private static Node Row(params Node[] items)
    {
        var row = new Stack(Orientation.Horizontal, gap: 4) { CrossAlign = Align.Center, Height = 13 };
        foreach (var n in items) row.Add(n);
        return row;
    }

    private static Node Page(string title, Func<(Pixel, Pixel)> sky, Node body) => new Panel
    {
        new PanelBackdrop(sky),
        new Stack(Orientation.Vertical, gap: 3)
        {
            Padding = new Thickness(7, 3, 3, 2),
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children =
            {
                new Label(title) { Style = new TextStyle(Fonts.QuiteSmall, new Pixel(205, 220, 255), Shadow: false) },
                body,
            },
        },
    };
}
