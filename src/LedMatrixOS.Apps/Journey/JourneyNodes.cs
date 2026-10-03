using BdfFontParser;
using LedMatrixOS.Apps.Commute;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Journey;

/// <summary>
/// The "LEAVE IN n MIN" hero, in the Commute style: green, amber, then a flashing GO plate as the time runs out, and a MISSED state when
/// every suggested journey has already gone. Allocation-free once the strings are cached.
/// </summary>
internal sealed class JourneyHero : Node
{
    private const int DigitsWidth = 40;

    private readonly BdfFont _huge = Fonts.Big.Scale(2);
    private readonly TextRun _caption = new(), _number = new(), _unit = new(), _go = new(), _title = new(), _sub = new();
    private TimeSpan _time;
    private TimeSpan _bumpAt = TimeSpan.MinValue;
    private int _shownMinutes = -1;

    public JourneyHero()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        _caption.Set(Fonts.QuiteSmall, "LEAVE IN");
        _unit.Set(Fonts.QuiteSmall, "MIN");
        _go.Set(_huge, "GO");
        _title.Set(Fonts.Big, "MISSED");
        _sub.Set(Fonts.QuiteSmall, "Refreshing");
    }

    public JourneyPlan Plan { get; set; } = JourneyPlan.None;

    /// <summary>Colour of the spine: the first vehicle's line colour.</summary>
    public Pixel Spine { get; set; } = TubeGfx.Muted;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var plan = Plan;
        int x = bounds.X + 8;
        if (plan.Missed)
        {
            _title.Draw(frame, x - 2, bounds.Y + 14, LeaveCard.Soon, shadow: true);
            _sub.Draw(frame, x - 2, bounds.Y + 36, TubeGfx.Muted);
            _shownMinutes = -1;
            return;
        }

        float flash = TubeGfx.Wave(_time, 0.6);
        var color = plan.Urgency switch
        {
            Urgency.Relaxed => LeaveCard.Relaxed,
            Urgency.Soon => LeaveCard.Soon,
            _ => Pixel.Lerp(LeaveCard.Hurry.WithBrightness(0.55f), LeaveCard.Hurry, flash),
        };

        frame.Fill(new Rectangle(bounds.X, bounds.Y + 1, 3, bounds.Height - 2), TubeColors.Lift(Spine, 120f));
        _caption.Draw(frame, x, bounds.Y + 1, TubeGfx.Muted);

        int minutes = Math.Min(99, plan.LeaveInSeconds / 60);
        bool goNow = minutes == 0;
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
            return;
        }

        frame.PushClip(numberRect);
        float bump = _bumpAt == TimeSpan.MinValue ? 1f : Easing.OutCubic(Math.Clamp((float)((_time - _bumpAt).TotalSeconds / 0.35), 0f, 1f));
        int dy = (int)MathF.Round(-14f * (1f - bump));
        _number.Draw(frame, x, numberRect.Y + dy, color, shadow: true);
        frame.PopClip();
        _unit.Draw(frame, x + DigitsWidth + 6, bounds.Y + 11, TubeGfx.Muted);
    }
}

/// <summary>
/// One suggested journey: its legs as a row of coloured pills (walk, line codes, bus roundels), the total duration, the arrival time and
/// when and where the first vehicle leaves. A disrupted leg pulses and flashes an amber frame.
/// </summary>
internal sealed class JourneyPage : Node
{
    private const int PillHeight = 11, PillGap = 3, MaxWidth = 176;

    private readonly struct Chip(int x, int width, Pixel color, Pixel ink, TextRun run, bool disrupted)
    {
        public readonly int X = x, Width = width;
        public readonly Pixel Color = color, Ink = ink;
        public readonly TextRun Run = run;
        public readonly bool Disrupted = disrupted;
    }

    private readonly Chip[] _pills;
    private readonly TextRun _duration = new(), _unit = new(), _arrLabel = new(), _arrival = new(), _dep = new(), _tag = new();
    private TimeSpan _time;

    public JourneyPage(JourneyOption option, int index, int count)
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;

        var pills = new List<Chip>();
        int x = 4;
        for (int i = 0; i < option.Legs.Length; i++)
        {
            var leg = option.Legs[i];
            var run = new TextRun();
            run.Set(Fonts.QuiteSmall, leg.Label);
            int width = run.Width + 6;
            if (x + width > MaxWidth)
            {
                var more = new TextRun();
                more.Set(Fonts.QuiteSmall, $"+{option.Legs.Length - i}");
                pills.Add(new Chip(x, more.Width + 6, new Pixel(48, 50, 62), TubeGfx.Muted, more, false));
                break;
            }

            var color = JourneyParser.ColorOf(leg);
            var ink = leg.Walking ? TubeGfx.Muted : TubeColors.TextOn(color);
            pills.Add(new Chip(x, width, color, ink, run, leg.Disrupted));
            x += width + PillGap;
        }
        _pills = pills.ToArray();

