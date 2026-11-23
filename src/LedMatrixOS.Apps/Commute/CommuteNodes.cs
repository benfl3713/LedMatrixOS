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
/// (green, amber, then a flashing red GO plate), with the train you are aiming for beside it and what surrounds it: how long after the
/// previous train it comes (a long gap means a packed train) and how long the wait is if you miss it. Allocation-free once the strings are cached.
/// </summary>
internal sealed class LeaveCard : Node
{
    internal static readonly Pixel Relaxed = new(96, 230, 130);
    internal static readonly Pixel Soon = new(255, 190, 40);
    internal static readonly Pixel Hurry = new(255, 70, 60);

    private const int LeftWidth = 58;

    private readonly BdfFont _huge = Fonts.Big.Scale(2);
    private readonly TextRun _caption = new(), _number = new(), _unit = new(), _dest = new(), _detailLine = new(), _beforeLine = new(), _afterLine = new(),
        _go = new(), _title = new(), _sub = new();
    private TimeSpan _time;
    private TimeSpan _bumpAt = TimeSpan.MinValue;
    private int _shownMinutes = -1;
    private string? _destKey, _lineKey;
    private int _detailMinutes = -1, _detailWidth = -1;
    private int _beforeKey = int.MinValue, _afterKey = int.MinValue;
    private Pixel _beforeColor, _afterColor;

    public LeaveCard()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        _caption.Set(Fonts.QuiteSmall, "LEAVE IN");
        _unit.Set(Fonts.QuiteSmall, "MIN");
        _go.Set(_huge, "GO");
    }

    public CommutePlan Plan { get; set; } = CommutePlan.None;

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

        int x = bounds.X + 7;
        _caption.Draw(frame, x, bounds.Y + 1, TubeGfx.Muted);

        int minutes = plan.LeaveInSeconds / 60;
        bool goNow = plan.Urgency == Urgency.Now && minutes == 0;
        if (minutes != _shownMinutes)
        {
            if (_shownMinutes >= 0) _bumpAt = _time;
            _shownMinutes = minutes;
            _number.Set(_huge, minutes.ToString());
        }

        var numberRect = new Rectangle(x, bounds.Y + 10, LeftWidth - 2, 37);
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
            _unit.Draw(frame, x + _number.Width + 2, bounds.Y + 33, TubeGfx.Muted);
        }

        // Right-hand column: the train (where it goes, when it comes), then the gaps either side of it
        int cx = x + LeftWidth;
        int maxText = bounds.Right - cx - 1;

        string destination = train.Destination;
        if (!ReferenceEquals(destination, _destKey) || maxText != _detailWidth)
        {
            _destKey = destination;
            _dest.Set(Fonts.Small, Fonts.Small.TruncateWithEllipsis(destination.ToUpperInvariant(), maxText));
        }
        _dest.Draw(frame, cx, bounds.Y + 1, TubeGfx.Ink);

        int trainMinutes = train.Minutes(_time);
        if (trainMinutes != _detailMinutes || !ReferenceEquals(train.LineName, _lineKey) || maxText != _detailWidth)
        {
            _detailMinutes = trainMinutes;
            _lineKey = train.LineName;
            _detailWidth = maxText;
            _detailLine.Set(Fonts.QuiteSmall, Fonts.QuiteSmall.TruncateWithEllipsis(
                trainMinutes == 0 ? $"{train.LineName} due" : $"{train.LineName} in {trainMinutes} min", maxText));
        }
        _detailLine.Draw(frame, cx, bounds.Y + 15, TubeGfx.Muted);

        UpdateGapLines(plan, maxText);
        if (_beforeLine.Width > 0) _beforeLine.Draw(frame, cx, bounds.Y + 28, _beforeColor);
        if (_afterLine.Width > 0) _afterLine.Draw(frame, cx, bounds.Y + (_beforeLine.Width > 0 ? 38 : 28), _afterColor);
    }

    // The strings only change when a rounded gap or its verdict does, i.e. on a poll, never per frame.
    private void UpdateGapLines(CommutePlan plan, int maxText)
    {
        int before = plan.GapBeforeSeconds < 0 ? -1 : GapMinutes(plan.GapBeforeSeconds);
        int beforeKey = before < 0 ? -1 : before * 2 + (plan.Busy ? 1 : 0);
        if (beforeKey != _beforeKey)
        {
            _beforeKey = beforeKey;
            _beforeColor = plan.Busy ? Soon : TubeGfx.Muted;
            _beforeLine.Set(Fonts.QuiteSmall, before < 0 ? "" : Fonts.QuiteSmall.TruncateWithEllipsis(
                plan.Busy ? $"Busy: {before} min since last" : $"{before} min since the last", maxText));
        }

        int after = plan.GapAfterSeconds < 0 ? -1 : GapMinutes(plan.GapAfterSeconds);
        int verdict = plan.LongWaitAfter ? 1 : plan.NextIsQuieter ? 2 : 0;
        int afterKey = after < 0 ? -1 : after * 4 + verdict;
        if (afterKey != _afterKey)
        {
            _afterKey = afterKey;
            _afterColor = verdict switch { 1 => Hurry, 2 => Relaxed, _ => TubeGfx.Muted };
            _afterLine.Set(Fonts.QuiteSmall, after < 0 ? "" : Fonts.QuiteSmall.TruncateWithEllipsis(verdict switch
            {
                1 => $"Miss it: {after} min wait!",
                2 => $"Quieter one {after} min later",
                _ => $"Next one {after} min later",
            }, maxText));
        }
    }

    private static int GapMinutes(int seconds) => Math.Max(1, (int)Math.Round(seconds / 60.0));

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

