using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Commute;

/// <summary>
/// The hero of the commute board: how long until you must leave, in big digits that change colour as it gets urgent
/// (green, amber, then a flashing red GO plate), with the train you are aiming for beside it. Allocation-free once the strings are cached.
/// </summary>
internal sealed class LeaveCard : Node
{
    private static readonly Pixel Relaxed = new(96, 230, 130);
    private static readonly Pixel Soon = new(255, 190, 40);
    private static readonly Pixel Hurry = new(255, 70, 60);

    private const int DigitsWidth = 40;

    private readonly BdfFont _huge = Fonts.Big.Scale(2);
    private readonly TextRun _caption = new(), _number = new(), _unit = new(), _dest = new(), _detailLine = new(), _walkLine = new(), _go = new(), _title = new(), _sub = new();
    private TimeSpan _time;
    private TimeSpan _bumpAt = TimeSpan.MinValue;
    private int _shownMinutes = -1;
    private string? _destKey, _lineKey;
    private int _detailMinutes = -1, _detailWalk = -1;

    public LeaveCard()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        _caption.Set(Fonts.QuiteSmall, "LEAVE IN");
        _unit.Set(Fonts.QuiteSmall, "MIN");
        _go.Set(_huge, "GO");
    }

    public CommutePlan Plan { get; set; } = CommutePlan.None;

    /// <summary>Whole walk time in minutes (shown in the detail line).</summary>
    public int WalkMinutes { get; set; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var plan = Plan;
        if (plan.Train is null)
        {
            DrawMissed(frame, bounds, plan.AllMissed);
            return;
        }

        var train = plan.Train;
        float flash = TubeGfx.Wave(_time, 0.6);
        var color = plan.Urgency switch
        {
            Urgency.Relaxed => Relaxed,
            Urgency.Soon => Soon,
            _ => Pixel.Lerp(Hurry.WithBrightness(0.55f), Hurry, flash),
        };

        // Line-coloured spine
        frame.Fill(new Rectangle(bounds.X, bounds.Y + 1, 3, bounds.Height - 2), TubeColors.Lift(train.Color, 120f));

        int x = bounds.X + 8;
        _caption.Draw(frame, x, bounds.Y + 1, TubeGfx.Muted);

        int minutes = plan.LeaveInSeconds / 60;
        bool goNow = plan.Urgency == Urgency.Now && minutes == 0;
        if (minutes != _shownMinutes)
        {
            if (_shownMinutes >= 0) _bumpAt = _time;
            _shownMinutes = minutes;
            _number.Set(_huge, minutes.ToString());
        }

        var numberRect = new Rectangle(x, bounds.Y + 10, DigitsWidth, 37);
        if (goNow)
        {
            frame.FillRoundedRect(new Rectangle(x, numberRect.Y + 2, _go.Width + 6, 33), 3, color);
            _go.Draw(frame, x + 3, numberRect.Y + 3, Pixel.Black);
        }
        else
        {
            frame.PushClip(numberRect);
            float bump = _bumpAt == TimeSpan.MinValue ? 1f : Easing.OutCubic(Math.Clamp((float)((_time - _bumpAt).TotalSeconds / 0.35), 0f, 1f));
            int dy = (int)MathF.Round(-14f * (1f - bump));
            _number.Draw(frame, x, numberRect.Y + dy, color, shadow: true);
            frame.PopClip();
        }

        // Right-hand column: unit, where the train is going and when it comes
        int cx = x + DigitsWidth + 6;
        int maxText = bounds.Right - cx - 2;
        if (!goNow) _unit.Draw(frame, cx, bounds.Y + 11, TubeGfx.Muted);

        string destination = train.Destination;
        if (!ReferenceEquals(destination, _destKey))
        {
            _destKey = destination;
            _dest.Set(Fonts.Small, Fonts.Small.TruncateWithEllipsis(destination.ToUpperInvariant(), maxText));
        }
        _dest.Draw(frame, cx, bounds.Y + 22, TubeGfx.Ink);

        int trainMinutes = train.Minutes(_time);
        if (trainMinutes != _detailMinutes || !ReferenceEquals(train.LineName, _lineKey) || WalkMinutes != _detailWalk)
        {
            _detailMinutes = trainMinutes;
            _lineKey = train.LineName;
            _detailWalk = WalkMinutes;
            _detailLine.Set(Fonts.QuiteSmall, Fonts.QuiteSmall.TruncateWithEllipsis(
                trainMinutes == 0 ? $"{train.LineName} due" : $"{train.LineName} in {trainMinutes} min", maxText));
            _walkLine.Set(Fonts.QuiteSmall, $"{WalkMinutes} min walk");
        }
        _detailLine.Draw(frame, cx, bounds.Y + 37, TubeGfx.Muted);
        _walkLine.Draw(frame, cx, bounds.Y + 45, TubeGfx.Muted);
    }

    private void DrawMissed(FrameBuffer frame, Rectangle bounds, bool allMissed)
    {
        int x = bounds.X + 8;
        _title.Set(Fonts.Big, allMissed ? "NEXT ONE" : "NO TRAINS");
        _title.Draw(frame, x, bounds.Y + 14, allMissed ? Soon : TubeGfx.Muted, shadow: true);
        _sub.Set(Fonts.QuiteSmall, allMissed ? "Due trains are too soon" : "Nothing due right now");
        _sub.Draw(frame, x, bounds.Y + 36, TubeGfx.Muted);
        _shownMinutes = -1;
    }
}

