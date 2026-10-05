using System.Globalization;
using BdfFontParser;
using System.Numerics;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Ambient;

/// <summary>Weekday and date beside the clock: a bold weekday, an accent bar with a travelling shimmer, then the day and month.</summary>
internal sealed class DateBlock : Node
{
    private readonly HomeState _s;
    private readonly GlyphLine _weekday = new(), _date = new(), _ampm = new();
    private string _weekdayText = "", _dateText = "", _ampmText = "";
    private readonly BdfFont _big = Fonts.Big, _sm = Fonts.Small, _tiny = Fonts.QuiteSmall;

    public DateBlock(HomeState state)
    {
        _s = state;
        Width = 112;
        VAlign = Align.Stretch;
        HAlign = Align.Start;
    }

    public float Alpha = 1f;

    /// <summary>0 normally; 1 once the data chips are showing and AM/PM sits beside the weekday.</summary>
    public float Compact;

    public void SetText(string weekday, string date, string ampm)
    {
        _weekdayText = weekday;
        _dateText = date;
        _ampmText = ampm;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (Alpha <= 0.01f) return;
        _weekday.Set(_big, _weekdayText);
        _date.Set(_sm, _dateText);
        _ampm.Set(_tiny, _ampmText);

        var p = _s.Pal;
        // With the data chips showing, AM/PM moves from above the weekday to beside it, so the block is no taller than in 24-hour mode.
        float inline = Compact;
        int ampmH = _ampmText.Length > 0 ? (int)MathF.Round((_ampm.Height + 2) * (1f - inline)) : 0;
        int total = ampmH + _weekday.Height + 4 + 2 + 4 + _date.Height * 2;
        int x = bounds.X, y = bounds.Y + (bounds.Height - total) / 2;

        if (_ampmText.Length > 0)
        {
            var ap = TextPaint.Solid(Pixel.Lerp(p.Date, p.Accent, 0.4f));
            ap.Alpha = Alpha;
            ap.Shadow = true;
            int ax = x + (int)MathF.Round((_weekday.Width + 5) * inline);
            int ay = y + (int)MathF.Round((ampmH + _weekday.Height - _ampm.Height - ampmH) * inline);
            _ampm.Draw(frame, ax, ay, 1, ap);
        }
        y += ampmH;

        var wp = TextPaint.Vertical(Pixel.Lerp(p.Date, Pixel.White, 0.45f), p.Date);
        wp.Alpha = Alpha;
        wp.Shadow = true;
        wp.Bold = true;
        _weekday.Draw(frame, x, y, 1, wp);
        y += _weekday.Height + 4;

        // Accent bar with a bright shimmer sliding along it.
        const int barW = 44;
        float shimmer = (_s.T * 0.35f * _s.Speed) % 1f;
        for (int i = 0; i < barW; i++)
        {
            float d = MathF.Abs(i / (float)barW - shimmer * 1.4f + 0.2f);
            float glow = Gfx.Saturate(1f - d * 5f);
            var c = Pixel.Lerp(p.Accent, Pixel.White, glow * 0.8f);
            frame.BlendPixel(x + i, y, c, Alpha);
            frame.BlendPixel(x + i, y + 1, c, Alpha * 0.8f);
        }
        y += 2 + 4;

        var dp = TextPaint.Vertical(Pixel.Lerp(p.Time1, Pixel.White, 0.35f), p.Time1);
        dp.Alpha = Alpha;
        dp.Shadow = true;
        _date.Draw(frame, x, y, 2, dp);
    }
}

/// <summary>A 2px line along the bottom edge that fills once a minute, with a glowing head. It is the seconds hand.</summary>
internal sealed class SecondsLine : Node
{
    private readonly HomeState _s;

    public SecondsLine(HomeState state)
    {
        _s = state;
        Height = 3;
        VAlign = Align.End;
        HAlign = Align.Stretch;
    }

    public float Alpha = 1f;

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var p = _s.Pal;
        float prog = (_s.Second + _s.SecondFrac) / 60f;
        int fill = (int)(prog * bounds.Width);
        float flash = _s.MinuteFlash;
        int y = bounds.Bottom - 2;

        var track = Gfx.Dim(p.Accent, 0.10f * Alpha);
        frame.Fill(new Rectangle(bounds.X, y, bounds.Width, 2), track);

        for (int x = 0; x < fill && x < bounds.Width; x++)
        {
            float u = x / (float)bounds.Width;
            var c = Pixel.Lerp(p.A1, p.A2, u);
            c = Pixel.Lerp(c, Pixel.White, flash * 0.7f);
            // The tail brightens towards the head so the line reads as motion.
            float head = Gfx.Saturate(1f - (fill - x) / 60f);
            c = Gfx.Dim(c, (0.45f + 0.55f * head) * Alpha);
            frame.Fill(new Rectangle(bounds.X + x, y, 1, 2), c);
        }

        if (fill > 0 || prog > 0f)
            Gfx.GlowDisc(frame, bounds.X + prog * bounds.Width, y + 1, 7f, Pixel.Lerp(p.A1, Pixel.White, 0.4f), 0.55f * Alpha);
    }
}

