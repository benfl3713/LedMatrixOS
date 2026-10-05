using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Tides;

/// <summary>
/// A water surface whose height follows the tide (<see cref="Level"/>, 0 at the lowest tide of the forecast, 1 at the highest), under a dusk sky,
/// with a buoy bobbing on the waves and the next two high/low waters written across the top. Drawing allocates nothing.
/// </summary>
internal sealed class TideNode : Node
{
    public const int WaterTop = 24, WaterBottom = 52;     // waterline at the highest / lowest tide

    private readonly TextRun _first = new(), _firstHeight = new(), _second = new(), _secondHeight = new(), _place = new(), _status = new();
    private Pixel[] _rows = [];
    private int _rowsHeight;

    public TideNode()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public double Level { get; set; } = 0.5;
    public double Seconds { get; set; }
    public TideKind? FirstKind { get; private set; }
    public TideKind? SecondKind { get; private set; }

    /// <summary>The y of the waterline for the current level (before waves).</summary>
    public float Waterline => WaterBottom + (float)Math.Clamp(Level, 0, 1) * (WaterTop - WaterBottom);

    public void SetFirst(TideKind? kind, string label, string height) { FirstKind = kind; _first.Set(Fonts.QuiteSmall, label); _firstHeight.Set(Fonts.QuiteSmall, height); }
    public void SetSecond(TideKind? kind, string label, string height) { SecondKind = kind; _second.Set(Fonts.QuiteSmall, label); _secondHeight.Set(Fonts.QuiteSmall, height); }
    public void SetPlace(string text) => _place.Set(Fonts.QuiteSmall, text);
    public void SetStatus(string text) => _status.Set(Fonts.QuiteSmall, text);

    private static readonly Pixel High = new(140, 225, 255), Low = new(255, 196, 120), Soft = new(210, 220, 235);

    private static Pixel ColorOf(TideKind? kind) => kind switch { TideKind.High => High, TideKind.Low => Low, _ => Soft };

    private static float Wave(float x, double t) =>
        2.1f * MathF.Sin(x * 0.085f + (float)t * 1.5f) + 1.2f * MathF.Sin(x * 0.21f - (float)t * 2.3f) + 0.9f * MathF.Sin(x * 0.04f + (float)t * 0.6f);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (bounds.Height != _rowsHeight)
        {
            _rowsHeight = bounds.Height;
            _rows = new Pixel[bounds.Height];
        }

        // Sky: deep blue at the top, a pale glow at the horizon.
        for (int y = 0; y < bounds.Height; y++)
        {
            float t = y / (float)Math.Max(1, bounds.Height - 1);
            _rows[y] = Pixel.Lerp(new Pixel(74, 190, 228), new Pixel(8, 40, 100), t);   // water colour by depth
            frame.Fill(new Rectangle(bounds.X, bounds.Y + y, bounds.Width, 1), Pixel.Lerp(new Pixel(14, 22, 52), new Pixel(70, 100, 150), MathF.Pow(t, 1.4f)));
        }

        float baseLine = bounds.Y + Waterline;
        for (int x = 0; x < bounds.Width; x++)
        {
            float surface = baseLine + Wave(x, Seconds);
            int top = (int)MathF.Round(surface);
            for (int y = Math.Max(top, bounds.Y); y < bounds.Bottom; y++)
                frame.SetPixel(bounds.X + x, y, _rows[y - bounds.Y]);

            // Crest highlight, with a bit of foam on the taller peaks.
            frame.SetPixel(bounds.X + x, top, new Pixel(170, 232, 248));
            if (surface < baseLine - 2.4f) frame.SetPixel(bounds.X + x, top - 1, new Pixel(235, 248, 255));
        }

        DrawBuoy(frame, bounds, baseLine);

        // Labels
        const int pad = 3;
        _first.Draw(frame, bounds.X + pad, bounds.Y + 1, ColorOf(FirstKind), shadow: true);
        _firstHeight.Draw(frame, bounds.X + pad, bounds.Y + 9, Pixel.White, shadow: true);
        _second.Draw(frame, bounds.Right - pad - _second.Width, bounds.Y + 1, ColorOf(SecondKind), shadow: true);
        _secondHeight.Draw(frame, bounds.Right - pad - _secondHeight.Width, bounds.Y + 9, Pixel.White, shadow: true);
        _place.Draw(frame, bounds.X + (bounds.Width - _place.Width) / 2, bounds.Y + 1, Soft, shadow: true);
        _status.Draw(frame, bounds.X + (bounds.Width - _status.Width) / 2, bounds.Y + 9, Pixel.White, shadow: true);
    }

    private void DrawBuoy(FrameBuffer frame, Rectangle bounds, float baseLine)
    {
        int bx = bounds.X + bounds.Width * 3 / 4;
        int y = (int)MathF.Round(baseLine + Wave(bx - bounds.X, Seconds));
        var white = new Pixel(240, 240, 240);
        var red = new Pixel(220, 40, 40);
        for (int dy = 1; dy <= 6; dy++)
            for (int dx = -1; dx <= 1; dx++)
                frame.SetPixel(bx + dx, y - dy, dy is 2 or 3 ? red : white);
        frame.SetPixel(bx, y - 7, white);
        frame.SetPixel(bx, y - 8, new Pixel(255, 220, 80));
    }
}
