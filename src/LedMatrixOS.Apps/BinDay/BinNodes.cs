using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.BinDay;

/// <summary>One line of the next-collection summary, precomputed when the data or date changes.</summary>
internal sealed record SummaryRowData(Pixel Colour, string Name, string Date, string Relative, bool Soon);

/// <summary>Pager token: a page of the summary (two rows) plus a signature so changed content rebuilds the page.</summary>
internal readonly record struct SummaryPage(int Index, string Signature);

internal sealed class BinStyles
{
    public static readonly Pixel Amber = new(255, 190, 40), Ink = new(235, 235, 235), Muted = new(135, 135, 148);

    public readonly TextStyle Name = new(Fonts.Small, Ink, Shadow: false);
    public readonly TextStyle Date = new(Fonts.QuiteSmall, Muted, Shadow: false);
    public readonly TextStyle Relative = new(Fonts.QuiteSmall, Muted, Shadow: false);
    public readonly TextStyle RelativeSoon = new(Fonts.QuiteSmall, Amber, Shadow: false);
    public readonly TextStyle Headline = new(Fonts.Small, Pixel.White, Shadow: true);
    public readonly TextStyle Plate = new(Fonts.QuiteSmall, Pixel.White, Shadow: false);
    public readonly TextStyle Reminder = new(Fonts.Big, Pixel.White, Shadow: true);
    public readonly TextStyle Message = new(Fonts.Small, Amber, Shadow: false);
    public readonly TextStyle Caption = new(Fonts.QuiteSmall, Muted, Shadow: false);
    public readonly TextStyle Clock = new(Fonts.Small, Ink, Shadow: false);
}

/// <summary>
/// A pixel-art wheelie bin drawn from rectangles: tapered body in the bin colour, lid with a handle, two wheels. Idle animation: every
/// few seconds the lid lifts a pixel and the bin rocks one pixel sideways. <see cref="Mini"/> is a small flat version for list rows.
/// </summary>
internal sealed class BinIcon : Node
{
    public const int FullWidth = 22, FullHeight = 34;
    public const int MiniWidth = 11, MiniHeight = 14;
    private static readonly Pixel Wheel = new(70, 70, 78), Dark = new(8, 8, 10);

    private readonly bool _mini;
    private readonly float _phase;
    private TimeSpan _time;

    public BinIcon(bool mini, float phaseSeconds = 0)
    {
        _mini = mini;
        _phase = phaseSeconds;
        Width = mini ? MiniWidth : FullWidth;
        Height = mini ? MiniHeight : FullHeight;
    }

    public Pixel Colour { get; set; } = new(60, 60, 60);

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    private static Pixel Mix(Pixel a, Pixel b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var body = Colour;
        var edge = Mix(body, Pixel.White, 0.45f);
        var lid = Mix(body, Pixel.White, 0.18f);
        var shade = Mix(body, Pixel.Black, 0.35f);

        if (_mini) { RenderMini(frame, bounds, body, edge, lid); return; }

        // Idle: a 4 second cycle; the lid lifts for the first 0.5 s and the bin rocks for the same moment.
        double cycle = (_time.TotalSeconds + _phase) % 4.0;
        bool lift = cycle < 0.5;
        int dx = lift ? 1 : 0;
        int lidLift = lift ? 1 : 0;
        int x = bounds.X + dx, y = bounds.Y;

        // Handle on the lid.
        frame.Fill(new Rectangle(x + 7, y + 1 - lidLift, 8, 1), edge);
        // Lid: wider than the body.
        frame.Fill(new Rectangle(x, y + 2 - lidLift, 22, 4), edge);
        frame.Fill(new Rectangle(x + 1, y + 3 - lidLift, 20, 2), lid);
        if (lift) frame.Fill(new Rectangle(x + 1, y + 5, 20, 1), Dark);

        // Body: tapers towards the base, outlined, with a lit left edge and a shaded right edge.
        const int rows = 24;
        for (int row = 0; row < rows; row++)
        {
            int inset = 1 + row * 3 / rows;
            int left = x + inset, width = 22 - inset * 2;
            int py = y + 6 + row;
            frame.Fill(new Rectangle(left, py, width, 1), body);
            frame.SetPixel(left, py, edge);
            frame.SetPixel(left + width - 1, py, edge);
            frame.SetPixel(left + 1, py, Mix(body, Pixel.White, 0.15f));
            frame.SetPixel(left + width - 2, py, shade);
        }
        frame.Fill(new Rectangle(x + 2, y + 30, 18, 1), edge);

        // Wheels.
        frame.Fill(new Rectangle(x + 3, y + 31, 4, 3), Wheel);
        frame.Fill(new Rectangle(x + 15, y + 31, 4, 3), Wheel);
        frame.SetPixel(x + 4, y + 32, Pixel.White.WithBrightness(0.4f));
        frame.SetPixel(x + 16, y + 32, Pixel.White.WithBrightness(0.4f));
    }

    private static void RenderMini(FrameBuffer frame, Rectangle bounds, Pixel body, Pixel edge, Pixel lid)
    {
        int x = bounds.X, y = bounds.Y;
        frame.Fill(new Rectangle(x, y, 11, 2), edge);
        frame.Fill(new Rectangle(x + 1, y + 1, 9, 1), lid);
        for (int row = 0; row < 9; row++)
        {
            int inset = 1 + row / 5;
            frame.Fill(new Rectangle(x + inset, y + 2 + row, 11 - inset * 2, 1), body);
            frame.SetPixel(x + inset, y + 2 + row, edge);
            frame.SetPixel(x + 10 - inset, y + 2 + row, edge);
        }
        frame.Fill(new Rectangle(x + 2, y + 11, 7, 1), edge);
        frame.Fill(new Rectangle(x + 2, y + 12, 2, 2), Wheel);
        frame.Fill(new Rectangle(x + 7, y + 12, 2, 2), Wheel);
    }
}

/// <summary>A background that breathes between two brightness levels of a colour (the active reminder card).</summary>
internal sealed class PulseBlock : Node
{
    private TimeSpan _time;

    public Pixel Colour { get; set; } = new(150, 90, 0);

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float wave = 0.5f + 0.5f * MathF.Sin((float)_time.TotalSeconds * 2.6f);
        frame.Fill(bounds, Colour.WithBrightness(0.25f + 0.35f * wave));
    }
}

/// <summary>One summary line: mini bin, name, date, and how far away it is.</summary>
internal sealed class SummaryRow : Stack
{
    public SummaryRow(SummaryRowData row, BinStyles styles) : base(Orientation.Horizontal, gap: 6)
    {
        HAlign = Align.Stretch;
        Height = 18;
        CrossAlign = Align.Center;
        Children.Add(new BinIcon(mini: true) { Colour = row.Colour });
        Children.Add(new MarqueeLabel(row.Name) { Style = styles.Name, Width = 66 });
        Children.Add(new Label(row.Date) { Style = styles.Date, Width = 56 });
        Children.Add(new Label(row.Relative) { Style = row.Soon ? styles.RelativeSoon : styles.Relative, Grow = 1, HAlign = Align.Stretch });
    }
}
