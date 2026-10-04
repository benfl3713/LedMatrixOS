using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.ISS;

/// <summary>Colours and text styles of the ISS Tracker. Created in <c>Build</c>, once the fonts are loaded.</summary>
internal sealed class IssStyles
{
    public static readonly Pixel Amber = new(255, 176, 0);
    public static readonly Pixel Sky = new(120, 200, 255);
    public static readonly Pixel Dim = new(120, 130, 150);
    public static readonly Pixel Gold = new(255, 214, 90);

    public readonly TextStyle Title = new(Fonts.QuiteSmall, Amber, Shadow: false);
    public readonly TextStyle Region = new(Fonts.QuiteSmall, Sky, Shadow: false);
    public readonly TextStyle Speed = new(Fonts.Small, Pixel.White, Shadow: false);
    public readonly TextStyle Muted = new(Fonts.QuiteSmall, Dim, Shadow: false);
    public readonly TextStyle Altitude = new(Fonts.QuiteSmall, Gold, Shadow: false);
    public readonly TextStyle Coords = new(Fonts.QuiteSmall, new Pixel(235, 235, 240), Shadow: false);
    public readonly TextStyle Distance = new(Fonts.QuiteSmall, new Pixel(150, 190, 255), Shadow: false);
    public readonly TextStyle Band = new(Fonts.QuiteSmall, Pixel.Black, Shadow: false);
    public readonly TextStyle Message = new(Fonts.Small, Amber, Shadow: false);
}

/// <summary>
/// The world map: land and sea from the precomputed cell grid shaded by day and night, the station with its past and coming ground track,
/// your own position, and a pulse plus sweep around the station while it is within range of you. Time comes from the frame clock.
/// </summary>
internal sealed class IssMapNode(IssModel model) : Node
{
    public const int MarginX = 1, MarginY = 3;
    public const double SweepSeconds = 2.5, PulseSeconds = 2.0;

    private static readonly Pixel Trail = new(255, 214, 90), Ahead = new(150, 170, 220), Panel = new(70, 140, 255), Truss = new(190, 200, 215), Home = new(0, 220, 255);
    private TimeSpan _time;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int ox = bounds.X + MarginX, oy = bounds.Y + MarginY;
        var palette = WorldMap.Palette;
        var shade = model.Shade;
        for (int y = 0; y < WorldMap.Height; y++)
            for (int x = 0; x < WorldMap.Width; x++)
                frame.SetPixel(ox + x, oy + y, palette[WorldMap.CellAt(x, y) * 3 + shade[y * WorldMap.Width + x]]);

        if (model.HomeX >= 0) DrawHome(frame, ox + model.HomeX, oy + model.HomeY);
        if (!model.HasPosition) return;

        DrawTrack(frame, ox, oy);
        int cx = ox + model.DotX, cy = oy + model.DotY;
        double t = _time.TotalSeconds;
        if (model.InRange && !model.Stale)
        {
            frame.PushClip(new Rectangle(ox, oy, WorldMap.Width, WorldMap.Height));
            DrawRange(frame, cx, cy, t);
            frame.PopClip();
        }
        DrawStation(frame, cx, cy, t);
    }

    private static void DrawHome(FrameBuffer frame, int x, int y)
    {
        frame.SetPixel(x, y, Pixel.White);
        frame.SetPixel(x - 1, y, Home);
        frame.SetPixel(x + 1, y, Home);
        frame.SetPixel(x, y - 1, Home);
        frame.SetPixel(x, y + 1, Home);
    }

    private void DrawTrack(FrameBuffer frame, int ox, int oy)
    {
        int px = model.DotX, py = model.DotY;
        for (int i = 0; i < model.PastCount; i++)
        {
            int x = model.PastX[i], y = model.PastY[i];
            if (Math.Abs(x - px) < WorldMap.Width / 2)
                frame.DrawLine(ox + px, oy + py, ox + x, oy + y, Trail.WithBrightness(1f - 0.8f * i / model.PastCount));
            px = x;
            py = y;
        }

        for (int i = 0; i < model.FutureCount; i++)
            frame.SetPixel(ox + model.FutureX[i], oy + model.FutureY[i], Ahead.WithBrightness(1f - 0.55f * i / model.FutureCount));
    }

    private static void DrawStation(FrameBuffer frame, int cx, int cy, double t)
    {
        float breathe = 0.7f + 0.3f * MathF.Sin((float)t * 4f);
        for (int dy = -1; dy <= 1; dy++)
        {
            frame.SetPixel(cx - 2, cy + dy, Panel);
            frame.SetPixel(cx + 2, cy + dy, Panel);
        }
        frame.SetPixel(cx - 1, cy, Truss);
        frame.SetPixel(cx + 1, cy, Truss);
        frame.SetPixel(cx, cy - 1, Pixel.White.WithBrightness(breathe));
        frame.SetPixel(cx, cy, Pixel.White);
        frame.SetPixel(cx, cy + 1, Pixel.White.WithBrightness(breathe));
    }

    private static void DrawRange(FrameBuffer frame, int cx, int cy, double t)
    {
        double phase = t / IssMapNode.PulseSeconds % 1.0;
        int radius = 4 + (int)(phase * 12);
        frame.DrawCircle(cx, cy, radius, IssStyles.Amber.WithBrightness((float)(1.0 - phase)));

        double angle = t / SweepSeconds * 2 * Math.PI;
        for (int k = 4; k >= 0; k--)
        {
            double a = angle - k * 0.2;
            frame.DrawLine(cx, cy, cx + (int)Math.Round(Math.Sin(a) * 9), cy - (int)Math.Round(Math.Cos(a) * 9),
                IssStyles.Amber.WithBrightness(k == 0 ? 1f : 0.5f / k));
        }
    }
}

/// <summary>A solid block that breathes between two brightnesses while <paramref name="active"/> is true, and is invisible otherwise.</summary>
internal sealed class PulseBlock(Func<bool> active, Pixel color) : Node
{
    private TimeSpan _time;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (!active()) return;
        float pulse = 0.65f + 0.35f * MathF.Sin((float)_time.TotalSeconds * 5f);
        frame.FillRect(bounds, color.WithBrightness(pulse));
    }
}
