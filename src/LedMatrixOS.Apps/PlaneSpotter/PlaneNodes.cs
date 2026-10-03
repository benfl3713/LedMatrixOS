using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.PlaneSpotter;

/// <summary>Colours and text styles of the Plane Spotter. Created in <c>Build</c>, once the fonts are loaded.</summary>
internal sealed class PlaneStyles
{
    public static readonly Pixel Sky = new(120, 200, 255);
    public static readonly Pixel Green = new(70, 225, 105);
    public static readonly Pixel Amber = new(255, 176, 0);
    public static readonly Pixel Dim = new(120, 130, 150);
    public static readonly Pixel Ink = new(235, 235, 240);

    public readonly TextStyle Callsign = new(Fonts.Big, Pixel.White, Shadow: false);
    public readonly TextStyle Operator = new(Fonts.QuiteSmall, new Pixel(150, 190, 255), Shadow: false);
    public readonly TextStyle Altitude = new(Fonts.Small, new Pixel(255, 214, 90), Shadow: false);
    public readonly TextStyle Detail = new(Fonts.QuiteSmall, Ink, Shadow: false);
    public readonly TextStyle Muted = new(Fonts.QuiteSmall, Dim, Shadow: false);
    public readonly TextStyle RowCall = new(Fonts.QuiteSmall, Pixel.White, Shadow: false);
    public readonly TextStyle RowAlt = new(Fonts.QuiteSmall, new Pixel(255, 214, 90), Shadow: false);
    public readonly TextStyle RowDist = new(Fonts.QuiteSmall, new Pixel(150, 190, 255), Shadow: false);
    public readonly TextStyle Strip = new(Fonts.QuiteSmall, new Pixel(190, 190, 200), Shadow: false);
    public readonly TextStyle Clock = new(Fonts.Small, Pixel.White, Shadow: false);
    public readonly TextStyle MessageBig = new(Fonts.Big, Pixel.White, Shadow: false);
    public readonly TextStyle Message = new(Fonts.Small, Amber, Shadow: false);
}

/// <summary>A following aircraft: callsign, altitude and distance on one 9px line.</summary>
internal sealed class PlaneRowNode : Stack
{
    public const int RowHeight = 9;

    public PlaneRowNode(PlaneRow row, PlaneStyles styles) : base(Orientation.Horizontal)
    {
        CrossAlign = Align.Center;
        Add(new Panel { Width = 38, Children = { new Label(() => row.Callsign) { Style = styles.RowCall } } });
        Add(new Panel { Width = 28, Children = { new Label(() => row.AltText) { Style = styles.RowAlt } } });
        Add(new Panel { Grow = 1, HAlign = Align.Stretch, Children = { new Label(() => row.DistText) { Style = styles.RowDist } } });
    }
}

/// <summary>A compass chip: a ring with a north tick and a dart pointing along the track (nothing when the heading is unknown).</summary>
internal sealed class HeadingChip(Func<PlaneRow?> row) : Node
{
    public static readonly Pixel Ring = new(60, 80, 120);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int r = Math.Min(bounds.Width, bounds.Height) / 2 - 1;
        int cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
        frame.DrawCircle(cx, cy, r, Ring);
        frame.SetPixel(cx, cy - r, new Pixel(255, 90, 70));   // north
        if (row() is not { HasTrack: true } p) return;

        float rad = p.Track * MathF.PI / 180f;
        float dx = MathF.Sin(rad), dy = -MathF.Cos(rad);
        float len = r - 1.5f;
        int tipX = cx + (int)MathF.Round(dx * len), tipY = cy + (int)MathF.Round(dy * len);
        int tailX = cx - (int)MathF.Round(dx * len * 0.7f), tailY = cy - (int)MathF.Round(dy * len * 0.7f);
        float bx = cx - dx * len * 0.25f, by = cy - dy * len * 0.25f;
        float wing = len * 0.55f;
        int w1x = (int)MathF.Round(bx - dy * wing), w1y = (int)MathF.Round(by + dx * wing);
        int w2x = (int)MathF.Round(bx + dy * wing), w2y = (int)MathF.Round(by - dx * wing);
        var c = PlaneStyles.Sky;
        frame.DrawLine(tipX, tipY, w1x, w1y, c);
        frame.DrawLine(tipX, tipY, w2x, w2y, c);
        frame.DrawLine(w1x, w1y, tailX, tailY, c);
        frame.DrawLine(w2x, w2y, tailX, tailY, c);
        frame.DrawLine(tipX, tipY, tailX, tailY, c);
    }
}

/// <summary>A small up (climbing, green) or down (descending, amber) arrow; nothing when level.</summary>
internal sealed class ClimbArrow(Func<int> state) : Node
{
    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int s = state();
        if (s == 0) return;
        var c = s > 0 ? PlaneStyles.Green : PlaneStyles.Amber;
        int cx = bounds.X + bounds.Width / 2, top = bounds.Y, bottom = bounds.Bottom - 1;
        int half = Math.Min(3, bounds.Width / 2);
        if (s > 0)
        {
            frame.DrawLine(cx, top, cx, bottom, c);
            frame.DrawLine(cx, top, cx - half, top + half, c);
            frame.DrawLine(cx, top, cx + half, top + half, c);
        }
        else
        {
            frame.DrawLine(cx, top, cx, bottom, c);
            frame.DrawLine(cx, bottom, cx - half, bottom - half, c);
            frame.DrawLine(cx, bottom, cx + half, bottom - half, c);
        }
    }
}

