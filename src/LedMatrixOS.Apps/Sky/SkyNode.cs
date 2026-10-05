using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Sky;

/// <summary>Sky colours by sun altitude: night, twilight, the orange of sunrise and sunset, then day blue.</summary>
internal static class SkyPalette
{
    private static readonly (double Alt, Pixel Zenith, Pixel Horizon)[] Keys =
    [
        (-18, new Pixel(3, 4, 16), new Pixel(8, 10, 28)),
        (-12, new Pixel(8, 10, 38), new Pixel(24, 24, 66)),
        (-6, new Pixel(22, 28, 80), new Pixel(150, 72, 92)),
        (-1, new Pixel(46, 74, 140), new Pixel(255, 130, 64)),
        (5, new Pixel(66, 120, 205), new Pixel(255, 196, 130)),
        (20, new Pixel(48, 118, 225), new Pixel(150, 200, 248)),
        (45, new Pixel(30, 100, 225), new Pixel(120, 185, 245)),
    ];

    public static void Evaluate(double altitude, out Pixel zenith, out Pixel horizon)
    {
        if (altitude <= Keys[0].Alt) { zenith = Keys[0].Zenith; horizon = Keys[0].Horizon; return; }
        for (int i = 1; i < Keys.Length; i++)
        {
            if (altitude > Keys[i].Alt) continue;
            var (a0, z0, h0) = Keys[i - 1];
            var (a1, z1, h1) = Keys[i];
            float t = (float)((altitude - a0) / (a1 - a0));
            zenith = Pixel.Lerp(z0, z1, t);
            horizon = Pixel.Lerp(h0, h1, t);
            return;
        }

        zenith = Keys[^1].Zenith;
        horizon = Keys[^1].Horizon;
    }
}

/// <summary>
/// The whole panel as the sky: a gradient from the sun altitude, stars at night, the sun or moon on a dotted arc, weather-driven clouds and
/// precipitation, and a hill silhouette along the bottom. State is pushed in by <see cref="SkyClockApp"/>; drawing allocates nothing.
/// </summary>
internal sealed class SkyNode : Node
{
    private const int StarCount = 70, CloudCount = 14, DropCount = 110;

    private int _w, _h;
    private int[] _hill = [];
    private int[] _far = [];
    private readonly float[] _starX = new float[StarCount], _starY = new float[StarCount], _starBase = new float[StarCount], _starSpeed = new float[StarCount], _starPhase = new float[StarCount];
    private readonly float[] _cloudX = new float[CloudCount], _cloudY = new float[CloudCount], _cloudRx = new float[CloudCount], _cloudRy = new float[CloudCount], _cloudSpeed = new float[CloudCount];
    private readonly float[] _dropX = new float[DropCount], _dropOff = new float[DropCount], _dropSpeed = new float[DropCount];

    public SkyNode()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;

        var rng = new Random(2026);
        for (int i = 0; i < StarCount; i++)
        {
            _starX[i] = (float)rng.NextDouble();
            _starY[i] = (float)rng.NextDouble();
            _starBase[i] = 0.45f + 0.55f * (float)rng.NextDouble();
            _starSpeed[i] = 0.8f + 2.2f * (float)rng.NextDouble();
            _starPhase[i] = (float)(rng.NextDouble() * Math.PI * 2);
        }

        for (int i = 0; i < CloudCount; i++)
        {
            _cloudX[i] = (float)rng.NextDouble() * 400;
            _cloudY[i] = 0.08f + 0.5f * (float)rng.NextDouble();
            _cloudRx[i] = 14 + 14 * (float)rng.NextDouble();
            _cloudRy[i] = 3.5f + 2.5f * (float)rng.NextDouble();
            _cloudSpeed[i] = 1.2f + 2.6f * (float)rng.NextDouble();
        }