/// <summary>Current weather at a glance: an animated icon, the temperature, rain chance and the day's range.</summary>
internal sealed class WeatherChip : Node
{
    private static readonly (int X, int Y)[] Rays = [(0, -1), (0, 1), (-1, 0), (1, 0), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    private readonly TextRun _temp = new(), _unit = new(), _kind = new(), _rain = new(), _range = new();
    private TimeSpan _time;
    private WeatherSnapshot? _lastSnapshot;
    private bool _fahrenheit;

    public WeatherChip()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public WeatherSnapshot? Snapshot { get; set; }
    public bool Fahrenheit { get; set; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (Snapshot is not { } s)
        {
            DrawCloud(frame, bounds.X + 22, bounds.Y + 14, new Pixel(70, 74, 90));
            return;
        }

        if (!ReferenceEquals(s, _lastSnapshot) || Fahrenheit != _fahrenheit)
        {
            _lastSnapshot = s;
            _fahrenheit = Fahrenheit;
            int t = (int)Math.Round(s.Temp);
            _temp.Set(Fonts.Big, t.ToString());
            _unit.Set(Fonts.QuiteSmall, Fahrenheit ? "F" : "C");
            _kind.Set(Fonts.QuiteSmall, s.Kind.ToString().ToUpperInvariant());
            _rain.Set(Fonts.QuiteSmall, $"{s.PrecipChance}% CHANCE");
            _range.Set(Fonts.QuiteSmall, $"H{Math.Round(s.High)} L{Math.Round(s.Low)}");
        }

        int x = bounds.X + 2;
        DrawIcon(frame, x, bounds.Y + 2, s.Kind, s.IsDay);

        int tx = x + 24;
        _temp.Draw(frame, tx, bounds.Y + 1, TempColor(s.Temp, Fahrenheit), shadow: true);
        _unit.Draw(frame, tx + _temp.Width + 2, bounds.Y + 2, TubeGfx.Muted);

        _kind.Draw(frame, x, bounds.Y + 24, TubeGfx.Ink);
        var rainColor = s.PrecipChance >= 50 ? new Pixel(110, 170, 255) : TubeGfx.Muted;
        _rain.Draw(frame, x, bounds.Y + 33, rainColor);
        _range.Draw(frame, x, bounds.Y + 42, TubeGfx.Muted);
    }

    private static Pixel TempColor(double temp, bool fahrenheit)
    {
        double c = fahrenheit ? (temp - 32) * 5 / 9 : temp;
        return c switch
        {
            < 3 => new Pixel(140, 200, 255),
            < 12 => new Pixel(120, 230, 210),
            < 20 => new Pixel(200, 245, 120),
            < 27 => new Pixel(255, 205, 70),
            _ => new Pixel(255, 110, 60),
        };
    }

    private void DrawIcon(FrameBuffer frame, int x, int y, WeatherKind kind, bool isDay)
    {
        int cx = x + 10, cy = y + 9;
        switch (kind)
        {
            case WeatherKind.Clear:
                DrawSunOrMoon(frame, cx, cy, isDay);
                break;
            case WeatherKind.PartlyCloudy:
                DrawSunOrMoon(frame, cx - 4, cy - 3, isDay, small: true);
                DrawCloud(frame, cx + 2, cy + 2, new Pixel(200, 205, 215));
                break;
            case WeatherKind.Fog:
                for (int i = 0; i < 4; i++)
                {
                    int shift = (int)(MathF.Sin((float)_time.TotalSeconds * 0.8f + i) * 2);
                    frame.Fill(new Rectangle(x + 2 + shift + (i % 2) * 2, y + 3 + i * 4, 15, 2), new Pixel(150, 155, 165));
                }
                break;
            case WeatherKind.Cloudy:
                DrawCloud(frame, cx, cy, new Pixel(170, 175, 190));
                break;
            case WeatherKind.Snow:
                DrawCloud(frame, cx, cy - 3, new Pixel(170, 175, 190));
                for (int i = 0; i < 3; i++)
                {
                    float fall = (float)((_time.TotalSeconds * 4 + i * 1.7) % 6);
                    frame.SetPixel(x + 5 + i * 5 + (int)(MathF.Sin(fall + i) * 1.5f), y + 11 + (int)fall, Pixel.White);
                }
                break;
            case WeatherKind.Thunderstorm:
                DrawCloud(frame, cx, cy - 3, new Pixel(95, 100, 120));
                if (TubeGfx.Wave(_time, 1.7) > 0.85f)
                {
                    frame.Fill(new Rectangle(cx, cy + 3, 2, 3), new Pixel(255, 230, 80));
                    frame.Fill(new Rectangle(cx - 2, cy + 6, 3, 2), new Pixel(255, 230, 80));
                }
                break;
            default: // Drizzle, Rain
                DrawCloud(frame, cx, cy - 3, new Pixel(120, 130, 155));
                int drops = kind == WeatherKind.Rain ? 4 : 3;
                for (int i = 0; i < drops; i++)
                {
                    float fall = (float)((_time.TotalSeconds * 9 + i * 1.9) % 6);
                    frame.SetPixel(x + 4 + i * 4, y + 11 + (int)fall, new Pixel(100, 165, 255));
                }
                break;
        }
    }

    private void DrawSunOrMoon(FrameBuffer frame, int cx, int cy, bool isDay, bool small = false)
    {
        int r = small ? 3 : 4;
        if (isDay)
        {
            var sun = new Pixel(255, 205, 50);
            frame.FillCircle(cx, cy, r, sun);
            float pulse = TubeGfx.Wave(_time, 3);
            var ray = sun.WithBrightness(0.5f + 0.5f * pulse);
            int d = r + 2;
            foreach (var (ux, uy) in Rays)
            {
                int reach = ux != 0 && uy != 0 ? d - 1 : d;
                frame.SetPixel(cx + ux * reach, cy + uy * reach, ray);
            }
        }
        else
        {
            frame.FillCircle(cx, cy, r, new Pixel(225, 228, 240));
            frame.FillCircle(cx + 2, cy - 1, r, Pixel.Black);
        }
    }

    private static void DrawCloud(FrameBuffer frame, int cx, int cy, Pixel color)
    {
        frame.FillCircle(cx - 5, cy + 1, 3, color);
        frame.FillCircle(cx, cy - 1, 4, color);
        frame.FillCircle(cx + 5, cy + 1, 3, color);
        frame.Fill(new Rectangle(cx - 5, cy + 1, 11, 3), color);
    }
}