/// <summary>
/// Not drawn. Computes the clock, theme and scene for the frame, owns the entrance choreography and positions the hero clock and date.
/// Sits first in the tree so every other node sees this frame's state.
/// </summary>
internal sealed class HomeDirector : Node
{
    private readonly HomePageApp _app;
    private readonly HomeState _s;
    private readonly DigitStrip _strip;
    private readonly DateBlock _date;
    private readonly SecondsLine _line;
    private readonly HomeBackdrop _backdrop;
    private readonly HomeChips _chips;
    private readonly ChipPager _band;

    private readonly Tween<float> _themeMix = new(1f);
    private readonly Tween<float> _dateMix = new(1f);
    // 0 with no chips showing; 1 once the clock has lifted to make room for the chip band underneath.
    private readonly Tween<float> _chipMix = new(0f);
    private float _chipTarget;
    private int _chipSeconds = -1;
    private readonly Tween<float> _enterTime = new(0f), _enterDate = new(0f), _enterLine = new(0f);

    // With chips showing, the clock and date lift by ChipLift pixels and the chip band sits in the strip that frees up above the seconds line.
    private const float ChipLift = 6f;

    private HomePalette _from, _to;
    private string? _theme, _mode, _dateFormat;
    private float _level = -1f;
    private bool _showDate = true;
    private bool _first = true;
    private int _day = -1, _minute = -1;

    public HomeDirector(HomePageApp app, HomeState state, DigitStrip strip, DateBlock date, SecondsLine line, HomeBackdrop backdrop, HomeChips chips, ChipPager band)
    {
        _chips = chips;
        _band = band;
        _app = app;
        _s = state;
        _strip = strip;
        _date = date;
        _line = line;
        _backdrop = backdrop;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        var host = Host!;
        float dt = (float)Math.Min(ctx.Delta.TotalSeconds, 0.1);
        _s.Dt = dt;
        _s.T = (float)ctx.Time.TotalSeconds;
        _s.Speed = Math.Clamp(_app.AmbientSpeed, 1, 10) / 3f;

        var now = host.Time.GetLocalNow();
        _s.Hour = now.Hour + now.Minute / 60f + now.Second / 3600f;
        _s.Second = now.Second;
        _s.SecondFrac = now.Millisecond / 1000f;

        if (_first) Start(host);
        ApplySettings(host);

        if (now.Minute != _minute)
        {
            if (_minute >= 0) _s.MinuteFlash = 1f;
            _minute = now.Minute;
        }
        _s.MinuteFlash = MathF.Max(0f, _s.MinuteFlash - dt / 0.9f);

        string dateFormat = _app.DateFormat ?? "";
        if (now.Day != _day || !string.Equals(dateFormat, _dateFormat, StringComparison.Ordinal))
        {
            _day = now.Day;
            _dateFormat = dateFormat;
            _weekday = now.ToString("dddd", CultureInfo.InvariantCulture).ToUpperInvariant();
            _dateText = dateFormat switch
            {
                "DD/MM" => now.ToString("dd'/'MM", CultureInfo.InvariantCulture),
                "MM/DD" => now.ToString("MM'/'dd", CultureInfo.InvariantCulture),
                _ => now.Day.ToString(CultureInfo.InvariantCulture) + " " + now.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
            };
        }

        ApplyBrightness(now, dt);

        bool h24 = _app.Show24Hour;
        int hr = h24 ? now.Hour : (now.Hour % 12 == 0 ? 12 : now.Hour % 12);
        _strip.Pattern = !h24 && hr < 10 ? "d:dd" : "dd:dd";
        bool animate = !_first;
        if (_strip.Pattern == "dd:dd")
        {
            _strip.SetDigit(0, hr / 10, animate);
            _strip.SetDigit(1, hr % 10, animate);
            _strip.SetDigit(2, now.Minute / 10, animate);
            _strip.SetDigit(3, now.Minute % 10, animate);
        }
        else
        {
            _strip.SetDigit(0, hr, animate);
            _strip.SetDigit(1, now.Minute / 10, animate);
            _strip.SetDigit(2, now.Minute % 10, animate);
        }

        _date.SetText(_weekday, _dateText, h24 ? "" : (now.Hour < 12 ? "AM" : "PM"));

        UpdateChips(host, now);
        Pose();
        _first = false;
    }

    private string _weekday = "", _dateText = "";

    // Brightness and the night fade, eased so crossing the night boundary is a slow dim rather than a jump.
    private void ApplyBrightness(DateTimeOffset now, float dt)
    {
        float target = Math.Clamp(_app.Brightness, 5, 100) / 100f;
        if (_app.FadeAtNight)
        {
            int h = now.Hour, start = Math.Clamp(_app.NightStartHour, 0, 23), end = Math.Clamp(_app.NightEndHour, 0, 23);
            bool night = start == end ? false : start > end ? h >= start || h < end : h >= start && h < end;
            if (night) target *= Math.Clamp(_app.NightBrightness, 5, 100) / 100f;
        }

        _level = _level < 0f ? target : _level + (target - _level) * (1f - MathF.Exp(-dt / 0.8f));
        if (MathF.Abs(_level - target) < 0.004f) _level = target;
        if (Parent?.Parent is { } dimmer && dimmer.Opacity != _level) dimmer.Opacity = _level;
    }