        _duration.Set(Fonts.Big, option.Minutes.ToString());
        _unit.Set(Fonts.QuiteSmall, "min");
        _arrLabel.Set(Fonts.QuiteSmall, "ARR");
        _arrival.Set(Fonts.Big, option.Arrival.ToString("HH:mm"));

        var transit = option.FirstTransit;
        _dep.Set(Fonts.QuiteSmall, transit is null
            ? $"Walk {option.Minutes} min"
            : Fonts.QuiteSmall.TruncateWithEllipsis($"{transit.Departure:HH:mm} from {TflApi.StripStationSuffix(transit.From)}", 120));
        if (count > 1) _tag.Set(Fonts.QuiteSmall, index == 0 ? $"BEST 1/{count}" : $"ALT {index + 1}/{count}");
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float wave = TubeGfx.Wave(_time, 1.1);
        foreach (var pill in _pills)
        {
            var rect = new Rectangle(bounds.X + pill.X, bounds.Y, pill.Width, PillHeight);
            var fill = pill.Disrupted ? pill.Color.WithBrightness(0.45f + 0.55f * wave) : pill.Color;
            frame.FillRoundedRect(rect, 2, fill);
            if (pill.Disrupted && wave > 0.35f) TubeGfx.OutlineRound(frame, rect, LineHealth.MinorColor);
            pill.Run.Draw(frame, rect.X + (rect.Width - pill.Run.Width) / 2, rect.Y + (rect.Height - pill.Run.Height) / 2 + 1, pill.Disrupted ? Pixel.White : pill.Ink);
        }

        int x = bounds.X + 4;
        _duration.Draw(frame, x, bounds.Y + 13, TubeGfx.Ink, shadow: true);
        _unit.Draw(frame, x + _duration.Width + 3, bounds.Y + 24, TubeGfx.Muted);

        int right = bounds.Right - 4;
        _arrival.Draw(frame, right - _arrival.Width, bounds.Y + 13, TubeGfx.Amber, shadow: true);
        _arrLabel.Draw(frame, right - _arrival.Width - _arrLabel.Width - 3, bounds.Y + 24, TubeGfx.Muted);

        _dep.Draw(frame, x, bounds.Y + 33, TubeGfx.Muted);
        if (_tag.Width > 0) _tag.Draw(frame, right - _tag.Width, bounds.Y + 33, new Pixel(110, 110, 125));
    }
}

internal enum JourneyState { NotConfigured, Loading, Offline, CheckAddress, NoRoute }

/// <summary>Full-board message with the roundel, for the states where there is no journey to show.</summary>
internal sealed class JourneyStateScreen : Node
{
    private readonly TextRun _title = new(), _subtitle = new();
    private TimeSpan _time;
    private JourneyState _state = JourneyState.Loading;

    public JourneyStateScreen()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        State = JourneyState.Loading;
    }

    public JourneyState State
    {
        get => _state;
        set
        {
            _state = value;
            _title.Set(Fonts.Big, value switch
            {
                JourneyState.NotConfigured => "NO JOURNEY",
                JourneyState.Loading => "PLANNING",
                JourneyState.Offline => "NO DATA",
                JourneyState.CheckAddress => "CHECK ADDRESS",
                _ => "NO ROUTE",
            });
            _subtitle.Set(Fonts.Small, value switch
            {
                JourneyState.NotConfigured => "Set From and To in the app",
                JourneyState.Loading => "Finding the best route",
                JourneyState.Offline => "Retrying shortly",
                JourneyState.CheckAddress => "Check From / To in the app",
                _ => "Try the All modes setting",
            });
        }
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int cx = bounds.X + 34, cy = bounds.Y + bounds.Height / 2;
        bool loading = _state == JourneyState.Loading;
        float breathe = TubeGfx.Wave(_time, 2.4);
        float chase = (float)(_time.TotalSeconds / 1.6 % 1.0);

        var ring = _state == JourneyState.Offline ? LineHealth.SevereColor : TubeGfx.Roundel;
        TubeGfx.DrawRoundel(frame, cx, cy, 19, 5, loading ? ring : ring.WithBrightness(0.6f + 0.4f * breathe), TubeGfx.RoundelBar, loading ? chase : -1f);

        int x = bounds.X + 68;
        int top = cy - (_title.Height + 2 + _subtitle.Height) / 2;
        var titleColor = _state == JourneyState.Offline ? LineHealth.SevereColor : TubeGfx.Amber;
        _title.Draw(frame, x, top, titleColor, shadow: true);

        if (loading)
        {
            int dots = 1 + (int)(_time.TotalSeconds * 2.5) % 3;
            int dx = x + _title.Width + 6;
            for (int i = 0; i < 3; i++)
                frame.Fill(new Rectangle(dx + i * 6, top + _title.Height - 5, 3, 3), i < dots ? titleColor : titleColor.WithBrightness(0.2f));
        }

        _subtitle.Draw(frame, x, top + _title.Height + 2, TubeGfx.Muted);
    }
}