/// <summary>
/// The middle of the commute strip: what is wrong on the commute's lines. Anything worse than minor delays is spelled out in the line's
/// health colour ("JUBILEE: SEVERE DELAYS"), taking turns when several lines are hit; otherwise a quiet "Good service" or "Minor delays".
/// </summary>
internal sealed class CommuteStatusLine : Node
{
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(5);

    private readonly Func<IReadOnlyList<LineStatus>?> _source;
    private readonly List<TextRun> _alerts = new();
    private readonly List<Pixel> _colors = new();
    private readonly TextRun _calm = new();
    private Pixel _calmColor;
    private IReadOnlyList<LineStatus>? _last;
    private int _width = -1;
    private TimeSpan _time;

    public CommuteStatusLine(Func<IReadOnlyList<LineStatus>?> source)
    {
        _source = source;
        Grow = 1;
        VAlign = Align.Stretch;
    }

    /// <summary>How many lines are being called out.</summary>
    public int AlertCount => _alerts.Count;

    /// <summary>True for the states a commuter should hear about: anything but good service and minor delays.</summary>
    public static bool Notable(Health h) => h is not (Health.Good or Health.Minor);

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var statuses = _source();
        if (!ReferenceEquals(statuses, _last) || bounds.Width != _width) Rebuild(statuses, bounds.Width);

        var run = _calm;
        var color = _calmColor;
        if (_alerts.Count > 0)
        {
            int slot = _alerts.Count == 1 ? 0 : (int)(_time.Ticks / Dwell.Ticks % _alerts.Count);
            run = _alerts[slot];
            color = _colors[slot];
        }

        if (run.Width > 0) run.Draw(frame, bounds.X, bounds.Y + (bounds.Height - run.Height) / 2, color);
    }

    private void Rebuild(IReadOnlyList<LineStatus>? statuses, int width)
    {
        _last = statuses;
        _width = width;
        _alerts.Clear();
        _colors.Clear();
        var font = Fonts.QuiteSmall;
        bool minor = false;

        // Worst first, so the first thing on show is the thing that matters most
        foreach (var s in (statuses ?? []).OrderByDescending(s => s.Health))
        {
            if (s.Health == Health.Minor) minor = true;
            if (!Notable(s.Health)) continue;
            var run = new TextRun();
            run.Set(font, font.TruncateWithEllipsis($"{s.Name}: {s.Description}".ToUpperInvariant(), width));
            _alerts.Add(run);
            _colors.Add(s.Health.Color());
        }

        bool known = statuses is { Count: > 0 };
        _calm.Set(font, !known ? "" : minor ? "Minor delays" : "Good service");
        _calmColor = (minor ? LineHealth.MinorColor : LineHealth.GoodColor).WithBrightness(0.75f);
    }
}

/// <summary>Current weather at a glance, in a narrow column: an animated icon, the temperature, rain chance and the day's range.</summary>
internal sealed class WeatherChip : Node
{
    private static readonly (int X, int Y)[] Rays = [(0, -1), (0, 1), (-1, 0), (1, 0), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    private readonly TextRun _temp = new(), _unit = new(), _rain = new(), _range = new();
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
            _rain.Set(Fonts.QuiteSmall, $"{s.PrecipChance}% RAIN");
            _range.Set(Fonts.QuiteSmall, $"H{Math.Round(s.High)} L{Math.Round(s.Low)}");
        }

        int x = bounds.X + 2;
        DrawIcon(frame, x, bounds.Y + 2, s.Kind, s.IsDay);

        int tx = x + 24;
        _temp.Draw(frame, tx, bounds.Y + 1, TempColor(s.Temp, Fahrenheit), shadow: true);
        _unit.Draw(frame, tx + _temp.Width + 2, bounds.Y + 2, TubeGfx.Muted);

        var rainColor = s.PrecipChance >= 50 ? new Pixel(110, 170, 255) : TubeGfx.Muted;
        _rain.Draw(frame, x, bounds.Y + 28, rainColor);
        _range.Draw(frame, x, bounds.Y + 38, TubeGfx.Muted);
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