    // Polls for the switched-on chips, the strings they show, and the clock lifting to make room while at least one is visible.
    private void UpdateChips(UiHost host, DateTimeOffset now)
    {
        _app.SyncChipPolls();
        _chips.Refresh(_app, now);

        int seconds = Math.Clamp(_app.ChipSeconds, 3, 30);
        if (seconds != _chipSeconds)
        {
            _chipSeconds = seconds;
            _band.Interval = TimeSpan.FromSeconds(seconds);
        }

        float target = _chips.Visible.Count > 0 ? 1f : 0f;
        if (target != _chipTarget)
        {
            _chipTarget = target;
            host.Animator.Animate(_chipMix, target, TimeSpan.FromMilliseconds(700), Easing.InOutCubic);
        }
    }

    private void Start(UiHost host)
    {
        // Whole scene fades in, then the clock rises, the date slides in and the seconds line draws out, staggered on one timeline.
        if (Parent is { } root)
        {
            root.Opacity = 0f;
            root.AnimateOpacity(1f, TimeSpan.FromMilliseconds(900), Easing.OutCubic);
        }
        host.Animator.Add(Timeline.Parallel(
            Timeline.To(_enterTime, 1f, TimeSpan.FromMilliseconds(900), Easing.OutBack),
            Timeline.Sequence(Timeline.Delay(TimeSpan.FromMilliseconds(320)), Timeline.To(_enterDate, 1f, TimeSpan.FromMilliseconds(800), Easing.OutCubic)),
            Timeline.Sequence(Timeline.Delay(TimeSpan.FromMilliseconds(500)), Timeline.To(_enterLine, 1f, TimeSpan.FromMilliseconds(900), Easing.OutCubic))));
    }

    private void ApplySettings(UiHost host)
    {
        var theme = _app.Theme;
        if (!string.Equals(theme, _theme, StringComparison.Ordinal))
        {
            bool firstTheme = _theme is null;
            _theme = theme;
            _from = _s.Pal;
            _to = HomeThemes.Get(theme);
            if (firstTheme) _s.Pal = _to;
            else
            {
                _themeMix.Set(0f);
                host.Animator.Animate(_themeMix, 1f, TimeSpan.FromMilliseconds(1400), Easing.InOutSine);
            }
        }
        if (_themeMix.IsRunning) _s.Pal = HomePalette.Lerp(_from, _to, _themeMix.Value);
        else if (!_first) _s.Pal = _to;

        var mode = _app.DisplayMode;
        if (!string.Equals(mode, _mode, StringComparison.Ordinal))
        {
            _mode = mode;
            _backdrop.RequestMode(HomeModes.Parse(mode));
        }

        bool showDate = _app.ShowDate;
        if (showDate != _showDate)
        {
            _showDate = showDate;
            host.Animator.Animate(_dateMix, showDate ? 1f : 0f, TimeSpan.FromMilliseconds(700), Easing.InOutCubic);
        }
        else if (_first)
        {
            _dateMix.Set(showDate ? 1f : 0f);
            _showDate = showDate;
        }
    }

    // Time left-aligned beside the date, or centred alone when the date is hidden; the shift between the two is tweened.
    private void Pose()
    {
        var p = _s.Pal;
        int w = _strip.TotalWidth;
        const int leftX = 10, gap = 16;
        float center = (256 - w) / 2f;
        float mix = _dateMix.Value;
        float x = center + (leftX - center) * mix;
        float lift = (1f - _enterTime.Value) * 20f;
        float chipMix = _chipMix.Value;
        float chipLift = ChipLift * chipMix;
        _strip.Position = new Vector2(x, lift - chipLift);
        _s.ChipLeft = mix;
        _date.Compact = chipMix;
        _band.Opacity = chipMix;

        float t = _s.T;
        float shift = 0.5f + 0.5f * MathF.Sin(t * 0.25f * _s.Speed);
        var top = Pixel.Lerp(p.Time0, Pixel.White, _s.MinuteFlash * 0.8f);
        var bottom = Pixel.Lerp(Pixel.Lerp(Pixel.Lerp(p.Time1, p.Time0, 0.3f), p.A2, shift * 0.3f), Pixel.White, _s.MinuteFlash * 0.5f);
        var paint = TextPaint.Vertical(top, bottom);
        paint.Shadow = true;
        paint.ShadowColor = Gfx.Dim(p.Sky0, 0.15f);
        paint.Glow = 0.14f;
        paint.Bold = true;
        _strip.Paint = paint;
        float pulse = 1f - _s.SecondFrac;
        _strip.ColonAlpha = 0.28f + 0.72f * pulse * pulse;

        float dateAlpha = _enterDate.Value * mix;
        _date.Alpha = dateAlpha;
        _date.Position = new Vector2(x + w + gap + (1f - _enterDate.Value) * 36f, -chipLift);
        _line.Alpha = _enterLine.Value;
        _line.Visible = _app.ShowSeconds;
    }
}