        for (int i = 0; i < DropCount; i++)
        {
            _dropX[i] = (float)rng.NextDouble();
            _dropOff[i] = (float)rng.NextDouble() * 100;
            _dropSpeed[i] = 0.7f + 0.6f * (float)rng.NextDouble();
        }
    }

    /// <summary>Sun altitude in degrees used for the sky colours (may be pushed up by the alarm).</summary>
    public double Altitude { get; set; } = 30;
    public bool SunVisible { get; set; }
    public float SunX { get; set; }
    public float SunY { get; set; }
    public bool MoonVisible { get; set; }
    public float MoonX { get; set; }
    public float MoonY { get; set; }
    public double MoonAge { get; set; }
    public WeatherKind Kind { get; set; } = WeatherKind.Clear;
    public double Seconds { get; set; }

    public int HorizonY => (_h > 0 ? _h : 64) - 10;

    private void EnsureLayout(int w, int h)
    {
        if (w == _w && h == _h) return;
        _w = w;
        _h = h;
        _hill = new int[w];
        _far = new int[w];
        for (int x = 0; x < w; x++)
        {
            double u = x / (double)w * Math.PI * 2;
            _hill[x] = (int)Math.Round(3.2 + 1.8 * Math.Sin(u * 3 + 0.6) + 1.2 * Math.Sin(u * 7 + 2.0));
            _far[x] = (int)Math.Round(6.5 + 2.2 * Math.Sin(u * 2 + 2.4) + 1.4 * Math.Sin(u * 5 + 0.3));
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        EnsureLayout(bounds.Width, bounds.Height);
        int hy = bounds.Bottom - 10;
        SkyPalette.Evaluate(Altitude, out var zenith, out var horizon);
        float murk = Murk(Kind);
        if (murk > 0)
        {
            zenith = Dull(zenith, murk);
            horizon = Dull(horizon, murk);
        }

        // Gradient
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            float t = Math.Clamp((y - bounds.Top) / (float)Math.Max(1, hy - bounds.Top), 0f, 1f);
            frame.Fill(new Rectangle(bounds.X, y, bounds.Width, 1), Pixel.Lerp(zenith, horizon, t * t * 0.4f + t * 0.6f));
        }

        float night = (float)Math.Clamp((-Altitude - 3) / 10, 0, 1);
        if (night > 0.01f) DrawStars(frame, bounds, hy, night);
        DrawArc(frame, bounds, hy);
        if (SunVisible) DrawSun(frame, bounds);
        if (MoonVisible) DrawMoon(frame, bounds, night);

        DrawClouds(frame, bounds, hy, horizon);
        DrawPrecipitation(frame, bounds, hy);
        if (Kind == WeatherKind.Thunderstorm && Seconds % 7 < 0.18)
            for (int y = bounds.Top; y < hy; y++)
                for (int x = bounds.Left; x < bounds.Right; x++)
                    frame.BlendPixel(x, y, new Pixel(230, 235, 255), 0.35f);

        DrawGround(frame, bounds, hy, horizon);
    }

    /// <summary>How much the weather drains the colour from the sky (0 clear, towards 1 stormy).</summary>
    private static float Murk(WeatherKind kind) => kind switch
    {
        WeatherKind.PartlyCloudy => 0.1f,
        WeatherKind.Cloudy => 0.45f,
        WeatherKind.Fog => 0.5f,
        WeatherKind.Drizzle => 0.6f,
        WeatherKind.Rain => 0.68f,
        WeatherKind.Snow => 0.5f,
        WeatherKind.Thunderstorm => 0.82f,
        _ => 0f,
    };

    private static Pixel Dull(Pixel c, float amount)
    {
        byte grey = (byte)(0.3f * c.R + 0.59f * c.G + 0.11f * c.B);
        return Pixel.Lerp(c, new Pixel(grey, grey, grey), amount).WithBrightness(1f - 0.3f * amount);
    }

    private void DrawStars(FrameBuffer frame, Rectangle b, int hy, float night)
    {
        for (int i = 0; i < StarCount; i++)
        {
            int x = b.X + (int)(_starX[i] * b.Width);
            int y = b.Y + (int)(_starY[i] * Math.Max(1, hy - b.Y - 6));
            float twinkle = 0.65f + 0.35f * MathF.Sin((float)Seconds * _starSpeed[i] + _starPhase[i]);
            float v = _starBase[i] * twinkle * night;
            frame.BlendPixel(x, y, new Pixel(235, 240, 255), Math.Min(1f, v));
        }
    }

    /// <summary>The path the sun (by day) or moon (by night) follows, as a faint dotted line.</summary>
    private void DrawArc(FrameBuffer frame, Rectangle b, int hy)
    {
        for (int x = b.X + 8; x < b.Right - 8; x += 4)
        {
            float p = (x - b.X - 8f) / (b.Width - 16f);
            frame.BlendPixel(x, (int)MathF.Round(ArcY(p, hy, b.Y)), Pixel.White, 0.16f);
        }
    }

    /// <summary>Screen y of the arc at progress <paramref name="p"/> (0 rises, 1 sets).</summary>
    public static float ArcY(float p, int hy, int top = 0) => hy + 3 - MathF.Sin(MathF.PI * p) * (hy + 3 - top - 9);

    private void DrawSun(FrameBuffer frame, Rectangle b)
    {
        float low = (float)Math.Clamp(Altitude / 20, 0, 1);
        var core = Pixel.Lerp(new Pixel(255, 110, 40), new Pixel(255, 244, 175), low);
        int cx = b.X + (int)MathF.Round(SunX), cy = b.Y + (int)MathF.Round(SunY);
        const int glow = 11;
        for (int dy = -glow; dy <= glow; dy++)
            for (int dx = -glow; dx <= glow; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > glow) continue;
                if (d <= 3.4f) frame.SetPixel(cx + dx, cy + dy, core);
                else frame.BlendPixel(cx + dx, cy + dy, core, 0.55f * (1 - d / glow) * (1 - d / glow));
            }
    }

    private void DrawMoon(FrameBuffer frame, Rectangle b, float night)
    {
        int cx = b.X + (int)MathF.Round(MoonX), cy = b.Y + (int)MathF.Round(MoonY);
        const int r = 4;
        double k = Math.Cos(2 * Math.PI * MoonAge);
        bool waxing = MoonAge < 0.5;
        var lit = new Pixel(238, 236, 214);
        var dark = new Pixel(30, 34, 54);
        for (int dy = -r; dy <= r; dy++)
        {
            double w = Math.Sqrt(Math.Max(0, r * r + 0.5 - dy * dy));
            double term = k * w;
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > 18) continue;
                bool isLit = waxing ? dx > term : dx < -term;
                frame.SetPixel(cx + dx, cy + dy, isLit ? lit : Pixel.Lerp(new Pixel(14, 16, 30), dark, night));
            }
        }

        for (int dy = -9; dy <= 9; dy++)
            for (int dx = -9; dx <= 9; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 4.6f && d < 9) frame.BlendPixel(cx + dx, cy + dy, lit, 0.16f * (1 - d / 9) * night);
            }
    }

    private void DrawClouds(FrameBuffer frame, Rectangle b, int hy, Pixel horizon)
    {
        int count; float dark; float alpha = 0.9f;
        switch (Kind)
        {
            case WeatherKind.PartlyCloudy: count = 4; dark = 0.05f; break;
            case WeatherKind.Cloudy: count = 14; dark = 0.3f; break;
            case WeatherKind.Fog: count = 14; dark = 0.1f; alpha = 0.55f; break;
            case WeatherKind.Drizzle: count = 14; dark = 0.5f; break;
            case WeatherKind.Rain: count = 14; dark = 0.6f; break;
            case WeatherKind.Snow: count = 14; dark = 0.25f; break;
            case WeatherKind.Thunderstorm: count = 14; dark = 0.85f; break;
            default: return;
        }

        float light = (float)Math.Clamp((Altitude + 8) / 22, 0.14, 1);
        var baseColor = Pixel.Lerp(new Pixel(240, 242, 248), new Pixel(96, 102, 118), dark).WithBrightness(light);
        if (Altitude < 10) baseColor = Pixel.Lerp(baseColor, horizon, 0.18f);

        float room = hy - 14 - b.Y;
        for (int i = 0; i < count; i++)
        {
            float rx = _cloudRx[i], ry = _cloudRy[i];
            float span = b.Width + rx * 4;
            float cx = b.X + (_cloudX[i] + (float)Seconds * _cloudSpeed[i]) % span - rx * 2;
            float cy = b.Y + 3 + _cloudY[i] * room;
            Lobe(frame, cx, cy, rx, ry, baseColor, alpha);
            Lobe(frame, cx - rx * 0.35f, cy - ry * 0.6f, rx * 0.5f, ry * 0.85f, baseColor, alpha);
            Lobe(frame, cx + rx * 0.3f, cy - ry * 0.45f, rx * 0.45f, ry * 0.75f, baseColor, alpha);
        }
    }

    private static void Lobe(FrameBuffer frame, float cx, float cy, float rx, float ry, Pixel color, float alpha)
    {
        int x0 = (int)MathF.Floor(cx - rx), x1 = (int)MathF.Ceiling(cx + rx), y0 = (int)MathF.Floor(cy - ry), y1 = (int)MathF.Ceiling(cy + ry);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                float d2 = dx * dx + dy * dy;
                if (d2 >= 1) continue;
                frame.BlendPixel(x, y, color, alpha * MathF.Min(1f, (1 - d2) * 3f));
            }
    }

    private void DrawPrecipitation(FrameBuffer frame, Rectangle b, int hy)
    {
        int count; bool snow = false;
        switch (Kind)
        {
            case WeatherKind.Drizzle: count = 40; break;
            case WeatherKind.Rain: count = 80; break;
            case WeatherKind.Thunderstorm: count = DropCount; break;
            case WeatherKind.Snow: count = 70; snow = true; break;
            default: return;
        }

        float fall = hy - b.Y;
        for (int i = 0; i < count; i++)
        {
            float speed = snow ? 7 * _dropSpeed[i] : 55 * _dropSpeed[i];
            float y = (_dropOff[i] / 100f * fall + (float)Seconds * speed) % fall;
            float x = b.X + _dropX[i] * b.Width + (snow ? MathF.Sin((float)Seconds * 0.8f + _dropOff[i]) * 2f : y * 0.12f);
            int px = (int)MathF.Round(x), py = b.Y + (int)y;
            if (snow) frame.BlendPixel(px, py, Pixel.White, 0.85f);
            else
            {
                frame.BlendPixel(px, py, new Pixel(205, 222, 255), 0.8f);
                frame.BlendPixel(px, py - 1, new Pixel(205, 222, 255), 0.45f);
            }
        }
    }

    private void DrawGround(FrameBuffer frame, Rectangle b, int hy, Pixel horizon)
    {
        var far = Pixel.Lerp(horizon, new Pixel(6, 8, 14), 0.72f);
        var near = Pixel.Lerp(horizon, new Pixel(3, 4, 7), 0.9f);
        for (int x = 0; x < b.Width; x++)
        {
            int farTop = hy + 4 - _far[x] + 3, nearTop = hy + 4 - _hill[x] + 3;
            frame.Fill(new Rectangle(b.X + x, farTop, 1, b.Bottom - farTop), far);
            frame.Fill(new Rectangle(b.X + x, nearTop, 1, b.Bottom - nearTop), near);
        }
    }
}