/// <summary>
/// The radar: concentric rings around your position, aircraft as dots placed by bearing and distance (the nearest highlighted),
/// and a sweep line driven by the frame clock.
/// </summary>
internal sealed class RadarNode(PlaneBoardModel model) : Node
{
    public const double SweepSeconds = 4.0;
    private static readonly Pixel RingColor = new(0, 70, 40), Cross = new(0, 45, 28), SweepColor = new(60, 255, 130);
    private TimeSpan _time;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int radius = Math.Min(bounds.Width, bounds.Height) / 2 - 1;
        int cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
        frame.FillCircle(cx, cy, radius, new Pixel(0, 14, 8));
        frame.DrawLine(cx - radius, cy, cx + radius, cy, Cross);
        frame.DrawLine(cx, cy - radius, cx, cy + radius, Cross);
        frame.DrawCircle(cx, cy, radius / 3, RingColor);
        frame.DrawCircle(cx, cy, radius * 2 / 3, RingColor);
        frame.DrawCircle(cx, cy, radius, new Pixel(0, 110, 60));

        double angle = _time.TotalSeconds / SweepSeconds * 2 * Math.PI;
        for (int k = 5; k >= 0; k--)
        {
            double a = angle - k * 0.13;
            float fade = k == 0 ? 1f : 0.45f / k;
            frame.DrawLine(cx, cy, cx + (int)Math.Round(Math.Sin(a) * (radius - 1)), cy - (int)Math.Round(Math.Cos(a) * (radius - 1)), SweepColor.WithBrightness(fade));
        }

        var all = model.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var p = all[i];
            int x = cx + (int)MathF.Round(p.RadarX * (radius - 1)), y = cy + (int)MathF.Round(p.RadarY * (radius - 1));
            if (i == 0)
            {
                float pulse = 0.65f + 0.35f * MathF.Sin((float)_time.TotalSeconds * 6f);
                frame.FillRect(new Rectangle(x - 1, y - 1, 3, 3), new Pixel(255, 214, 90).WithBrightness(pulse));
                frame.SetPixel(x, y, Pixel.White);
            }
            else
            {
                frame.FillRect(new Rectangle(x, y, 2, 2), new Pixel(120, 220, 255));
            }
        }

        frame.SetPixel(cx, cy, Pixel.White);   // you are here
        frame.SetPixel(cx - 1, cy, new Pixel(200, 200, 200));
        frame.SetPixel(cx + 1, cy, new Pixel(200, 200, 200));
        frame.SetPixel(cx, cy - 1, new Pixel(200, 200, 200));
        frame.SetPixel(cx, cy + 1, new Pixel(200, 200, 200));
    }
}

/// <summary>A calm night sky: twinkling stars and a slow cloud, driven by the frame clock.</summary>
internal sealed class SkyNode : Node
{
    private const int Stars = 28;
    private readonly int[] _x = new int[Stars], _y = new int[Stars];
    private readonly float[] _phase = new float[Stars];
    private TimeSpan _time;

    public SkyNode()
    {
        uint s = 12345;
        for (int i = 0; i < Stars; i++)
        {
            s = s * 1664525u + 1013904223u;
            _x[i] = (int)(s >> 8) % 1000;
            s = s * 1664525u + 1013904223u;
            _y[i] = (int)(s >> 8) % 1000;
            _phase[i] = (i * 0.37f) % 1f;
        }
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        frame.FillLinearGradient(bounds, new Pixel(6, 10, 30), new Pixel(14, 24, 52), vertical: true);
        double t = _time.TotalSeconds;
        for (int i = 0; i < Stars; i++)
        {
            float tw = 0.25f + 0.75f * (0.5f + 0.5f * MathF.Sin((float)(t * 1.3) + _phase[i] * 6.28f));
            frame.SetPixel(bounds.X + _x[i] * bounds.Width / 1000, bounds.Y + _y[i] * bounds.Height / 1000, new Pixel(210, 220, 255).WithBrightness(tw));
        }

        // a cloud drifting left to right, wrapping every 40 seconds
        int span = bounds.Width + 40;
        int cx = bounds.X - 20 + (int)(t / 40.0 % 1.0 * span);
        int cy = bounds.Y + bounds.Height - 14;
        var c = new Pixel(40, 52, 84);
        frame.FillEllipse(new Rectangle(cx, cy + 2, 22, 7), c);
        frame.FillEllipse(new Rectangle(cx + 4, cy - 2, 12, 9), c);
        frame.FillEllipse(new Rectangle(cx + 12, cy, 12, 8), c);
    }
}
